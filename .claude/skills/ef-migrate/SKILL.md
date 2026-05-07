---
name: ef-migrate
description: Add an EF Core migration, apply it, and regenerate the AOT compiled model. Takes a migration name as argument.
---

Run these three commands in order. Stop and report if any step fails.

1. Add the migration:
```
dotnet ef migrations add {NAME} --project src/Pacevite.Api
```

2. Apply it to the local database:
```
dotnet ef database update --project src/Pacevite.Api
```

3. Regenerate the AOT compiled model (required after any schema change):
```
dotnet ef dbcontext optimize \
  --project src/Pacevite.Api \
  --output-dir Infrastructure/Persistence/Compiled \
  --namespace Pacevite.Api.Infrastructure.Persistence.Compiled
```

After all three succeed, confirm what changed: the new migration file name, whether any tables were added/altered, and that the compiled model was regenerated.
