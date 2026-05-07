---
name: container-rebuild
description: Force a clean no-cache rebuild of a named compose service and restart it with the proxy. Args: service name (web|api).
---

Given a service name (web or api), run from the project root /home/madulog/Projects/Pacevite:

1. `podman compose build --no-cache {service}`
2. `podman compose up -d --force-recreate {service} proxy`
3. Print final status with `podman compose ps`

Always use `--no-cache`. Running `podman compose up --build` alone reuses layer cache
and will silently deploy stale compiled output — this is the correct workaround.

If no service name is given, default to `web`.
