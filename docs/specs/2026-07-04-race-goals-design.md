# Race Goals + AI Pacing Plan Design

**Date:** 2026-07-04
**Status:** Approved

## Overview

Let an athlete declare a target race ("Marathon on 2026-10-12, goal 3:45:00"). Pacevite shows a countdown and gap-to-goal (current prediction vs target), an AI pacing plan built from the athlete's own split history, a prediction-vs-goal trend over time, and makes the AI coach goal-aware via a new chat tool. When race day arrives, the goal auto-matches the actual result and reports Achieved or Missed.

## Decisions Made

| Decision | Choice | Rationale |
|---|---|---|
| Goal cardinality | One active goal per event type | Supports the A-race + tune-up-race pattern; maps 1:1 onto the per-event-type prediction engine |
| Scope | Goal card + countdown, AI pacing plan, coach chat awareness, gap-over-time trend | All four selected; each independently shippable |
| Goal lifecycle | Auto-match result on race day | Event of the goal's type within ±3 days of `RaceDate` links to the goal → Achieved/Missed with margin. Closes the loop |
| Trend machinery | Recompute-as-of, on demand | Prediction is a deterministic linear regression over stored events — history is replayed, not stored. No new tables, no write-path coupling, self-correcting on event deletion |
| Status storage | Derived at read time, never persisted | Pure function of `(goal, events, today)`; avoids GET-with-side-effects and drift |
| Pacing plan numbers | Computed in code; LLM writes commentary only | Split targets are arithmetic athletes will race by — the LLM must not do the math |
| Pacing plan caching | Cached on the goal row with an input fingerprint | LLM output is nondeterministic and costs money; regenerate only when inputs change |

## Architecture

### Data model

```
RaceGoal
  Id                       Guid PK
  UserId                   string (app-level FK, per ADR 0006)
  EventType                EventType
  RaceName                 string
  RaceDate                 DateOnly
  TargetSecs               int
  CreatedAt                DateTimeOffset
  PacingPlanJson           string?      -- cached LLM output + computed splits
  PacingPlanFingerprint    string?
  PacingPlanGeneratedAt    DateTimeOffset?
```

No `Status` column. Derived status (named constants `MatchWindowDays = 3`, `ExpiryGraceDays = 7`):

- **Achieved / Missed** — a Finished event of the goal's `EventType` exists within ±`MatchWindowDays` of `RaceDate`; the closest by date is the matched result; `ElapsedSecs <= TargetSecs` → Achieved, else Missed (margin reported either way).
- **Expired** — `RaceDate + ExpiryGraceDays` has passed with no matching event.
- **Active** — otherwise.

Uniqueness ("one active per event type") is enforced in the create handler — an existing goal of the same type that is not yet Achieved/Missed/Expired → `409 Conflict`. It cannot be a DB partial index because status is derived; app-level enforcement is acceptable at this scale and is integration-tested.

### Shared refactor — `PredictionCalculator`

The linear regression currently embedded in `GetPredictionHandler` is extracted to a pure static `PredictionCalculator`. Consumers: existing prediction endpoint, the goal gap computation, and the trend endpoint. Single source of truth — the trend must never disagree with the prediction card.

### New vertical slice — `Features/Goals/`

| Route | Auth | Purpose |
|---|---|---|
| `POST /api/goals` | authorized | Create. Validator: `RaceDate` in the future, `TargetSecs` within sane bounds, valid `EventType`; duplicate active goal → `409` |
| `GET /api/goals` | authorized | All goals with derived status, days-to-race, matched event (if any), and gap = current `PredictionCalculator` output − `TargetSecs` |
| `DELETE /api/goals/{id}` | authorized | Abandon. No edit endpoint in v1 — delete + recreate (YAGNI) |
| `GET /api/goals/{id}/trend` | authorized | Recompute-as-of series: for each Finished event of the type from the **second** onward (a fit needs ≥ 2 points), the regression prediction using only events up to and including that date, plus the target line |
| `GET /api/goals/{id}/pacing-plan` | authorized | Cached AI pacing plan (below) |

All queries filter by the authenticated `UserId`; a goal belonging to another user → `404`.

### Pacing plan

1. Compute average split *proportions* across the athlete's Finished events of the goal's type (server-side equivalent of the frontend's `computeAverageSplits`), scale to `TargetSecs`. No split history → even splits with a note in the commentary prompt.
2. Fingerprint = `(TargetSecs, RaceDate, latest event Id of the type)`. Cache hit → return `PacingPlanJson` without touching Anthropic.
3. Cache miss → Anthropic call (existing `PredictionCoaching` pattern) generates strategy commentary *around* the precomputed numbers; result + fingerprint persisted on the goal row.
4. Anthropic failure mirrors `PredictionCoaching` semantics: log Critical, rethrow. No silent degradation.

### Chat tool

New `GetRaceGoalsToolHandler` (`get_race_goals`) via the `/add-chat-tool` pattern: returns the athlete's goals with derived status, days-to-race, target, and gap. Registered alongside the existing four handlers.

### Frontend

- `DashboardPage`: goal card per goal — race name, countdown, target vs current predicted time, status chip (Active/Achieved/Missed/Expired), link to pacing plan.
- Goal create form (event type, race name, date, target time).
- `PredictPage`: Recharts `ReferenceLine` at the target + the as-of trend series overlay.
- Pacing plan panel: computed split table + AI commentary.
- New hooks: `useGoals`, `useGoalTrend`, `usePacingPlan`.

### Error handling

House pattern throughout (catch → `LogCritical` → rethrow). Trend endpoint with < 2 usable events → `409 Conflict`, consistent with the existing prediction endpoint's minimum-data behavior.

## Testing

- **Unit**: status derivation — all four states, window boundaries (exactly ±3 days, grace day 7), two candidate events → closest wins, matched event deleted → reverts; trend prefix regressions against hand-computed values; split scaling with full/partial/no history; fingerprint invalidation on each input change; validator branches (past date, zero/negative target, bad enum); chat tool handler with stubbed data.
- **Integration**: goal CRUD with auth and user-scoping (cross-user `404`); duplicate-active → `409`; trend over seeded events; pacing plan generated once then served from cache on the second call (Anthropic stubbed as in existing chat tests); goal gap equals `/events/prediction` output for the same data.
