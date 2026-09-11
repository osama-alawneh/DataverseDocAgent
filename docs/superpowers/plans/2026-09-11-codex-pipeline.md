# Local Codex analysis pipeline implementation plan

> Execute the accepted user architecture in this clone with focused implementation workers and review. No changes to the original repository, push, deployment, or live Dataverse smoke test.

**Goal:** .NET collects authoritative evidence, schedules bounded repository skills through authenticated local Codex, validates resumable checkpoints, and assembles DOCX.

**Architecture:** Versioned snapshots hold immutable evidence and explicit coverage. The scheduler supplies selected components to three skills; interpretations cannot replace inventory. Each stage is bound to input and skill hashes and validated before atomic publication.

**Tech stack:** .NET 8, existing SDK tools/OpenXML, JsonSchema.Net, Codex CLI exec.

## Tasks

- [x] Evidence collection: add `Pipeline/EvidenceSnapshot.cs`, `EvidenceCollector.cs`, tests. Persist SHA256 manifest/file entries, stable component IDs and explicit success/failure/exclusion/unimplemented coverage. Use current five tools; disclose standard-table limitation. Retain relationship direction/lookup metadata and deduplicate by schema ID.
- [x] Process runner: add `Pipeline/AnalysisRunner.cs`, `CodexCliRunner.cs`, tests. Interface takes skill name, prompt, working directory, schema and output paths. Use argument lists/stdin, subscription login, ignored user config, read-only/ephemeral mode, bounded capture, cancellation/timeout and process-tree cleanup. Distinguish configuration/login/usage/output failures.
- [x] Scheduling: add `Pipeline/AnalysisPipeline.cs`, checkpoint contracts and tests. Partition input by count/bytes. Validate exact component coverage, references, schema, hashes and versions before publishing/reusing. One bounded retry for invalid output, no repeated login/usage failures. Synthesis follows analysis and receives evidence plus validated summaries.
- [x] Assembly and entry points: merge only interpretations into facts, disclose coverage in DOCX, replace API and console Claude defaults, fix prefix attribution, enforce existing permission checker before collection. Keep old parsing tests only if legacy code remains clearly isolated; remove obsolete direct API wiring.
- [x] Skills/fixture/docs: `.agents/skills/dataverse-{table-analysis,relationship-analysis,environment-synthesis}/SKILL.md`, versioned JSON schemas, sanitized fixture, setup/run/resume guide and ignored run outputs.
- [x] Verification (local checks and real user-run fixture passed; sandbox console repeat blocked by file permissions): fresh restore/build/regression tests; fake-runner failure/resume/cancellation tests; real Codex fixture smoke test if existing authentication is accessible, inspect DOCX XML. Report exact blockers separately from mock evidence.

## Validation cases

Evidence hashes reject mutation and traversal; failed collection does not become empty success. Scheduler rejects omitted/unknown/duplicate IDs, changed skill/input/output hashes and malformed JSON. Successful checkpoints avoid runner calls; corrupted checkpoints rerun. Cancellation publishes no output. Assembly counts raw components independent of AI prose and includes missing analysis/coverage.

## Execution ledger

2026-09-11: clean independent clone verified; branch `codex/local-skills-pipeline` created. CLI help inspected; current sandbox `codex login status` reports no home directory. Independent implementation continues.

2026-09-11: replaced the default Anthropic integration with deterministic evidence collection, the local Codex runner, validated/resumable skill scheduling, and evidence-derived DOCX assembly. Removed obsolete orchestration/prompt/parser/schema wiring and tests. API and console enforce the existing 13-privilege checker on the connected client and release task credential references before analysis; raw SDK/model diagnostics are not logged. Prefix ownership is unknown without publisher evidence. Null/missing application-user roles remain explicitly unavailable.

2026-09-11: independent review caught and resolved ambiguous duplicate JSON metadata keys, parent-ID case aliases, CLI partial-exit classification for expected exclusions, and the console workflow lock gap between analysis and DOCX publication. Evidence's SDK-null-collection, standard-table traversal, nontransactional reads, unpaged application-user and unsupported automation limitations are disclosed in coverage and the operator guide. The bundled managed reader solution was updated to 1.0.0.7 to include missing prvReadSystemUser; package/checker privilege parity is tested. Live role import remains untested.

2026-09-11: offline restore succeeded using existing NuGet cache and a workspace NuGet.Config with cleared sources after the sandbox blocked default roaming NuGet configuration. Full solution build (Shared/API/Console/Tests) succeeded. Full regression suite passed 318/318 before final runner diagnostic classification and workflow-lock verification. A real Codex fixture smoke attempt generated a partial DOCX but no valid AI stages: CLI exec returned Access is denied before model invocation, despite readable login status through the selected CODEX_HOME. This is an execution-environment blocker, not a successful model-backed smoke test. No live Dataverse call, role import, deployment, commit or push was performed.

2026-09-11 final local verification: `dotnet build DataverseDocAgent.sln --no-restore --nologo` succeeded across all four projects with 0 warnings and 0 errors. `dotnet test DataverseDocAgent.sln --no-build --no-restore --nologo` passed 321/321 tests, including process failures/cancellation, checkpoint invalidation/resume, snapshot integrity, authoritative DOCX publication/cancellation, expected-scope exit classification, role-package parity and regression coverage. An OpenXML schema check exposed and fixed the existing table border-order and missing-table-grid defects; the fake-runner DOCX now has zero OpenXML validation errors. The earlier partial smoke DOCX predates that rendering fix and should be regenerated before use.

2026-09-11 console concurrency verification: held `.workflow.lock` in a dedicated ignored run directory, then invoked `analyze` with a deliberately nonexistent executable. The console failed safely with exit 1 before analysis, wrote neither analysis-report.json nor report.docx, and made no model call. `git diff --check` is clean apart from informational line-ending conversion warnings; branch remains `codex/local-skills-pipeline`. `rg Anthropic src` returns no matches. The separately authorized final model smoke was also blocked by host filesystem permissions; it did not verify model analysis.

2026-09-11 final smoke outcome: a fresh helper had network permission, access to CODEX_HOME, and an explicit write grant for its output directory, but still received Access is denied for `D:\Development\DataverseDocAgent - Codex\.dataverse-runs\codex-smoke-helper` before any model call. It produced no helper outputs or checkpoints. Real model smoke remains NOT VERIFIED because of host filesystem permissions. The earlier `.dataverse-runs/codex-smoke/report.docx` contains zero valid AI stages, predates the renderer fix, and is not a successful deliverable. Local evidence remains the clean solution build (0 warnings/errors), 321/321 full regression tests, and a subsequent 20/20 scheduler test pass after the source-schema follow-up.

Manual remaining verification: open a normal signed-in PowerShell session with write access to the clone, change to `D:\Development\DataverseDocAgent - Codex`, and run:

```powershell
dotnet run --project src/DataverseDocAgent.Console -- analyze --snapshot fixtures/sanitized-snapshot --run .dataverse-runs/fixture-smoke
```

This analyzes only the sanitized fixture. It does not connect to Dataverse. Successful model-backed output must be checked for completed analysis stages and the expected fixture facts; a partial report or blocked command is not proof of a successful smoke test. No further model attempt was made by this implementation agent. Changes remain uncommitted in the independent clone; the original repository was not edited and nothing was pushed.

2026-09-11: user completed real subscription fixture run with explicit CLI path. Independent inspection verified three successful stages (targets5/1/8), complete required analysis coverage, checkpoint hashes/references, and actual DOCX OpenXML validation0errors with expected2tables/3fields/1relationship. Production scheduler replay of copied real checkpoints with a throwing runner reused all3stages with0runner calls. One exact console repeat in sandbox failed before analysis on .workflow.lock (Windows0x80070005); all9 original stage files retained hashes/mtime and original report/analysis JSON stayed unchanged. Current identity remains CodexSandboxOffline; Admin-owned run artifacts still lack named oaalaModify. No live Dataverse/model retry, application edits, commit or push during this verification.
