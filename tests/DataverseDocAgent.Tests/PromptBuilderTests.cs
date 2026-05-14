// F-011, F-013 — Story 3.5 Mode 1 prompt-shape stability
using DataverseDocAgent.Api.Agent;

namespace DataverseDocAgent.Tests;

public class PromptBuilderTests
{
    [Fact]
    public void BuildMode1Prompt_MentionsAllFourMode1Tools_AndForbidsCodeFences()
    {
        var prompt = PromptBuilder.BuildMode1Prompt();

        // Tool names are part of the prompt-tool contract — drift would silently
        // change which tools Claude calls. Asserted by exact name.
        Assert.Contains("get_organisation_metadata", prompt);
        Assert.Contains("list_custom_tables",        prompt);
        Assert.Contains("get_table_fields",          prompt);
        Assert.Contains("get_relationships",         prompt);

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
}
