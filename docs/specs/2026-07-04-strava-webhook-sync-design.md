# Strava Webhook Auto-Sync Design

**Date:** 2026-07-04
**Status:** Approved

## Overview

Extend the existing Strava integration from manual pull (athlete browses `/sync` and confirms imports) to push-driven awareness: Strava webhooks notify Pacevite when activities are created or deleted and when an athlete deauthorizes the app. New activities surface as a "pending" badge and **New** markers on the existing live `/sync` activity list — Pacevite does not build a parallel copy of Strava data.

## Decisions Made

| Decision | Choice | Rationale |
|---|---|---|
| Delivery mechanism | Real Strava webhooks (public HTTPS callback) | Public deployment exists/planned; webhooks are the honest "automatic" design |
| Import policy | Pending inbox — athlete confirms on `/sync` | Keeps event history clean of junk activities; reuses the existing confirm flow |
| Webhook scope | Creates + deauthorization; deletes clean up **unconfirmed pending rows only**; activity updates ignored | Import semantics, not sync semantics: once confirmed, the athlete owns the Pacevite copy. Deauth handling is mandated by Strava's API agreement |
| Architecture | Thin webhook + live inbox | `/sync` already lists importable activities live from Strava; the webhook only needs to answer "anything new?" and "was access revoked?". One endpoint + one small table, no background worker |
| Inline/background processing | Neither — no detail fetch at webhook time | Webhook payload carries only IDs; details come from the existing live `ListActivities` call when the athlete opens `/sync` |
| Subscription lifecycle | One-time ops step, outside the app | `POST https://www.strava.com/api/v3/push_subscriptions` with `callback_url` + `verify_token`; the API should not need to know its own public URL |

## Architecture

### New domain entity

```
PendingSyncActivity
  Id                  Guid PK
  SyncConnectionId    Guid FK → SyncConnections (cascade delete)
  ExternalActivityId  long
  ReceivedAt          DateTimeOffset
  UNIQUE (SyncConnectionId, ExternalActivityId)   -- idempotent webhook retries
```

The unique index makes `create` processing an upsert, so Strava retries (we return 500 on processing failure, which triggers a retry) are harmless.

`Event.SyncConnectionId` must use `OnDelete(SetNull)` so deleting a `SyncConnection` on deauth preserves confirmed events (verify current behavior; migrate if it is cascade).

### New vertical slice — `Features/Sync/Webhook/` (+ pending-list handlers)

| Route | Auth | Handler | Purpose |
|---|---|---|---|
| `GET /api/sync/strava/webhook` | anon | `VerifyWebhookSubscription` | Strava's one-time challenge: validate `hub.verify_token` against config `Strava:WebhookVerifyToken`, echo `{"hub.challenge": "..."}`; `403` on mismatch |
| `POST /api/sync/strava/webhook` | anon | `ProcessWebhookEvent` | Dispatch on `object_type` / `aspect_type` (see data flow) |
| `GET /api/sync/strava/pending` | authorized | `GetPendingSync` | Pending activity IDs + count for the frontend badge |
| `DELETE /api/sync/strava/pending/{activityId}` | authorized | `DismissPendingActivity` | Dismiss without importing |

New config key: `Strava:WebhookVerifyToken` (required when webhooks enabled; from environment in production). New optional config `Strava:WebhookSubscriptionId` — when set, POST events whose `subscription_id` does not match are logged at Warning and ACKed with `200` (do not invite retries for traffic we will never process). When unset, the check is skipped: the subscription ID only exists *after* subscription creation, which itself requires the GET challenge endpoint to already be live (bootstrap ordering — see ops runbook).

### Anonymous-route justification (security)

The two webhook routes are anonymous by necessity — Strava's servers call them (documented exception, same category as `/health`). Compensating controls:

- Verify-token gate on GET; `subscription_id` check on POST.
- Dedicated rate-limiter policy on the webhook route (fixed window, configured like the existing `auth` policy).
- The POST handler performs no privileged reads and returns no data. Worst case, a forged event creates a badge entry for an activity ID that the live Strava list will not corroborate, or removes a pending badge entry.
- Payload is bound to a typed contract and validated (unknown `object_type` / `aspect_type` → log Warning, `200`).

### Data flow

1. Athlete finishes an activity → Strava POSTs `{ object_type: "activity", aspect_type: "create", object_id, owner_id, subscription_id, event_time }`.
2. Handler maps `owner_id` → `SyncConnections.ExternalAthleteId`. Unknown athlete → log Warning, return `200`.
3. Dispatch:
   - activity `create` → upsert `PendingSyncActivity`.
   - activity `delete` → delete matching pending row if present (confirmed events untouched).
   - activity `update` → ignored by design.
   - athlete event with `updates.authorized == "false"` (deauth) → delete the `SyncConnection`; pending rows go with it (cascade); confirmed events survive via `SetNull`.
4. Frontend badge: `GetPendingSync` via TanStack Query with periodic refetch; `/sync` marks live-list rows whose ID is pending as **New**; confirming (existing `ConfirmActivity` flow) or dismissing deletes the pending row. `ConfirmStravaActivityHandler` gains one step: remove the pending row for the confirmed `ExternalActivityId`.

### Error handling

House pattern: catch → `LogCritical` → rethrow. A `500` from processing makes Strava retry; idempotent upsert absorbs the retry. Logs contain Strava numeric IDs and counts only — no PII.

### Frontend

- New hook `useSyncPending` (count + IDs).
- Nav/`SyncPage` badge; **New** chip on activity rows; dismiss button per row.
- Confirm/dismiss mutations invalidate the pending query.

## Testing

- **Unit** (`ProcessWebhookEvent` branches): create → upsert; duplicate create → no-op; delete with/without pending row; deauth → connection + pending removed, events retained; unknown subscription id → ignored + Warning; unknown athlete → ignored + Warning; malformed body → validation error.
- **Integration** (WebApplicationFactory + Testcontainers): GET challenge echoes `hub.challenge` with correct token and `403`s a bad token; POST create persists a pending row; POST deauth wipes connection + pending rows, confirmed events survive; confirm and dismiss both clear pending; `GET /pending` and `DELETE /pending/{id}` require auth and are user-scoped.

## Ops runbook (documented, not automated)

1. Deploy with `Strava:WebhookVerifyToken` set.
2. `curl -X POST https://www.strava.com/api/v3/push_subscriptions -F client_id=... -F client_secret=... -F callback_url=https://<public-host>/apis/pacevite/api/sync/strava/webhook -F verify_token=<token>`.
3. Record the returned subscription `id` in `Strava:WebhookSubscriptionId`.
