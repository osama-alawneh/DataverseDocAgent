# Story 1.1: Project Scaffold and Solution Structure

Status: done

## Story

As a developer,
I want a clean .NET 8 solution with the confirmed feature-folder project structure,
so that all subsequent stories have a consistent, navigable home from day one.

## Acceptance Criteria

1. A .NET 8 solution named `DataverseDocAgent` exists with two projects: `DataverseDocAgent.Console` (Phase 1 POC) and `DataverseDocAgent.Api` (stub for Phase 2).
2. The `DataverseDocAgent.Api` project follows the feature-folder layout from Architecture Section 3: `Features/`, `Agent/`, `Agent/Tools/`, `Dataverse/`, `Documents/`, `Storage/`, `Jobs/`, `Middleware/`, `Common/`.
3. All confirmed NuGet packages are referenced in the correct project(s): `Anthropic.SDK`, `Microsoft.PowerPlatform.Dataverse.Client`, `dnlib`, `DocumentFormat.OpenXml`, `Serilog.AspNetCore`, `Microsoft.Extensions.Caching.Memory` (in Api); `Anthropic.SDK`, `Microsoft.PowerPlatform.Dataverse.Client` (in Console).
4. `appsettings.json` and `appsettings.Development.json` exist in `DataverseDocAgent.Api` with placeholder sections for `Anthropic:ApiKey` and `Dataverse:TestCredentials`. Values are populated via .NET User Secrets — never committed.
5. `.gitignore` excludes `appsettings.Development.json`, `*.user`, `secrets.json`, `bin/`, `obj/`, and `.vs/`.
6. `dotnet build` produces zero errors and zero warnings across the full solution.
7. Each project file and folder stub includes a comment annotation `// F-xxx` corresponding to its Feature Registry ID where applicable (NFR-015).

## Tasks / Subtasks

- [x] Create solution and projects (AC: 1)
  - [x] `dotnet new sln -n DataverseDocAgent`
  - [x] `dotnet new console -n DataverseDocAgent.Console -o src/DataverseDocAgent.Console`
  - [x] `dotnet new webapi -n DataverseDocAgent.Api -o src/DataverseDocAgent.Api --no-openapi`
  - [x] Add both projects to solution
- [x] Add NuGet packages (AC: 3)
  - [x] Add `Anthropic.SDK` to both projects
  - [x] Add `Microsoft.PowerPlatform.Dataverse.Client` to both projects
  - [x] Add `dnlib`, `DocumentFormat.OpenXml`, `Serilog.AspNetCore`, `Microsoft.Extensions.Caching.Memory` to Api project only
- [x] Create feature-folder directory structure in Api project (AC: 2)
  - [x] `Features/SecurityCheck/`, `Features/DocumentGenerate/`, `Features/ImpactAnalyse/`, `Features/HealthAudit/`, `Features/Download/`
  - [x] `Agent/`, `Agent/Tools/`
  - [x] `Dataverse/`, `Documents/`, `Storage/`, `Jobs/`, `Middleware/`, `Common/`
  - [x] Add a `.gitkeep` or stub file in each empty folder
- [x] Configure User Secrets and appsettings (AC: 4)
  - [x] `dotnet user-secrets init` in both projects
  - [x] Add placeholder `appsettings.json` with empty `Anthropic` and `Dataverse` sections
  - [x] Add `appsettings.Development.json` with commented examples (never real values)
- [x] Configure .gitignore (AC: 5)
  - [x] Add standard .NET .gitignore entries
  - [x] Explicitly add `appsettings.Development.json` and `secrets.json`
- [x] Verify clean build (AC: 6)
  - [x] Run `dotnet build` from solution root — confirm zero warnings/errors

## Dev Notes

- The `DataverseDocAgent.Api` project stub should have minimal content at this stage — `Program.cs` with `var app = WebApplication.CreateBuilder(args).Build(); app.Run();` is sufficient. The full API host is stood up in Story 2.1.
- The `DataverseDocAgent.Console` project is the active Phase 1 project. The Api project is scaffolded but not yet used.
- Use the minimal Web API template (`--no-openapi`) to avoid Swagger dependencies that are not needed for an API-only product.
- .NET User Secrets store is per-project. Both projects need separate `dotnet user-secrets init`.
- Do NOT add `Azure.Storage.Blobs` yet — that is a Phase 2 dependency introduced in Story 3.2.

### Project Structure Notes

```
DataverseDocAgent/
├── DataverseDocAgent.sln
├── .gitignore
├── src/
│   ├── DataverseDocAgent.Console/
│   │   ├── DataverseDocAgent.Console.csproj
│   │   └── Program.cs
│   └── DataverseDocAgent.Api/
│       ├── DataverseDocAgent.Api.csproj
│       ├── Program.cs
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       ├── Features/
│       ├── Agent/Tools/
│       ├── Dataverse/
│       ├── Documents/
│       ├── Storage/
│       ├── Jobs/
│       ├── Middleware/
│       └── Common/
├── docs/
└── artefacts/
```

### References

- [Source: _bmad-output/planning-artifacts/architecture.md#3-project-structure-feature-folder]
- [Source: docs/prd.md#8-technical-architecture] — NuGet package list
- [Source: docs/prd.md#7-non-functional-requirements] — NFR-015 (FR traceability annotations)

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

_None._

### Completion Notes List

- Created `DataverseDocAgent.sln` with two projects: `DataverseDocAgent.Console` (console, Phase 1) and `DataverseDocAgent.Api` (webapi stub, Phase 2).
- `Program.cs` in Api replaced with minimal stub per Dev Notes (`WebApplication.CreateBuilder(args).Build(); app.Run()`).
- All 6 NuGet packages added to Api; `Anthropic.SDK` and `Microsoft.PowerPlatform.Dataverse.Client` added to Console.
- Feature-folder directories created with `.gitkeep` files annotated `// F-xxx` per NFR-015.
- User Secrets initialized on both projects. `appsettings.json` has placeholder `Anthropic` and `Dataverse` sections. `appsettings.Development.json` has commented examples — no real credentials.
- `.gitignore` generated via `dotnet new gitignore` with explicit additions for `appsettings.Development.json` and `secrets.json`.
- `dotnet build` → 0 errors, 0 warnings (AC: 6 ✅).
- No automated tests applicable — scaffolding story with no business logic; build success is the validation gate.

### File List

- `DataverseDocAgent.sln`
- `.gitignore`
- `src/DataverseDocAgent.Console/DataverseDocAgent.Console.csproj`
- `src/DataverseDocAgent.Console/Program.cs`
- `src/DataverseDocAgent.Api/DataverseDocAgent.Api.csproj`
- `src/DataverseDocAgent.Api/Program.cs`
- `src/DataverseDocAgent.Api/appsettings.json`
- `src/DataverseDocAgent.Api/appsettings.Development.json`
- `src/DataverseDocAgent.Api/Features/SecurityCheck/.gitkeep`
- `src/DataverseDocAgent.Api/Features/DocumentGenerate/.gitkeep`
- `src/DataverseDocAgent.Api/Features/ImpactAnalyse/.gitkeep`
- `src/DataverseDocAgent.Api/Features/HealthAudit/.gitkeep`
- `src/DataverseDocAgent.Api/Features/Download/.gitkeep`
- `src/DataverseDocAgent.Api/Agent/.gitkeep`
- `src/DataverseDocAgent.Api/Agent/Tools/.gitkeep`
- `src/DataverseDocAgent.Api/Dataverse/.gitkeep`
- `src/DataverseDocAgent.Api/Documents/.gitkeep`
- `src/DataverseDocAgent.Api/Storage/.gitkeep`
- `src/DataverseDocAgent.Api/Jobs/.gitkeep`
- `src/DataverseDocAgent.Api/Middleware/.gitkeep`
- `src/DataverseDocAgent.Api/Common/.gitkeep`
- `src/DataverseDocAgent.Api/Properties/launchSettings.json`
- `global.json`

### Change Log

- 2026-04-13: Story 1.1 implemented — .NET 8 solution scaffold, feature-folder structure, NuGet packages, User Secrets, .gitignore. Build: 0 errors, 0 warnings.
- 2026-04-13: Code review findings appended (8 patches, 10 deferred, 9 dismissed).
- 2026-04-13: Second review pass (3-layer adversarial) — all 8 patches confirmed, 1 new defer added (AllowedHosts), 14 dismissed.
- 2026-04-13: All 8 review patches applied — F-IDs corrected, package versions pinned to 8.x, launchSettings cleaned, .http deleted, global.json added, artefacts/ gitignored, csproj annotated. Build: 0 errors, 0 warnings. Story marked done.

### Review Findings

- [x] [Review][Patch] AC 7 — Replace alpha Feature IDs in 13 .gitkeep files with PRD §4 numeric Feature Registry IDs [src/DataverseDocAgent.Api/Features/**/.gitkeep] — current `F-SEC`/`F-DOC`/etc. don't exist in registry; remove annotations from infrastructure-only folders per "where applicable"
- [x] [Review][Patch] AC 7 — Add `<!-- F-xxx -->` annotation to both .csproj files [src/DataverseDocAgent.Console/DataverseDocAgent.Console.csproj, src/DataverseDocAgent.Api/DataverseDocAgent.Api.csproj]
- [x] [Review][Patch] Pin `Microsoft.Extensions.Caching.Memory` and `Serilog.AspNetCore` to 8.0.x to match net8.0 TFM [src/DataverseDocAgent.Api/DataverseDocAgent.Api.csproj:13,15] — currently on 10.x; future M.E.* package additions in Story 2.1/3.1 will trigger NU1605 downgrade warnings and break AC 6
- [x] [Review][Patch] Clean up `launchSettings.json` weatherforecast launchUrl (404 trap for first F5) [src/DataverseDocAgent.Api/Properties/launchSettings.json]
- [x] [Review][Patch] Delete leftover `DataverseDocAgent.Api.http` template file [src/DataverseDocAgent.Api/DataverseDocAgent.Api.http] — references nonexistent `weatherforecast` endpoint
- [x] [Review][Patch] Add `global.json` pinning SDK to 8.0.x at repo root for AC 6 reproducibility across machines/CI
- [x] [Review][Patch] Add `artefacts/` to `.gitignore` (UK spelling per PRD project structure)
- [x] [Review][Patch] Update File List to include `src/DataverseDocAgent.Api/Properties/launchSettings.json` (template-generated, not previously listed)
- [x] [Review][Defer] appsettings.Development.json committed-and-ignored creates operational confusion [src/DataverseDocAgent.Api/appsettings.Development.json + .gitignore] — both AC 4 and AC 5 are literally met; document onboarding pattern in a future story
- [x] [Review][Defer] Empty Anthropic/Dataverse keys in committed `appsettings.json` invite paste-secret-here mistakes — add onboarding doc
- [x] [Review][Defer] Console project has no `Microsoft.Extensions.Configuration.*` packages — `UserSecretsId` set but no loader; add in Story 1.2
- [x] [Review][Defer] Console project has no `appsettings.json` — add in Story 1.2 when first config binding is needed
- [x] [Review][Defer] No `Directory.Build.props` / Central Package Management — add when third project arrives
- [x] [Review][Defer] No `tests/` solution folder slot — add when first test project arrives
- [x] [Review][Defer] `UserSecretsId` only auto-loads in `Development` environment — document for non-Dev environments
- [x] [Review][Defer] `Nullable enable` + `dnlib` (no NRT annotations) — known paper-cut for Story 2.x plugin scan code
- [x] [Review][Defer] Project namespace `DataverseDocAgent.Console` collides with `System.Console` — known footgun for any future code in that project
- [x] [Review][Defer] `appsettings.Development.json` "commented examples" task wording — JSON has no native comment syntax; revisit AC wording in retrospective
- [x] [Review][Defer] `AllowedHosts: "*"` in committed `appsettings.json` — acceptable for Phase 1 stub (not deployed); address in Story 2.1 when full API host and environment-specific config are configured
