# Performance Prediction Design

**Date:** 2026-04-25
**Status:** Approved

## Overview

Add a performance prediction feature to Pacevite. Given a user's historical finished events of a given type, the system projects a finish time for their next race using linear regression, then optionally generates a split-level coaching analysis via Claude (AI).

## Decisions Made

| Decision | Choice | Rationale |
|---|---|---|
| Prediction method | Linear regression over (date, elapsed_secs) | Statistically sound, handles uneven race cadence, produces R² for confidence |
| AI coaching | Separate on-demand SSE stream | Keeps page fast; AI call only fires when user explicitly requests it |
| Placement | Dedicated `/predict` page + Dashboard teaser | AI response needs vertical space; teaser gives zero-friction visibility on Dashboard |
| Minimum data requirement | 2 finished events of same type | Can't fit a meaningful trend on 1 point; 409 Conflict below threshold |
| Streaming format | Existing `SseEvent` infrastructure (`Delta` / `Done` / `Error`) | Already in codebase; consistent with future chat feature |

## Architecture

### API — Two new endpoints

#### `GET /api/events/prediction?eventType={type}` → JSON (Mediator)

Query handled by `GetPredictionHandler`. Filters events by `UserId`, `EventType`, `Completion = Finished`, ordered by `EventDate`.

**Response shape:**
```json
{
  "eventType": "HYROX",
  "predictedSecs": 4432,
  "confidenceLabel": "High",
  "avgImprovementSecs": 215,
  "dataPoints": [
    { "eventId": "...", "eventDate": "2023-10-14", "elapsedSecs": 4930, "fittedSecs": 4920 },
    { "eventId": "...", "eventDate": "2024-03-09", "elapsedSecs": 4724, "fittedSecs": 4710 },
    { "eventId": "...", "eventDate": "2024-11-16", "elapsedSecs": 4501, "fittedSecs": 4508 },
    { "eventId": null,  "eventDate": "2026-04-25", "elapsedSecs": null, "fittedSecs": 4432 }
  ]
}
```

The final data point has `eventId: null` and `elapsedSecs: null` — it is the projected "today" point, used by the frontend to render the dashed trend extension.

**Error cases:**
- `< 2` finished events of the type → `409 Conflict` with `{ "message": "Need at least 2 finished HYROX events to predict" }`
- All events on the same date (slope undefined) → `409 Conflict`

#### `GET /api/events/prediction/coaching?eventType={type}` → SSE stream (bypasses Mediator)

Registered directly in `EventEndpoints.cs`. Not Mediator — streaming responses don't fit request-response.

Streams `SseEvent.Delta` tokens as Claude generates the response, then `SseEvent.Done`. On failure: `SseEvent.Error`.

Response headers: `Content-Type: text/event-stream`, `Cache-Control: no-cache`, `X-Accel-Buffering: no`.

### Algorithm — Linear Regression

Inputs: list of `(daysSinceFirst: double, elapsedSecs: double)` pairs, minimum 2 points.

Outputs:
- `predictedSecs` — regression value at `(today - firstEventDate).TotalDays`
- `fittedSecs` per historical point — for the chart trend line
- `R²` — coefficient of determination, used to derive confidence label
- `avgImprovementSecs` — mean of consecutive deltas (communicated to user as "avg improvement per race")

**Confidence label:**

| Condition | Label |
|---|---|
| R² ≥ 0.85 and ≥ 3 events | `High` |
| R² ≥ 0.60 or exactly 2 events | `Medium` |
| R² < 0.60 | `Low` |

**Floor clamping:** The predicted time is clamped to a per-type minimum (world-record-adjacent floor) to prevent nonsensical projections for athletes on steep improvement curves. Clamped predictions are always returned with `Low` confidence regardless of R².

Per-type floors (seconds):
- `HYROX` — 3000 (50:00)
- `MARATHON` — 7200 (2:00:00)
- `SPARTAN` — 1800 (30:00)
- `GENERIC` — 60 (1:00)

### AI Coaching Prompt

The coaching endpoint:
1. Fetches the user's events + splits for the given type (same query as `GetPredictionHandler`)
2. Runs the prediction algorithm to get `predictedSecs` and `confidenceLabel`
3. Builds a structured system + user prompt (see below)
4. Calls `AnthropicClient.Messages.StreamAsync` and pipes `SseEvent.Delta` tokens to the response

**System prompt:**
```
You are a performance coach for endurance and functional fitness events.
Analyse the athlete's split-level trends across their race history.
For each split, note whether it is improving, plateauing, or declining.
Identify the 2-3 biggest opportunities for time savings.
Be specific: name the station/segment, quantify the trend, and give one actionable coaching cue per opportunity.
Keep the total response under 300 words. Use plain text, no markdown headers.
```

**User message** (dynamically constructed):
```
Here are my {eventType} race results, oldest to newest:

{for each event:}
{eventName} — {eventDate} — {formatTime(elapsedSecs)}
{for each split:}
  {splitLabel}: {formatTime(splitSecs)}

Algorithmic prediction for my next race: {formatTime(predictedSecs)} ({confidenceLabel} confidence)

Please analyse my split trends and tell me where my next time savings are.
```

### Frontend

#### New route: `/predict` → `PredictPage`

Registered in `App.tsx` alongside existing authenticated routes, wrapped in `<AuthGuard>`.

**Components:**

| Component | File | Responsibility |
|---|---|---|
| `PredictPage` | `pages/PredictPage.tsx` | Layout, event type state, wires child components |
| `PredictionCard` | `components/PredictionCard.tsx` | Predicted time, confidence badge, avg improvement, current PB |
| `PredictionCoaching` | `components/PredictionCoaching.tsx` | "Generate analysis" button, streams and renders coaching text |
| `PredictionTeaser` | `components/PredictionTeaser.tsx` | Compact widget on Dashboard — predicted time + "Full analysis →" link |

**`usePrediction` hook** (`hooks/usePrediction.ts`): React Query wrapper for `GET /api/events/prediction`. Returns `{ prediction, isLoading, error }`. Keyed by `['prediction', eventType]`.

**Streaming in `PredictionCoaching`:** On button click, calls `fetch` with the JWT `Authorization` header and reads the response body as a `ReadableStream`. `EventSource` is not used — it cannot set custom headers, so it cannot pass the JWT token. The stream is decoded line-by-line; `data:` lines matching `SseEvent.Delta` have their `text` field appended to a `coachingText` state string as tokens arrive. Renders the text verbatim (no markdown parsing — prompt instructs plain text).

**Progress chart on `PredictPage`:** Reuses `ProgressChart` with an extra `projectedPoint` prop. When provided, the chart renders the historical dots + solid line as normal, then adds a dashed line from the last real point to the projected point, with an open circle at the projected end.

**Dashboard teaser (`PredictionTeaser`):** Calls `usePrediction` with the user's most-recently-raced event type (derived from `useEvents` — sort by date, take the type of the most recent). Shows predicted time and avg improvement. Hidden when `isLoading` or when the prediction query returns a 409 (not enough data). Links to `/predict`.

#### Nav update
All three authenticated pages (`DashboardPage`, `UploadPage`, `EventDetailPage`) add a `<Link to="/predict">` nav item alongside "Upload".

## Files Changed

| File | Change |
|---|---|
| `src/Pacevite.Api/Features/Events/EventEndpoints.cs` | Register two new prediction routes |
| `src/Pacevite.Api/Features/Events/GetPrediction/GetPredictionQuery.cs` | **New** — query record |
| `src/Pacevite.Api/Features/Events/GetPrediction/GetPredictionHandler.cs` | **New** — linear regression + response |
| `src/Pacevite.Api/Features/Events/GetPrediction/GetPredictionValidator.cs` | **New** — validates eventType is a known enum value |
| `src/Pacevite.Api/Features/Events/PredictionCoaching/PredictionCoachingHandler.cs` | **New** — fetches events, builds prompt, streams Claude response |
| `src/Pacevite.Api/Program.cs` | Register `AnthropicClient` and `AnthropicOptions` in DI |
| `src/Pacevite.Web/src/App.tsx` | Add `/predict` route |
| `src/Pacevite.Web/src/pages/PredictPage.tsx` | **New** |
| `src/Pacevite.Web/src/components/PredictionCard.tsx` | **New** |
| `src/Pacevite.Web/src/components/PredictionCoaching.tsx` | **New** |
| `src/Pacevite.Web/src/components/PredictionTeaser.tsx` | **New** |
| `src/Pacevite.Web/src/hooks/usePrediction.ts` | **New** |
| `src/Pacevite.Web/src/lib/types.ts` | Add `PredictionResponse` and `PredictionDataPoint` types |
| `src/Pacevite.Web/src/components/ProgressChart.tsx` | Add optional `projectedPoint` prop |
| `src/Pacevite.Web/src/pages/DashboardPage.tsx` | Add `<PredictionTeaser />` and Predict nav link |
| `src/Pacevite.Web/src/pages/UploadPage.tsx` | Add Predict nav link |
| `src/Pacevite.Web/src/pages/EventDetailPage.tsx` | Add Predict nav link |
| `src/Pacevite.Web/src/test/handlers.ts` | Add MSW handler for `/api/events/prediction` |

## Testing

### API Unit Tests

- `GetPredictionHandler` — returns correct `predictedSecs`, `confidenceLabel`, `avgImprovementSecs`, and `dataPoints` for 3 known events
- `GetPredictionHandler` — returns `409` for 1 finished event
- `GetPredictionHandler` — returns `409` for 0 finished events
- `GetPredictionHandler` — returns `409` when all events on same date
- `GetPredictionHandler` — clamps prediction to floor and sets `Low` confidence when regression extrapolates below floor
- `GetPredictionHandler` — excludes `DNF`/`DNS` events from regression inputs
- Linear regression helper — known 3-point input produces correct slope, intercept, R²
- Confidence label logic — R² thresholds map to correct labels

### API Integration Tests

- `GET /api/events/prediction?eventType=HYROX` — authenticated, correct response shape
- `GET /api/events/prediction?eventType=HYROX` — returns 409 when user has only 1 finished event
- `GET /api/events/prediction?eventType=HYROX` — returns 401 when unauthenticated
- `GET /api/events/prediction/coaching?eventType=HYROX` — returns `text/event-stream`, streams a `done` event
- `GET /api/events/prediction/coaching?eventType=HYROX` — returns 401 when unauthenticated

### Frontend Unit Tests

- `PredictionCard` — renders formatted predicted time, confidence badge, avg improvement
- `PredictionCard` — renders "Medium" badge style distinct from "High"
- `PredictionTeaser` — renders on Dashboard with correct time and link to `/predict`
- `PredictionTeaser` — hidden when prediction query returns 409
- `PredictionCoaching` — shows "Generate analysis" button initially
- `PredictionCoaching` — renders streaming text as delta events arrive
- `ProgressChart` — renders dashed projected point when `projectedPoint` prop is provided

## Out of Scope

- User-specified target race date (prediction is always "if I raced today")
- Predictions that account for external factors (altitude, weather, course profile)
- Saving or exporting coaching analysis
- Per-split individual predictions
- Chat-style follow-up questions on the coaching analysis
