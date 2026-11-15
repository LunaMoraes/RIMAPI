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

- **Request Pipeline:** HTTP transport (`HttpListenerTransport` on 1.5/1.6, `WatsonTransport` on 1.7) → thread-safe Request Queue → `ApiServer.ProcessQueuedRequests()` (main Unity thread, ≤10 req/tick) → `Router` (localhost `Host`/CORS/Content-Type checks + validation exception mapping) → `Controller` (thin, no `Verse.*` calls) → `Service` (RimWorld API access) → JSON `ApiResult<T>` (`snake_case`).
- **DI:** Custom `ServiceCollection` / `ServiceProvider` (`TryGetService<T>` safe lookup) with `RegisterDiscoveredComponents()` auto-discovery + explicit registration in `ApiServer.CreateDefaultServiceProvider()`.
- **Real-time Events:** `SseService` (`snake_case` serialization) + Harmony patches in `Source/RIMAPI/RimworldRestApi/Hooks/`.
- **Extensions:** Implement `IRimApiExtension` for modular route/service registration.

## Custom Endpoints & Tooling

- **AI Agent Routes (`/api/v1/ai/...` + expanded `OrderController`):**
  - `GET /api/v1/ai/snapshot` — Fair-play fog-of-war colony state snapshot by default (`?omniscient=true`, `?include=game_conditions,designations,prisoners,guests,animals,mechs`, `?since_tick=`) (`AiController`)
  - `GET /api/v1/ai/designations` — List active map designations respecting fog of war (`AiController`)
  - `POST /api/v1/order/designate/area` — Expanded designations (`mine`, `deconstruct`, `harvest`, `chop`, `cut`, `haul`, `hunt`, `tame`, `slaughter`, `smooth`, `claim`, `uninstall`, `forbid`, `unforbid`, `cancel`, `remove-all`) respecting fog of war by default (`omniscient`) with `affected_count` & `skipped_fogged_count`
  - `POST /api/v1/ai/pawn/draft`, `POST /api/v1/ai/pawn/command` (`attack` fog check), `POST /api/v1/ai/pawn/policy` (supports area/policy IDs + reading policy), `GET /api/v1/ai/policies`, `GET /api/v1/ai/areas`, `POST /api/v1/ai/areas/create`, `POST /api/v1/ai/areas/cells`, `POST /api/v1/ai/prisoner/interaction` (`AiPawnController`)
  - `GET /api/v1/ai/letters`, `POST /api/v1/ai/letters/dismiss` (`ChoiceLetter_GrowthMoment` passion/trait selection), `GET /api/v1/ai/zones/stockpile` (`Zone_Stockpile` + `IStoreSettingsParent` storage buildings), `POST /api/v1/ai/zones/stockpile/filter`, `GET /api/v1/ai/trade/traders`, `GET /api/v1/ai/trade/deal`, `POST /api/v1/ai/trade/execute` (`dry_run`, quote `index`, `net_silver_change`, `TradeSession.Active` conflict guard) (`AiColonyExtrasController`)
  - `GET /api/v1/ai/designators`, `POST /api/v1/ai/designate`, `GET /api/v1/ai/pawn/options`, `POST /api/v1/ai/pawn/options/invoke`, `GET /api/v1/ai/gizmos`, `POST /api/v1/ai/gizmos/invoke`, `POST /api/v1/ai/mech/work-mode` (Biotech mechs + soft-reflection *Misc. Robots* `AIRobot`), `GET /api/v1/ai/pawn/abilities` (vanilla + `VEF.Abilities` / *Vanilla Psycasts Expanded*), `POST /api/v1/ai/pawn/ability` (vanilla + `VEF.Abilities`), `POST /api/v1/ai/hospitality/guest`, `GET /api/v1/ai/hospitality/beds`, `POST /api/v1/ai/hospitality/bed` (soft-reflection *Hospitality* bridge), `POST /api/v1/ai/time/advance` (`AiCommandSurfaceController`; wraps FloatMenu/Gizmos/thing Designator/VEF casting with `Find.Selector` context binding)
- **Dashboard Routes (`/api/v1/dashboard/...` + `ai_action` SSE):**
  - `GET /api/v1/dashboard/overview` (supports Main Menu when `snapshot == null`), `GET /api/v1/dashboard/actions` (write-focused by default, `?include_reads=true`, capability `tier` + `X-RimApi-Actor`), `POST /api/v1/dashboard/actions/clear` (`DashboardController`)
  - Local web UI: `dashboard/index.html` (see `dashboard/README.md`)
- **Human-Only Experiment Routes (`/api/v1/experiment/...`):**
  - `GET /api/v1/experiment/status`, `POST /api/v1/experiment/incident`, `POST /api/v1/experiment/speed`, `POST /api/v1/experiment/annotation` (`ExperimentController`)
- **Checks & Docs:**
  - Validate YAML docs: `python scripts/docs/mkdocs_check_api.py` (controllers live under `docs/_api_macroses/controllers/`)
  - Smoke Test: `python tests/ai_dashboard_smoke_test.py` (add `--write-tests` for mutating checks) against a running RimWorld 1.7 colony.
