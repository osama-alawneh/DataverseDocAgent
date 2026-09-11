# DataverseDocAgent

Generate Dataverse documentation with .NET collection, repository skills executed through your authenticated local Codex CLI, and deterministic DOCX assembly.

The supported inventory includes custom tables, their custom fields and relationships, organisation metadata, and application users. Coverage and missing analysis are disclosed in every report. Model analysis cannot replace the collected inventory or its counts.

Start with the [local Codex setup and run guide](docs/LOCAL-CODEX.md). The included sanitized fixture can exercise analysis without connecting to Dataverse:

```powershell
dotnet run --project src/DataverseDocAgent.Console -- analyze --snapshot fixtures/sanitized-snapshot --run .dataverse-runs/fixture
```

This requires .NET 8, Codex CLI, and an existing ChatGPT login for Codex. Analysis sends selected metadata batches to the hosted model through that login. No Anthropic API key is used.
