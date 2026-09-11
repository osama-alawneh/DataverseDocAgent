# Local Codex pipeline

.NET collects and saves authoritative Dataverse evidence, schedules bounded analysis stages, validates their results, and builds the DOCX. Codex supplies labelled interpretations and citations; it does not select the Dataverse queries or supply inventory counts.

## Setup

Install the .NET 8 SDK and Codex CLI. Run the following as the same operating-system account that will run the console or API:

```powershell
codex login
codex login status
dotnet restore DataverseDocAgent.sln
```

Use ChatGPT login for Codex. The runner rejects API-key authentication; it does not fall back to direct model APIs. For an API hosted as a service, ensure that account has a usable home directory and Codex login. Set `CODEX_HOME` only if the account cannot use its normal Codex home; use an absolute directory containing that account's Codex configuration/authentication and restrict its filesystem access. Never put login material in this repository.

See the official [non-interactive mode guide](https://learn.chatgpt.com/docs/non-interactive-mode) and [skills guide](https://learn.chatgpt.com/docs/build-skills).

## Analyze the sanitized fixture

Run from the repository root:

```powershell
dotnet run --project src/DataverseDocAgent.Console -- analyze --snapshot fixtures/sanitized-snapshot --run .dataverse-runs/fixture
```

Optional arguments select a skills root, executable, and model:

```powershell
dotnet run --project src/DataverseDocAgent.Console -- analyze --snapshot fixtures/sanitized-snapshot --run .dataverse-runs/fixture --skills .agents/skills --codex codex --model gpt-5.5
```

The model argument is optional. Omit it to use the CLI's configured/default model. A model must be available to the signed-in account. `--snapshot` points to a directory containing `manifest.json`, coverage, and component files, not a standalone model response.

The report is written to `<run>/report.docx`. A completed report with unavailable analysis or failed/partial collection returns exit code 2 and preserves the report for review. Expected extraction exclusions are always disclosed but do not alone change a successful exit code. A fatal input or setup failure returns 1; analysis-stage login/configuration/usage failures can instead produce a partial report with exit 2; invalid arguments return 2; Ctrl+C returns 130.

## Collect from Dataverse

Provide all four environment variables through your normal secret-management mechanism before running the console:

- `DATAVERSE_ENVIRONMENT_URL`
- `DATAVERSE_TENANT_ID`
- `DATAVERSE_CLIENT_ID`
- `DATAVERSE_CLIENT_SECRET`

Credentials are accepted only through the environment for these console commands, never as command-line arguments. Do not paste secrets into examples, reports, or tracked files.

```powershell
# Collect evidence without a model call.
dotnet run --project src/DataverseDocAgent.Console -- collect --run .dataverse-runs/my-environment

# Analyze that saved evidence; the original snapshot is kept intact.
dotnet run --project src/DataverseDocAgent.Console -- analyze --snapshot .dataverse-runs/my-environment/evidence --run .dataverse-runs/my-environment

# Or collect, analyze and render in one invocation, using a new run directory.
dotnet run --project src/DataverseDocAgent.Console -- generate --run .dataverse-runs/my-environment-new
```

Collection reuses the authenticated client for the permission preflight. Missing required privileges block collection; extra privileges produce the existing security-check recommendation and do not block generation. The gate retains the existing 13 privileges for compatibility: Read Entity, Attribute, Relationship, PluginAssembly, PluginType, SdkMessageProcessingStep, WebResource, Workflow, Role, SystemForm, Query, Organization and SystemUser. Several exceed the implemented collection scope. Use the supplied version 1.0.0.7 reader role: it adds `prvReadSystemUser`, which is missing from version 1.0.0.6. Importing/assigning the provided reader role remains a manual Dataverse administration step; this pipeline does not modify roles or broaden access automatically. See [the role artifact](../artefacts/README.md).

## API

The existing `POST /api/document/generate` asynchronous job/download flow now invokes the same local pipeline. Run with:

```powershell
dotnet run --project src/DataverseDocAgent.Api
```

Configure `Codex:Executable`, optional `Codex:Model`, and `Codex:TimeoutSeconds` through normal .NET configuration. Configure `Pipeline:SkillsRoot` and `Pipeline:RunRoot` with absolute paths for deployed hosts; repository execution defaults to `.agents/skills` and `.dataverse-runs` under the repository root. Optional scheduling limits are `Pipeline:BatchSize`, `Pipeline:MaxInputCharacters`, and `Pipeline:MaxAttempts`. `Generation:PerTaskTimeoutMinutes` bounds the overall job.

The API still accepts its credential request body over the existing HTTPS endpoint. Credential references are detached from the queued task after connection completes. The SDK connection is disposed before analysis. Immutable managed strings and authentication internals cannot be promised to be zeroed from memory.

A partial report remains downloadable and contains explicit coverage. CLI exit code 2 is not an API job state. Authentication/configuration/usage failures have structured failure codes; raw SDK or child-process output is not included in user-facing job diagnostics.

## Skills and resumable stages

The three repository skills live under `.agents/skills`: `dataverse-table-analysis`, `dataverse-relationship-analysis`, and `dataverse-environment-synthesis`. That is Codex's repository skill location. This scheduler explicitly reads the selected versioned `SKILL.md` and output schema, embeds its instructions with the selected evidence, and validates the returned result. It does not depend on automatic ambient skill discovery or ambient tool execution. The child process runs with restrictive configuration and tools disabled by the runner.

The console holds a workflow lock across analysis and final DOCX publication, and writes the DOCX through an atomic rename. Concurrent console commands targeting the same run directory fail rather than interleave artifacts. The scheduler batches by component count and input size. Every result must cover exactly its assigned component IDs, use known evidence references, match its stage/input/skill contract, and pass its JSON schema. Invalid output receives at most the configured bounded retry. Login and usage failures are not repeatedly retried.

Rerun `analyze` with the same snapshot and run directory to resume. Valid checkpoints are reused; changed inputs/skills, malformed output, and hash mismatches invalidate reuse. Evidence directories are write-once: use a new run directory for a fresh `collect` or `generate`. SHA-256 hashes detect corruption and drift; they are not digital signatures or protection against someone who can modify both a file and its manifest.

## Coverage and interpretation limits

Collection traverses the returned custom tables. Customizations confined to standard tables are excluded. A relationship touching a custom table may refer to a standard table outside the inventory. Relationship records preserve direction/lookup metadata and are counted once by schema name, even if observed from both endpoints.

Organisation metadata, table/field descriptions, relationship metadata, and application-user role signals come from the SDK evidence. Solution membership is not queried. Application users are limited to the extractor's enabled, unlicensed users with an application ID; the SDK result is not paged, so large inventories may be truncated. Failed role lookup remains explicit. Synchronous SDK calls cannot be interrupted mid-call; cancellation is observed around them. Sequential reads do not form a transactional environment snapshot, and a valid empty SDK result does not independently prove server completeness.

Plugins, cloud flows, JavaScript/web resources, forms, views, business rules and automation dependencies are not implemented by this pipeline. A broad permission gate does not imply that these components were scanned.

DOCX counts and complexity use only collected evidence. Failed or missing collection is not reported as proof that fields, relationships, or users do not exist. The report includes coverage, missing-analysis notices, and an authoritative evidence appendix. AI statements are labelled by their declared fact/inference basis and cite evidence IDs; they remain interpretations that should be checked against the cited metadata. Naming prefixes, including `cr*` and `msdyn`, do not establish publisher ownership or prove the presence/absence of third-party ISV components.

## Data handling

`collect` keeps evidence local. `analyze` and `generate` send selected metadata batches to hosted models through the authenticated Codex subscription. Local orchestration does not mean local model inference. Names, descriptions, URLs, application-user IDs/emails, roles, and metadata relationships may be sensitive; only analyze data you are authorized to send under your account's applicable controls.

Snapshots, checkpoints and reports are stored in the chosen run directory. `.dataverse-runs` is ignored by Git, but another chosen directory is not automatically ignored or encrypted. Restrict filesystem access and set an appropriate retention policy. The final DOCX contains raw metadata in its evidence appendix. Application logs use fixed diagnostics and structural status information rather than raw SDK errors or model output. Credentials are not included in evidence or prompts, and the runner filters credential-bearing environment variables; these measures do not replace host access controls or provider retention settings.

## Verification status for this change

The solution build passed with 0 warnings and 0 errors, the full regression suite passed 321/321 tests, and the subsequent source-schema follow-up passed 20/20 scheduler tests.

On 2026-09-11, the user completed a real Codex fixture run in PowerShell using the existing subscription login and an explicit CLI executable path. The saved `.dataverse-runs/fixture-smoke` results were then independently checked:

- All three skill stages succeeded: table analysis covers 5 targets, relationship analysis covers 1, and environment synthesis covers all 8 fixture components. All required analysis coverage entries are complete.
- Checkpoint output hashes, input bindings, target coverage and evidence references match. The current DOCX passes OpenXML validation with zero errors and contains the expected 2 custom tables, 3 fields, 1 relationship and application-user evidence, with scope limitations retained.
- A guarded replay copied the real saved stage artifacts into `.dataverse-runs/final-verification/checkpoint-replay` and used the production snapshot loader and scheduler with a runner that throws on any invocation. All three stages were reused with **zero runner calls**, revalidating the current skill/schema/input/output bindings.

The exact console repeat in the implementation sandbox remained blocked before analysis by Windows access denial on the original run's `.workflow.lock` (HRESULT 0x80070005). All nine original stage files retained their hashes and modification times; the successful report and analysis JSON were unchanged. The report was also open in another process during inspection, so a byte copy was used for OpenXML validation. This is a host-access limitation, not a failed model stage or checkpoint-reuse failure. Normal-user ACL repair is not established by these sandbox checks; the latest run files remain Administrators-owned without a named oaala Modify rule.

To repeat the complete console workflow, close the report in Word and run from PowerShell with write access to the run directory. On this machine the verified CLI path is:

```powershell
dotnet run --project src/DataverseDocAgent.Console -- analyze --snapshot fixtures/sanitized-snapshot --run .dataverse-runs/fixture-smoke --codex 'C:\Users\oaala\AppData\Local\OpenAI\Codex\bin\7ac07f4ce733f89a\codex.exe'
```

Use the installed executable path appropriate to your machine if Codex is not on PATH. No live Dataverse calls or role imports were used for this fixture verification.
