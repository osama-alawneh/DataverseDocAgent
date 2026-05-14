// F-011, F-013 — Story 3.5 Mode 1 prompt-shape stability
using DataverseDocAgent.Api.Agent;

namespace DataverseDocAgent.Tests;

public class PromptBuilderTests
{
    [Fact]
    public void BuildMode1Prompt_MentionsAllFiveMode1Tools_AndForbidsCodeFences()
    {
        var prompt = PromptBuilder.BuildMode1Prompt();

        // Tool names are part of the prompt-tool contract — drift would silently
        // change which tools Claude calls. Asserted by exact name.
        Assert.Contains("get_organisation_metadata", prompt);
        Assert.Contains("list_custom_tables",        prompt);
        Assert.Contains("get_table_fields",          prompt);
        Assert.Contains("get_relationships",         prompt);
        // Story 3.7 — F-055 / FR-050. Fifth Mode 1 tool.
        Assert.Contains("get_application_users",     prompt);

        Assert.Contains("JSON", prompt);
        Assert.Contains("no markdown code fences", prompt);
    }

    [Fact]
    public void BuildMode1Prompt_ForbidsClaudeFromInventingComplexityRating()
    {
        // FR-011 — complexityRating is deterministic in C#, NEVER from Claude.
        // The prompt explicitly tells Claude not to emit it; this test pins
        // that instruction against future prompt edits.
        var prompt = PromptBuilder.BuildMode1Prompt();
        Assert.Contains("Do NOT invent a `complexityRating`", prompt);
    }

    [Fact]
    public void BuildMode1Prompt_ContainsStrictOutputFormatRule()
    {
        // E2E hotfix 2026-05-14 (R-HF-5) — Claude was emitting a prose
        // preamble and a markdown bullet list before the JSON object,
        // causing AI_ERROR (JsonException). The prompt's OUTPUT FORMAT
        // block tells Claude the first character must be `{` and the
        // last must be `}`. Pin the rule so a future prompt edit cannot
        // silently drop it.
        var prompt = PromptBuilder.BuildMode1Prompt();
        Assert.Contains("OUTPUT FORMAT", prompt);
        Assert.Contains("first character MUST be `{`", prompt);
        Assert.Contains("last character MUST be `}`", prompt);
    }

    [Fact]
    public void BuildMode1Prompt_DeclaresApplicationUsersOutputKey_AndPassthroughRule()
    {
        // Story 3.7 AC-7 — Claude's JSON shape must gain a top-level
        // `applicationUsers` key alongside the existing organisation / tables /
        // fields / relationships / keyObservations keys, and the prompt must
        // explicitly forbid redacting or summarising role names so the renderer
        // can surface the verbatim role list (FR-050).
        var prompt = PromptBuilder.BuildMode1Prompt();
        Assert.Contains("applicationUsers", prompt);
        Assert.Contains("Pass the role array through verbatim", prompt);
    }
}
