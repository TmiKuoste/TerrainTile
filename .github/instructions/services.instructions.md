---
applyTo: "BuilderServices/**"
description: Rules for the Dockerised Azure Service Bus worker.
---

# BuilderServices (worker)

- **Targets `net9.0`** with implicit usings and nullable enabled — modern C# is fine here
  (unlike the `TerrainEngine` core).
- **Configuration comes from environment variables**, never hardcoded or committed:
  - `AZURE_SERVICE_BUS_CONNECTION_STRING`
  - `AZURE_SERVICE_BUS_SB_QUEUE_NAME`
- **Run locally:** `dotnet run --project BuilderServices/BuilderServices.csproj` with the env
  vars set.
- **Docker:** `docker build -t terraintile-builder -f BuilderServices/Dockerfile BuilderServices`.
  The image targets Linux and runs `dotnet BuilderServices.dll`.
- **Keep secrets out of the repo.** Connection strings stay in the environment / Azure config.
