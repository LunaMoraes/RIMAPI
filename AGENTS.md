# AGENTS.md — RIMAPI Custom

This repository is a custom/modded version of [IlyaChichkov/RIMAPI](https://github.com/IlyaChichkov/RIMAPI), a RimWorld mod that embeds a REST API + SSE server (`http://localhost:8765/`) directly inside the game.

## Agent Rules & Git Workflow

- **NEVER run `git commit` or `git push` without explicit user permission.** The user manages commits and pushes through GitHub Desktop. You may suggest commit titles and descriptions.
- Keep `AGENTS.md` up to date as architectural decisions, build steps, and custom features evolve.
- See [CLAUDE.md](CLAUDE.md) for the upstream architecture overview, coding conventions, API conventions, and domain map.

## Local Environment

- **RimWorld installation:** `C:\Program Files (x86)\Steam\steamapps\common\RimWorld` (currently `1.7.5005 rev1082`)
- **RimWorld Mods folder:** `C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\RIMAPI` (directory junction → `c:\projetos\rim-api-custom`, so builds are live immediately)
- **.NET SDK:** .NET 10 (`10.0.203` installed)

## Custom Project Direction & Conventions

- **Target Version:** RimWorld **1.7 only** (`Release-1.7`, `net10.0`, `RIMWORLD_1_7`). We do not need to maintain 1.5/1.6 backwards compatibility for new custom features.
- **Primary Goals:**
  1. Make RimWorld playable by autonomous AI colony-management agents (compact state snapshots, rich gameplay actions, clear action feedback).
  2. Support a local human-facing web dashboard to monitor what the AI agent is seeing/doing (localhost-only, permissive CORS).
  3. Provide human-only experiment/testing routes under `/api/v1/experiment/...` (route-prefix separation; AI agents are instructed not to use these).
- **Code Organization:** Fix/extend core `Controllers/` and `Services/` directly where needed, and group new AI, dashboard, and experiment endpoints under dedicated controllers/folders.

## Tech Stack & Build

- **Solution / Project:** `Source/RIMAPI/RimApi.sln` / `Source/RIMAPI/RimApi.csproj`
- **Target Frameworks & Configs:**
  - `Release-1.5` (`net472`, `RIMWORLD_1_5`) → `1.5/Assemblies/`
  - `Release-1.6` (`net472`, `RIMWORLD_1_6`) → `1.6/Assemblies/`
  - `Release-1.7` (`net10.0`, `RIMWORLD_1_7`, uses `Watson` 7.3.2 + `Lib.Harmony.Ref` 3.0.0-rc.4) → `1.7/Assemblies/`
  - `Debug` (`net472`) → `Source/RIMAPI/bin/Debug/`
- **Build Outputs:** `1.5/`, `1.6/`, and `1.7/` are gitignored in `.gitignore`.

Example build command for RimWorld 1.7:
```powershell
dotnet build Source/RIMAPI/RimApi.csproj -c Release-1.7
```

## Architecture Summary

- **Request Pipeline:** HTTP transport (`HttpListenerTransport` on 1.5/1.6, `WatsonTransport` on 1.7) → thread-safe Request Queue → `ApiServer.ProcessTick()` (main Unity thread, ≤10 req/tick) → `Router` → `Controller` (thin, no `Verse.*` calls) → `Service` (RimWorld API access) → JSON `ApiResult<T>` (`snake_case`).
- **DI:** Custom `ServiceCollection` / `ServiceProvider` registered in `ApiServer.CreateDefaultServiceProvider()`.
- **Real-time Events:** `SseService` + Harmony patches in `Source/RIMAPI/RimworldRestApi/Hooks/`.
- **Extensions:** Implement `IRimApiExtension` for modular route/service registration.
