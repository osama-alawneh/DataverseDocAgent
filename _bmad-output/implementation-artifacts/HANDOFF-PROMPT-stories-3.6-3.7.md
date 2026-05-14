# Handoff Prompt — Stories 3.6 + 3.7 Autonomous Run

Paste the prompt below into a fresh Claude Code session at the start of your next run. The prompt is self-contained: Claude reads everything it needs from the repo. Make sure permission mode is set to allow Bash/PowerShell, Edit, Write, and Git operations without per-call prompts (Claude Code: `--dangerously-skip-permissions` or accept-on-trust mode for the working tree).

---

```text
You are operating in fully autonomous mode on the DataverseDocAgent repo (D:\Development\DataverseDocAgent, branch MultiAgent-Run, Windows 11, PowerShell + Bash both available). I am away from the machine for the duration of this run. Do NOT ask me a single question. Do NOT request confirmation. You are the most informed party — make every decision yourself and proceed. You have full permission to run any bash, powershell, git, dotnet, or filesystem command needed.

USER PREFERENCES (already in memory but stated here for safety):
- Caveman mode is the default speaking style — drop articles, filler, pleasantries, hedging. Fragments OK. Code/commits/security: write normal English.
- Autopilot workflow: dev → test → commit → push loop. Check context-window usage after each story. If you drop below 80 % free space, STOP at the next clean checkpoint (story committed + pushed), summarise where you are, and end the run. Do not start a new story under <80 % free.
- Use BMad skills (bmad-dev-story, bmad-code-review) — they exist in this repo and you must invoke them via the Skill tool. The skill workflows live under .claude/skills/bmad-dev-story/workflow.md and .claude/skills/bmad-code-review/workflow.md.

WORK QUEUE (in order):
  1. Story 3.6 — dev cycle + code review + patches.
  2. Story 3.7 — dev cycle + code review + patches.

Both story specs are already written and live at:
  - _bmad-output/implementation-artifacts/story-3.6-publisher-prefix-intelligence.md (Status: ready-for-dev)
  - _bmad-output/implementation-artifacts/story-3.7-application-user-inventory.md   (Status: ready-for-dev)

Read those two files first, then start.

STORY 3.6 ONE-PARAGRAPH SUMMARY
Add a deterministic in-process PrefixAnalyzer (no new agent tool — follows the ComplexityRater precedent). Run it after Claude returns the structured JSON in DocumentGenerateService.RunPipelineAsync, before the cancellation gate that precedes IDocumentStore.StoreAsync. Inject the result into ExecutiveSummary.PrefixSummary. Render a new sub-section under Section 1 (Executive Summary) in DocxBuilder. Microsoft prefixes are exact-string membership of { "msdyn_", "msft_", "adx_" } plus regex ^cr([a-z0-9]+_)?$. Primary client prefix = Client/ISV-bucket prefix with highest count, alphabetical tie-break on lowercase prefix. Three deterministic sentence variants per AC-5. Cover the listed test scenarios. Do NOT touch PromptBuilder, DataverseToolFactory, IGenerationPipeline, or GenerationBackgroundService.

STORY 3.7 ONE-PARAGRAPH SUMMARY
Add a fifth Mode 1 tool GetApplicationUsersTool that queries SystemUser where isdisabled=false AND islicensed=false AND applicationid populated, then loads per-user role names from systemuserroles → role via QueryExpression. Per-user role-lookup failure → sentinel ["(role lookup unavailable)"] (NOT empty array). Outer SDK fault → { "error": "Failed to list application users" } JSON. Register as 5th tool in DataverseToolFactory.CreateMode1Tools. Extend PromptBuilder.BuildMode1Prompt with step 5 + applicationUsers JSON key + "Pass the role array through verbatim" rule. Extend GeneratedDocumentModel with ApplicationUserInfo + ApplicationUsers at top level. Defence in DocumentGenerateService — missing key parses as Array.Empty. New Section 5 in DocxBuilder ("Application Users (Integration Signals)") with the literal FR-050 prose paragraph and a three-column table. Empty list still renders the section heading + "No application users registered in this environment." Update PRD §5.4 to add the SystemUser read permission row (the senior-consultant audit gap flagged in the story Dev Notes).

CONTEXT-CRITICAL FILES — READ THESE BEFORE EDITING ANYTHING
  1. _bmad-output/implementation-artifacts/story-3.6-publisher-prefix-intelligence.md
  2. _bmad-output/implementation-artifacts/story-3.7-application-user-inventory.md
  3. _bmad-output/implementation-artifacts/sprint-status.yaml
  4. _bmad-output/implementation-artifacts/story-3.5-mode1-generation.md  (precedent + deferred-work)
  5. _bmad-output/implementation-artifacts/deferred-work.md
  6. src/DataverseDocAgent.Api/Features/DocumentGenerate/DocumentGenerateService.cs
  7. src/DataverseDocAgent.Api/Features/DocumentGenerate/ComplexityRater.cs   (PrefixAnalyzer precedent)
  8. src/DataverseDocAgent.Api/Documents/DocxBuilder.cs
  9. src/DataverseDocAgent.Api/Documents/GeneratedDocumentModel.cs
 10. src/DataverseDocAgent.Api/Agent/PromptBuilder.cs
 11. src/DataverseDocAgent.Api/Agent/Tools/DataverseToolFactory.cs
 12. src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs            (per-row sub-query precedent for 3.7)
 13. src/DataverseDocAgent.Api/Agent/Tools/ListCustomTablesTool.cs           (sibling-tool error contract)
 14. tests/DataverseDocAgent.Tests/DocxBuilderTests.cs                       (BuildSampleModel needs PrefixSummary + ApplicationUsers when those slots are required)
 15. tests/DataverseDocAgent.Tests/DataverseToolFactoryTests.cs              (rename _ReturnsExactlyFourNamedTools → _Five for 3.7)
 16. C:\Users\oaala\.claude\projects\D--Development-DataverseDocAgent\memory\MEMORY.md (and the files it links)
 17. CLAUDE.md if present at repo root

PROJECT CONVENTIONS (extracted from existing source — match them exactly)
- net8.0, Nullable enable, ImplicitUsings enable, file-scoped namespaces.
- Annotation comments at top of every new file: // F-XXX — FR-XXX. Use the F-codes already named in each story spec.
- Sibling-tool error contract: structured JSON { error, tableName? } on SDK fault, OperationCanceledException always propagates via `when (cancellationToken.IsCancellationRequested)` filter.
- Reflection-cached property lookups on SDK metadata (see GetTableFieldsTool).
- Compiled regex for shape validation, case-sensitive against lowercase logical names.
- DTOs use System.Text.Json with PropertyNamingPolicy.CamelCase + DefaultIgnoreCondition.WhenWritingNull.
- Tests use xUnit + Moq. Prefer Theory/InlineData for boundary coverage. Mock IOrganizationService surface.
- NFR-007: never log credential values; log only structured fields + exception types. Story 3.5 P10 set the bar — match it.

EXECUTION LOOP FOR EACH STORY
  Phase A — Implement:
    A1. Invoke the Skill tool with skill: "bmad-dev-story" and args: the story file path. Follow the workflow exactly. The skill marks the story in-progress in sprint-status, expects you to walk every task/subtask, write tests first where appropriate, run the full test suite green, and finally flip the story Status to "review" plus sprint-status to review.
    A2. Run `dotnet build DataverseDocAgent.sln -nologo` after every meaningful Edit cluster. Fix compile errors immediately; do not batch.
    A3. Run `dotnet test tests/DataverseDocAgent.Tests/DataverseDocAgent.Tests.csproj -nologo` and require Failed: 0. New tests added per the story spec's AC-12 (3.6) / AC-12 (3.7) must be present.
    A4. Commit. Style: imperative subject ≤72 chars matching this repo's history. Example subjects:
         - "Implement story 3.6 publisher prefix intelligence"
         - "Implement story 3.7 application user inventory"
       Body explains WHY, lists the user-facing surface change, ends with the Co-Authored-By line:
         Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
       Use a HEREDOC for the commit message — never -m with multi-line escapes.
    A5. Push with `git push origin MultiAgent-Run`.

  Phase B — Adversarial code review (mirrors what we did for 3.5):
    B1. Save the dev commit's diff to _bmad-output/implementation-artifacts/story-3.<N>-diff.patch via `git diff HEAD~1 HEAD > <path>`.
    B2. Invoke the Skill tool with skill: "bmad-code-review". The workflow asks several questions — answer them yourself from context. Pick "branch diff vs HEAD~1" / "uncommitted-equivalent" mode; spec file is the story file you just shipped. Skip user-confirmation HALTs by self-deciding.
    B3. Spawn three parallel review subagents (general-purpose subagent_type, run_in_background=true) with the bmad-review-adversarial-general, bmad-review-edge-case-hunter, and Acceptance Auditor prompts — same shape as the Story 3.5 review run; copy that pattern verbatim. Diff path goes both as /tmp and as _bmad-output/implementation-artifacts/ so either platform's path works.
    B4. When all three return, triage:
        - Apply every HIGH and MED severity finding that has an unambiguous fix.
        - Apply every test-coverage gap that is a true regression risk.
        - Dismiss findings already acknowledged in the spec's Dev Notes (scope acceptance, MVP simplification, etc.).
        - Defer architectural / SDK-version / pre-existing findings to _bmad-output/implementation-artifacts/deferred-work.md under a new heading "## Deferred from: code review of story-3.<N> (2026-05-14)" — match the style of the existing deferred-work entries.
    B5. Append a "### Review Findings" sub-section to the story file with every patch (checked off after fix), every defer (checked off, marked deferred), and a one-line dismissal note for the noise.
    B6. Apply patches. After each cluster of patches: rebuild + retest. Suite must stay green.
    B7. Flip story Status to "done". Update sprint-status.yaml: story → done, last_updated → today (2026-05-14), top comment to current state.
    B8. Commit:
         - Subject: "Apply story 3.<N> code review patches (P1-PX)"
         - Body: bullet list of patches with file:line refs, plus passing-test count.
         - Co-Authored-By footer.
    B9. Push.

  Phase C — Memory + handoff:
    - Update C:\Users\oaala\.claude\projects\D--Development-DataverseDocAgent\memory\project-state.md: mark this story done, update the Epic 3 row and "Next Immediate Actions" list.
    - If this is story 3.7 (the last in Epic 3 P2 scope), set Epic 3's row in the Epic Status table to "COMPLETE (3.0-3.8 done as of 2026-05-14)" and add an action "Run bmad-retrospective for Epic 3" as the new top item.

USAGE-BUDGET CHECK
After every story committed + pushed, look at remaining free context space (the auto-injected context-percent reading) and the wall-clock cost. If free space is <80 % when you reach the end of a story, STOP. Output a one-page summary: what was done, what is next, the next handoff prompt (or a pointer to this file's analogue updated for the remaining story). Do NOT start the next story under-budget.

ON FAILURE
If any of these happen, STOP and emit a tight status report — do NOT recover blindly:
  - dotnet build fails after three consecutive Edit attempts to fix the same error.
  - dotnet test fails for a reason that requires architectural change beyond the story's stated scope.
  - A code-review HIGH-severity finding cannot be patched without touching files outside the story's "Files modified" list.
  - The Anthropic API returns a non-rate-limit, non-auth error from inside a subagent run.
  - PRD §5.4 cannot be updated without changing more than the single SystemUser row.

DO NOT
  - Ask me anything mid-run.
  - Skip the bmad-code-review phase.
  - Skip the deferred-work.md update.
  - Commit without the Co-Authored-By footer.
  - Modify files outside each story's stated "Files modified" list unless an adversarial finding demands it; then document the diversion in the patch commit body.
  - Open a PR (the user opens PRs manually).
  - Run `git rebase` interactive, `--amend`, `push --force`, or any history-rewriting command.
  - Edit MEMORY.md directly — only edit memory/*.md files referenced from MEMORY.md.
  - Touch unrelated stories (3.5 done; 3.0-3.4 + 3.8 done — don't go re-touching anything in them).

WHEN DONE WITH EVERYTHING
Emit one final caveman-mode message summarising both stories' commits + push state + test counts, and list the next sensible action (e.g., "Run bmad-retrospective for Epic 3" or "Manual E2E baseline on a real environment").

Begin now. Read the two story spec files first, then invoke bmad-dev-story on 3.6.
```

---

## Pointers for next session — quick recap (already inside the prompt above, repeated here for the human glance)

| Item | Value |
|------|-------|
| Branch | MultiAgent-Run |
| Stories | 3.6 publisher prefix (ready-for-dev), 3.7 app user inventory (ready-for-dev) |
| Sprint-status path | `_bmad-output/implementation-artifacts/sprint-status.yaml` |
| Story spec paths | `_bmad-output/implementation-artifacts/story-3.6-publisher-prefix-intelligence.md`, `…story-3.7-application-user-inventory.md` |
| Test command | `dotnet test tests/DataverseDocAgent.Tests/DataverseDocAgent.Tests.csproj -nologo` |
| Build command | `dotnet build DataverseDocAgent.sln -nologo` |
| Recent commit style | see `git log --oneline -10` — "Implement story X" then "Apply story X code review patches (P1-PX)" |
| Memory dir | `C:\Users\oaala\.claude\projects\D--Development-DataverseDocAgent\memory\` |
| Stop rule | <80 % free context after a clean checkpoint |
