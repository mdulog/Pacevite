---
name: seed-db
description: Seeds the local dev database with /home/madulog/Projects/Pacevite/sample-events.json via the running API. The API must be running on port 5291.
---

## Steps

1. **Register or log in**

POST `http://localhost:5291/api/auth/register`:
```json
{ "email": "seed@pacevite.dev", "password": "Seed1234!" }
```
If the response is 409 (duplicate), POST to `/api/auth/login` with the same credentials instead.
Extract the `token` from the response body.

2. **Upload sample data**

POST `http://localhost:5291/api/events/upload` with:
- `Authorization: Bearer {token}`
- Multipart form field `file` containing the contents of `/home/madulog/Projects/Pacevite/sample-events.json` with `Content-Type: application/json`

3. **Report**

Print how many events were created (the response is an array — report its length).
If any step fails, print the status code and response body and stop.

## Notes
- If the API is not responding on port 5291, tell the user to start it first with `dotnet run --project src/Pacevite.Api --launch-profile http`
- Duplicate uploads are silently skipped by the API (idempotent by event_type + event_name + event_date)
