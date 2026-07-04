# Server-Side Events Pagination, Filtering & Search Design

**Date:** 2026-07-04
**Status:** Approved

## Overview

Replace the "return every event, paginate client-side" behavior of `GET /api/events` (a documented known limitation) with keyset-cursor pagination, server-side search, and purpose-built read models for the chart consumers that currently depend on the full list being in browser memory. Strava auto-sync will grow per-athlete event counts; this pays the debt before it compounds.

## Decisions Made

| Decision | Choice | Rationale |
|---|---|---|
| Pagination UX | Infinite scroll / Load more | Matches recent-first browsing; pairs with keyset |
| Pagination mechanism | Keyset cursor on `(EventDate DESC, Id DESC)` | Stable under Strava backfills inserting old-dated events; index-friendly; no deep-page `OFFSET` degradation |
| Search | Server-side, case-insensitive on `EventName` | Client-side search over loaded pages would silently miss unloaded events |
| Chart consumers | Purpose-built aggregate endpoints (not fetch-all escape hatch, not one mega dashboard endpoint) | Three of four `useEvents` consumers are aggregate readers; escape hatch keeps the full transfer on every dashboard load; a combined endpoint couples four concerns against the vertical-slice grain |
| List payload | Splits excluded (`EventSummaryResponse`) | The `Include(e => e.Splits)` join is the heaviest part of today's list query; no list consumer renders splits |
| Contract compatibility | Breaking change, migrated in the same effort | The only consumer is our own frontend |

## Architecture

### `GET /api/events` — new contract

```
GET /api/events?limit=20&cursor=<opaque>&eventType=&from=&to=&search=
→ { "items": [EventSummaryResponse, ...], "nextCursor": "..." | null }
```

- `EventSummaryResponse` = existing event fields minus splits.
- **Cursor**: base64url-encoded `(EventDate, Id)` of the last item. Keyset predicate `WHERE (EventDate, Id) < (@date, @id)` under `ORDER BY EventDate DESC, Id DESC` (EF translation: `e.EventDate < d || (e.EventDate == d && comparison on Id)`). The Guid tiebreaker only needs to be *consistent* within PostgreSQL — the cursor is opaque and never compared client-side. Malformed cursor → `400` via FluentValidation.
- **Limits**: default 20, max 100 — named constants in the validator.
- **Search**: `EF.Functions.ILike(e.EventName, pattern)` with `%` and `_` escaped in the user input; parameterized by EF (no injection surface); max length 100 validated.
- Existing `eventType` / `from` / `to` filters compose with search and cursor in the same query.

### New migration

Composite index `(UserId, EventDate DESC, Id DESC)` on `Events` — serves the keyset walk, the timeline endpoint, and existing date-range filters. Applied via the `/ef-migrate` workflow.

### New read models

| Route | Auth | Serves | Shape |
|---|---|---|---|
| `GET /api/events/timeline` | authorized | `ProgressChart`, `PredictionTeaser`, `PredictPage`, `RaceComparison` | `[{ id, eventDate, eventType, elapsedSecs, completion }]` — no splits or names; a few KB at thousands of events; optional `eventType` filter |
| `GET /api/events/{id}` (extended) | authorized | `EventDetailPage` split comparison | Response gains `averageSplits`: per-split-index average seconds across the athlete's Finished events of the same type, computed in SQL (`GROUP BY` split index) — replaces client-side `computeAverageSplits` over the full list |

`GET /api/events/personal-bests` and `GET /api/events/prediction` are untouched.

### Frontend migration

- `useEvents` → `useInfiniteQuery` with `getNextPageParam: (lastPage) => lastPage.nextCursor`.
- `DashboardPage`: client-side slicing replaced by Load-more / intersection-observer; search box becomes a debounced (300 ms named constant) server param that resets the cursor; type/date filters likewise reset it.
- New `useTimeline` hook feeds the three chart consumers; `EventDetailPage` reads `averageSplits` from the detail response and drops its full-list dependency.
- Upload/create/delete mutations invalidate the infinite events query, timeline, and PB caches together.
- **Targeted cleanup**: MSW handlers are rewritten for the new contract — the natural moment to fix the documented casing drift (`'Marathon'`/`'csv'` → `'MARATHON'`/`'CSV'`) so mocks match the real API.

### Error handling

House pattern (catch → `LogCritical` → rethrow). Validation failures (`limit` out of bounds, malformed cursor, oversized search) surface as RFC 7807 `400` via the existing `ValidationExceptionHandler`.

## Testing

- **Unit**: cursor codec round-trip; tampered/truncated cursor rejected; validator branches (limit 0 / 101 / valid, search length, cursor format); ILike escaping (`"50%_run"` matches literally).
- **Integration**: full page-walk visits every seeded event exactly once, in `EventDate DESC` order; **stability test** — insert an older-dated event between two page fetches and assert no skip or duplicate (the reason keyset was chosen); filters + search + cursor composed; timeline shape and `completion` values; `averageSplits` against hand-computed values; auth + user-scoping on every endpoint.
- **Frontend (Vitest + MSW)**: MSW handlers on the new paginated contract; `DashboardPage` renders page 1, loads more on scroll, debounces search; chart components against timeline mocks.
- **E2E (Playwright)**: dashboard shows first page; scrolling loads more; searching finds an event that lives beyond page 1.

## Sequencing note

This feature should land **before or alongside** Strava webhook auto-sync (`2026-07-04-strava-webhook-sync-design.md`), which increases per-athlete event volume, and before the race-goals trend work, whose `PredictPage` overlay pairs naturally with the `timeline` read model.
