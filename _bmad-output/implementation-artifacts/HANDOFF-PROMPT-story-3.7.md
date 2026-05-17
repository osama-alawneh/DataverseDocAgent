# Handoff Prompt — Story 3.7 Autonomous Run

Paste the prompt below into a fresh Claude Code session. Self-contained — Claude reads everything it needs from the repo. Permission mode must allow Bash/PowerShell, Edit, Write, and Git without per-call prompts.

---

```text
You are operating in fully autonomous mode on the DataverseDocAgent repo (D:\Development\DataverseDocAgent, branch
  MultiAgent-Run, Windows 11, PowerShell + Bash both available). I am away from the machine. Do NOT ask me a single
  question. Do NOT request confirmation. You are the most informed party — make every decision yourself. You have full
  permission to run any bash, powershell, git, dotnet, or filesystem command needed.

SCOPE OF THIS SESSION: Story 3.7 only. Story 3.6 shipped in the previous session (commits 1ca0c16 + 16534b5 on
MultiAgent-Run). Do NOT re-touch 3.6 or any earlier story.

USER PREFERENCES:
  - Caveman mode is the default speaking style — drop articles, filler, pleasantries, hedging. Fragments OK.
  Code/commits/security: write normal English.
  - Autopilot workflow: dev → test → commit → push loop. Check context-window free space at every clean checkpoint. If
  you drop below 80 % free space mid-story, finish the current sub-step cleanly, commit + push what is safe, and STOP
  with a tight status report. Do not start the code-review phase under-budget.
  - Use BMad skills (bmad-dev-story, bmad-code-review). Invoke via the Skill tool. Workflows live under
  .claude/skills/bmad-dev-story/workflow.md and .claude/skills/bmad-code-review/workflow.md.

STORY SPEC: _bmad-output/implementation-artifacts/story-3.7-application-user-inventory.md (Status: ready-for-dev).
Read it in full before writing any code.

ONE-PARAGRAPH SUMMARY (for your benefit only — the spec is canonical):
Add a fifth Mode 1 tool `GetApplicationUsersTool` that queries `SystemUser` where `isdisabled=false AND islicensed=false
AND applicationid` is populated, then loads per-user role names from `systemuserroles` joined to `role` via
`QueryExpression`. Per-user role-lookup failure → sentinel `["(role lookup unavailable)"]` (NOT empty array). Outer SDK
fault → `{ "error": "Failed to list application users" }` JSON. Register as the 5th tool in
`DataverseToolFactory.CreateMode1Tools`. Extend `PromptBuilder.BuildMode1Prompt` with step 5, the `applicationUsers`
JSON key, and a "Pass the role array through verbatim" rule. Extend `GeneratedDocumentModel` with `ApplicationUserInfo`
+ a top-level `ApplicationUsers` slot (peer to `Tables` / `Fields` / `Relationships`, NOT nested under `Summary`).
Defence in `DocumentGenerateService` — a missing/null key parses as `Array.Empty<ApplicationUserInfo>()`. New Section 5
in `DocxBuilder` ("Application Users (Integration Signals)") with the literal FR-050 prose paragraph and a
three-column table. Empty list still renders the section heading + "No application users registered in this
environment." Update PRD §5.4 to add the `SystemUser | Read` row (the senior-consultant audit gap flagged in the
story Dev Notes — recommended option 1). Do NOT touch `ComplexityRater`, `PrefixAnalyzer`, `IGenerationPipeline`, or
`GenerationBackgroundService`. The four existing Mode 1 tools stay byte-for-byte unchanged.

CONTEXT-CRITICAL FILES — READ THESE BEFORE EDITING ANYTHING:
   1. _bmad-output/implementation-artifacts/story-3.7-application-user-inventory.md
   2. _bmad-output/implementation-artifacts/sprint-status.yaml
   3. _bmad-output/implementation-artifacts/story-3.6-publisher-prefix-intelligence.md   (latest precedent + Review
       Findings format to mirror)
   4. _bmad-output/implementation-artifacts/story-3.5-mode1-generation.md                (Mode 1 pipeline precedent)
   5. _bmad-output/implementation-artifacts/deferred-work.md
   6. src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs                        (per-row sub-query precedent —
       structurally identical to roles-per-user)
   7. src/DataverseDocAgent.Api/Agent/Tools/GetRelationshipsTool.cs                       (sibling-tool error contract +
       OperationCanceledException filter shape)
   8. src/DataverseDocAgent.Api/Agent/Tools/ListCustomTablesTool.cs                       (sibling-tool error contract)
   9. src/DataverseDocAgent.Api/Agent/Tools/DataverseToolFactory.cs                       (registration site for 5th tool)
  10. src/DataverseDocAgent.Api/Agent/PromptBuilder.cs                                    (prompt JSON shape — needs
       applicationUsers key + step 5 + role-passthrough rule)
  11. src/DataverseDocAgent.Api/Features/DocumentGenerate/DocumentGenerateService.cs      (RunPipelineAsync model build
       — add safe-coalesce of parsed.ApplicationUsers; AgentJsonModel needs ApplicationUsers field)
  12. src/DataverseDocAgent.Api/Documents/GeneratedDocumentModel.cs                       (add ApplicationUserInfo +
       required top-level ApplicationUsers slot)
  13. src/DataverseDocAgent.Api/Documents/DocxBuilder.cs                                  (Build() call-site for new
       Section 5; AppendApplicationUsersSection helper)
  14. tests/DataverseDocAgent.Tests/DataverseToolFactoryTests.cs                          (rename
       _ReturnsExactlyFourNamedTools → _Five; assert new tool in trailing slot)
  15. tests/DataverseDocAgent.Tests/PromptBuilderTests.cs                                 (assert get_application_users
       + applicationUsers output key mentioned)
  16. tests/DataverseDocAgent.Tests/DocxBuilderTests.cs                                   (BuildSampleModel needs
       ApplicationUsers slot; add populated + empty Section 5 tests)
  17. tests/DataverseDocAgent.Tests/DocumentGenerateServiceTests.cs                        (missing-key defence test)
  18. tests/DataverseDocAgent.Tests/GetTableFieldsToolTests.cs                             (reflection-based SDK
       metadata setter pattern — SetNonPublic / SetLabel helpers, copy the shape for SystemUser entity-construction
       in new tool tests)
  19. docs/prd.md §5.4 permission table (lines ~405-416)                                  (SystemUser | Read row to
       add; see Dev Notes option 1)
  20. C:\Users\oaala\.claude\projects\D--Development-DataverseDocAgent\memory\MEMORY.md and the files it links
  21. CLAUDE.md at repo root if present

PROJECT CONVENTIONS (extracted from existing source — match them exactly):
  - net8.0, Nullable enable, ImplicitUsings enable, file-scoped namespaces.
  - Annotation comment at top of every new file: `// F-055 — FR-050 — Integration Signal Detection: App User Inventory
    (Story 3.7)`. Add `// NFR-007` inline at the per-user role-lookup catch block (no-message-leak intent).
  - Sibling-tool error contract: structured JSON `{ "error": "Failed to list application users" }` on outer SDK fault.
    `OperationCanceledException` ALWAYS propagates — use `when (cancellationToken.IsCancellationRequested)` filter or
    a `when (ex is not OperationCanceledException)` filter, matching `ListCustomTablesTool` / `GetTableFieldsTool`.
  - Per-user role lookup: try/catch around the secondary `QueryExpression` against `systemuserroles` linked to `role`.
    On `FaultException<OrganizationServiceFault>`, `TimeoutException`, `CommunicationException`, or
    `HttpRequestException` → set the user's role list to the single-element sentinel array
    `[ "(role lookup unavailable)" ]`. DO NOT swallow `OperationCanceledException` in this catch.
  - DTOs use System.Text.Json with `PropertyNamingPolicy.CamelCase` + `DefaultIgnoreCondition.WhenWritingNull` when
    serialised.
  - Tests use xUnit + Moq. Theory/InlineData for boundary coverage. Mock `IOrganizationService.RetrieveMultiple` and
    return controlled `EntityCollection` / `Entity` instances. SystemUser-entity construction uses the same
    reflection-based property-setter pattern that `GetTableFieldsToolTests` and `GetRelationshipsToolTests` already use
    for `EntityMetadata` / `AttributeMetadata`. Copy the helper shape, do not extract a shared helper (that work is
    already deferred in deferred-work.md under Story 3.4).
  - NFR-007: never log credential values. The per-user role-lookup catch must NOT call `ex.Message` — log only the
    user's id (Guid) + a fixed string like `"Role lookup failed for application user {UserId}"`.
  - Required-init contract on `GeneratedDocumentModel.ApplicationUsers` (top-level, peer to `Tables`). Required so a
    future refactor cannot silently bypass the tool result.
  - Reflection on SDK metadata uses internal property setters — the existing tests already include the
    `SetNonPublic` helper. Copy verbatim into `GetApplicationUsersToolTests.cs` (don't try to extract — deferred-work
    already tracks that consolidation).

EXECUTION LOOP — PHASE A — IMPLEMENT:
  A1. Invoke the Skill tool with skill: "bmad-dev-story" and args:
    "_bmad-output/implementation-artifacts/story-3.7-application-user-inventory.md". Follow the workflow exactly. The
    skill marks the story in-progress in sprint-status, expects you to walk every task/subtask, write tests where
    appropriate, run the full suite green, and finally flip the story Status to "review" plus sprint-status to review.
  A2. After every meaningful Edit cluster: `dotnet build DataverseDocAgent.sln -nologo`. Fix compile errors immediately;
    do not batch.
  A3. After implementation: `dotnet test tests/DataverseDocAgent.Tests/DataverseDocAgent.Tests.csproj -nologo`.
    Require Failed: 0. New tests per the spec's AC-12 must be present. Test count after 3.6 baseline = 230; expect
    at least ~240+ after 3.7 lands (factory test rename, prompt test extension, tool tests, docx tests, service test).
  A4. Commit. Subject: "Implement story 3.7 application user inventory". Body explains WHY (FR-050 — surface
    integration-signal app users at first glance), lists the user-visible surface change (new Section 5), test count,
    and ends with:
        Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
      Use HEREDOC for the commit message — never -m with multi-line escapes.
  A5. `git push origin MultiAgent-Run`.

EXECUTION LOOP — PHASE B — ADVERSARIAL CODE REVIEW (mirrors Story 3.6 review run):
  B1. Save the dev commit's diff: `git diff HEAD~1 HEAD >
    _bmad-output/implementation-artifacts/story-3.7-diff.patch`. Also copy to /tmp/story-3.7-diff.patch for
    platform-portability.
  B2. Self-decide every HALT in the bmad-code-review workflow:
        - Review target: branch-diff vs HEAD~1 (the commit you just pushed in A5).
        - Spec file: _bmad-output/implementation-artifacts/story-3.7-application-user-inventory.md.
        - Review mode: "full".
  B3. Spawn three parallel review subagents (subagent_type: general-purpose, run_in_background: true) with these roles:
        - Blind Hunter — diff only, no project access. Invokes bmad-review-adversarial-general skill.
        - Edge Case Hunter — diff + read access. Invokes bmad-review-edge-case-hunter skill.
        - Acceptance Auditor — diff + spec + PRD/architecture refs. Validates every AC and FR-050 verbatim.
      Same prompt shape used in Story 3.6 review (see git log — 1ca0c16..16534b5 — for the exact prompts; they sit
      in the Story 3.6 conversation history if available, else mirror them by inspection of the patch commit's body).
      Diff path passed in both as /tmp and as _bmad-output/implementation-artifacts/ so either platform works.
  B4. When all three return, triage:
        - Apply every HIGH and MED finding that has an unambiguous fix.
        - Apply every test-coverage gap that is a true regression risk.
        - Dismiss findings already acknowledged in the spec's Dev Notes (MVP scope, per-user-isolation rationale,
          sync-SDK limitation, etc.).
        - Defer architectural / SDK-version / pre-existing findings to
          _bmad-output/implementation-artifacts/deferred-work.md under a new heading
          "## Deferred from: code review of story-3.7 (2026-05-14)". Match the style of the existing Story 3.6
          deferred entries.
  B5. Append a "### Review Findings" sub-section to the story file with every patch (checked off after fix), every
    defer (checked off, marked deferred), and a one-line dismissal note for the noise — mirror the Story 3.6
    story file's Review Findings section.
  B6. Apply patches. After each cluster: rebuild + retest. Suite must stay green.
  B7. Flip story Status to "done". Update sprint-status.yaml: story 3-7 → done, last_updated → today (2026-05-14),
    top comment to current state.
  B8. Commit. Subject: "Apply story 3.7 code review patches (P1-PX)". Body: bullet list of patches with file:line
    refs, plus passing-test count. Co-Authored-By footer.
  B9. `git push origin MultiAgent-Run`.

EXECUTION LOOP — PHASE C — MEMORY + EPIC-3 ROLLUP + HANDOFF:
  C1. Update C:\Users\oaala\.claude\projects\D--Development-DataverseDocAgent\memory\project-state.md:
        - Mark 3.7 done in the Epic 3 row.
        - Story 3.7 is the LAST Epic 3 P2-scope story. Set the Epic 3 row to "COMPLETE (3.0-3.8 done as of 2026-05-14)".
        - Move "Implement Story 3.7" off the Next Immediate Actions list.
        - Promote "Run bmad-retrospective for Epic 3" to the top of the actions list.
        - Demote the manual E2E baseline action behind the retrospective.
  C2. Do NOT touch any other story's spec, source files, or tests. 3.0–3.6 + 3.8 are all done.
  C3. Do NOT start the Epic 3 retrospective in this session — leave it as the top action for the next session.
  C4. Final caveman-mode summary message: commits + push state + test counts + next sensible action (run
    bmad-retrospective for Epic 3 in a fresh session, OR perform the manual E2E NFR-001 baseline run against a real
    Dataverse + Anthropic key).

PRD §5.4 PERMISSION-TABLE EDIT
The story's Dev Notes recommend option 1 — add a single `SystemUser | Read | "List application users for integration
signal detection" | Never` row to PRD §5.4 in the SAME commit as the implementation. Do this. Update
`SecurityCheckService.RequiredPrivileges` (or the equivalent list — grep for `prvReadUser` and the table-of-privileges
constant) to include `prvReadSystemUser` if it is not already present. If touching the security-check service requires
test edits beyond a single inline-data row addition, STOP and emit a status report — the diversion is outside the
story's "Files modified" list.

USAGE-BUDGET CHECK
After Phase A push and again after Phase B push, look at remaining free context space. If <80 % free at either of
those checkpoints, STOP cleanly — finish the in-flight push, emit a tight status report, and end the run. Do not
start a new phase under-budget.

ON FAILURE — STOP AND EMIT TIGHT STATUS, DO NOT RECOVER BLINDLY:
  - dotnet build fails after three consecutive Edit attempts to fix the same error.
  - dotnet test fails for a reason that requires architectural change beyond the story's stated scope.
  - A code-review HIGH-severity finding cannot be patched without touching files outside the story's "Files modified"
    list.
  - The Anthropic API returns a non-rate-limit, non-auth error from inside a subagent run.
  - PRD §5.4 cannot be updated without changing more than the single SystemUser row.
  - `SecurityCheckService.RequiredPrivileges` update breaks more than the directly related test inline-data row.

DO NOT:
  - Ask me anything mid-run.
  - Skip the bmad-code-review phase.
  - Skip the deferred-work.md update.
  - Skip the PRD §5.4 row addition (option 1 is mandated by the story Dev Notes).
  - Commit without the Co-Authored-By footer.
  - Modify files outside the story's stated "Files modified" list unless an adversarial finding demands it; then
    document the diversion in the patch commit body.
  - Open a PR (the user opens PRs manually).
  - Run `git rebase -i`, `git commit --amend`, `git push --force`, or any history-rewriting command.
  - Edit MEMORY.md directly — only edit memory/*.md files referenced from MEMORY.md.
  - Touch story 3.6's PrefixAnalyzer / PublisherPrefixSummary / DocxBuilder Publisher Prefix Summary section.
  - Re-touch anything in stories 3.0–3.5 or 3.8 (all done).
  - Extract a shared SDK-metadata reflection helper for the new tool tests — that consolidation is already deferred.

Begin now. Read _bmad-output/implementation-artifacts/story-3.7-application-user-inventory.md in full, then invoke
bmad-dev-story on it.
```

---

## Pointers for next session — quick recap

| Item | Value |
|------|-------|
| Branch | MultiAgent-Run |
| Story | 3.7 application user inventory (ready-for-dev) |
| Last shipped | 3.6 publisher prefix intelligence — commits `1ca0c16` + `16534b5`; 230 tests pass |
| Sprint-status path | `_bmad-output/implementation-artifacts/sprint-status.yaml` |
| Story spec path | `_bmad-output/implementation-artifacts/story-3.7-application-user-inventory.md` |
| Test command | `dotnet test tests/DataverseDocAgent.Tests/DataverseDocAgent.Tests.csproj -nologo` |
| Build command | `dotnet build DataverseDocAgent.sln -nologo` |
| Recent commit style | see `git log --oneline -10` — `"Implement story X"` then `"Apply story X code review patches (P1-PX)"` |
| Memory dir | `C:\Users\oaala\.claude\projects\D--Development-DataverseDocAgent\memory\` |
| Stop rule | <80 % free context after a clean checkpoint |
| Final action after 3.7 done | "Run bmad-retrospective for Epic 3" (next session, not this one) |
