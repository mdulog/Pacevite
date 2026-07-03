# External Sync Scope — Apple Health & Garmin Connect

**Date:** 2026-07-03
**Status:** Decided (both platforms out of scope for this effort)

---

## Overview

The event-acquisition seed considered three ways to get more race data into Pacevite's existing prediction feature: manual entry, more file formats, and automated sync from external platforms. For sync, three platforms were evaluated — Strava, Garmin Connect, and Apple Health. Strava was built; the other two were parked for different reasons. This spec records those reasons so a future effort doesn't have to re-derive them.

---

## Decision

| Platform | Status | Reason |
|---|---|---|
| Strava | Shipped | OAuth2 with a well-documented public API; most common platform among Pacevite's target users (runners/triathletes). Implemented in `Features/Sync/*`. |
| Garmin Connect | Parked | No public self-serve developer API exists for reading a user's activity history from a backend/web app. "Garmin Connect IQ" — the product with public docs — is an on-device watch-app SDK, not a backend data API. Real activity-history access requires Garmin's Health/Connect partner program: an application plus manual Garmin approval, with no docs available to reference even hypothetically until accepted. Implementation was attempted (GPX → Strava → Garmin, in that order) and stopped as soon as this constraint surfaced. |
| Apple Health | Parked | iOS-only, no OAuth — HealthKit requires a native iOS companion app to read and forward data, which is incompatible with Pacevite's web-only architecture. Marked out of scope during the original seed interview, before any implementation was attempted. |

---

## Revisit Conditions

- **Garmin Connect** — revisit once Garmin partner API access is obtained (an application + approval outside engineering's control). At that point, treat it as a new feature: research the actual granted API surface (not Connect IQ) before writing any code, and apply the same TDD + real-API-verification discipline used for Strava.
- **Apple Health** — revisit only if Pacevite grows a native iOS companion app. HealthKit is not reachable from a web backend under any configuration.

---

## Current Sync Surface (as shipped)

- Manual entry — `Features/Events/CreateEvent/`
- File upload (CSV, JSON, GPX) — `Infrastructure/Parsing/*EventParser.cs`
- Strava OAuth sync — `Features/Sync/*`

All three feed the existing prediction feature. No new insight/prediction logic was introduced by this effort.
