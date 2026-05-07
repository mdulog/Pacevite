# 0003 — Store Location and Metadata as JSONB Columns

**Status:** Accepted

## Context and Problem Statement

Different event types (Marathon, Hyrox, Spartan, Generic) have different supplementary fields. Marathon events have a city/country; Hyrox events have station times; Spartan events have obstacle counts. The schema must accommodate this variation without a migration per event type.

## Considered Options

1. **Separate tables per event type** — `MarathonEvent`, `HyroxEvent`, etc. each with typed columns
2. **Single wide table with nullable columns** — all possible fields on `Event`, null when not applicable
3. **JSONB columns on `Event`** — `Location` and `Metadata` stored as PostgreSQL JSONB

## Decision Outcome

Chose **JSONB columns** (`Location jsonb`, `Metadata jsonb`) on the `Event` entity. `EventSplit` also has a `Metadata jsonb` column for split-level supplementary data.

A GIN index is defined on `Event.Metadata` to support future station/obstacle querying.

**Why:**
- New event types or new supplementary fields require no schema migration — just add keys to the JSON at parse time.
- PostgreSQL JSONB is indexed (GIN), queryable via `->` / `->>` operators, and stored as binary (not a text blob).
- Avoids the null-sprawl of a wide table as event types grow.

## Consequences

- **Easier:** Adding new event types and new supplementary fields without migrations.
- **Harder:** Type safety — `Location` and `Metadata` are `Dictionary<string, object>` in C#. Callers must know the expected keys per event type; there is no compile-time schema enforcement for JSONB content.
- EF Core serialises/deserialises via a `ValueConverter<Dictionary<string, object>, string>` — the `jsonb` column type is set explicitly so PostgreSQL stores binary, not text.
- No application-level FK constraint from `Event.UserId` to `AspNetUsers.Id` — see ADR 0006.
