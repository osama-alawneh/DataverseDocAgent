// F-001, F-013, NFR-014 — Story 3.5 DocumentGenerateService pipeline tests
// F-047 — Story 3.6 PrefixSummary enrichment integration test
using System.Text.Json;
using DataverseDocAgent.Api.Documents;
using DataverseDocAgent.Api.Features.DocumentGenerate;
using DataverseDocAgent.Api.Jobs;

namespace DataverseDocAgent.Tests;

public class DocumentGenerateServiceTests
{
    [Theory]
    [InlineData("{\"organisation\":{\"environmentName\":\"x\"}}",            "x")]
    [InlineData("```json\n{\"organisation\":{\"environmentName\":\"x\"}}\n```", "x")]
    [InlineData("```\n{\"organisation\":{\"environmentName\":\"x\"}}\n```",    "x")]
    public void ParseAgentJson_StripsCodeFences_ParsesOrganisation(string raw, string expectedName)
    {
        var model = DocumentGenerateService.ParseAgentJson(raw);
        Assert.NotNull(model);
        Assert.Equal(expectedName, model.Organisation?.EnvironmentName);
    }

    [Fact]
    public void ParseAgentJson_EmptyResponse_ThrowsAiError()
    {
        var ex = Assert.Throws<GenerationFailureException>(
            () => DocumentGenerateService.ParseAgentJson("   "));
        Assert.Equal(JobFailureCodes.AiError, ex.Code);
        Assert.True(ex.SafeToRetry);
    }

    [Fact]
    public void ParseAgentJson_InvalidJson_ThrowsAiError()
    {
        var ex = Assert.Throws<GenerationFailureException>(
            () => DocumentGenerateService.ParseAgentJson("not-json"));
        Assert.Equal(JobFailureCodes.AiError, ex.Code);
    }

    [Fact]
    public void StripCodeFences_NoFences_ReturnsTrimmedInput()
    {
        var result = DocumentGenerateService.StripCodeFences("  {\"k\":1}  ");
        Assert.Equal("{\"k\":1}", result);
    }

    [Fact]
    public void StripCodeFences_NoLeadingFence_PreservesTrailingBackticks()
    {
        // Story 3.5 code-review P4 — a fence-less JSON body whose string content
        // ends in three backticks must survive intact. Previously the trailing
        // strip ran unconditionally and corrupted the body.
        const string body = "{\"x\":\"code: ```\"}";
        var result = DocumentGenerateService.StripCodeFences(body);
        Assert.Equal(body, result);
    }

    [Fact]
    public void StripCodeFences_LeadingJsonFence_StripsBothEnds()
    {
        var result = DocumentGenerateService.StripCodeFences("```json\n{\"x\":1}\n```");
        Assert.Equal("{\"x\":1}", result);
    }

    // Story 3.6 — F-047. The Mode 1 enrichment step feeds PrefixAnalyzer the
    // tables Claude returns. The orchestrator and controller layers are
    // unchanged, so a parse → analyze handshake test is enough to lock in
    // that the enriched model carries a PrefixSummary derived from the JSON.
    [Fact]
    public void ParseAgentJson_TablesFeedPrefixAnalyzer_ProducesEnrichedSummary()
    {
        const string raw = """
            {
              "organisation": { "environmentName": "Contoso" },
              "tables": [
                { "logicalName": "vel_account" },
                { "logicalName": "vel_contact" },
                { "logicalName": "msdyn_thing" },
                { "logicalName": "cr3a7_widget" }
              ]
            }
            """;

        var parsed   = DocumentGenerateService.ParseAgentJson(raw);
        var tables   = (IReadOnlyList<TableInfo>?)parsed.Tables ?? Array.Empty<TableInfo>();
        var summary  = PrefixAnalyzer.Analyze(tables);

        Assert.Equal("vel", summary.PrimaryClientPrefix);
        Assert.False(summary.NoClientPrefixDetected);
        Assert.Equal(2, summary.ClientPrefixes[0].ComponentCount);
        // msdyn + cr3a7 → Microsoft bucket (cr3a7 matches ^cr[a-z0-9]*$).
        Assert.Equal(2, summary.MicrosoftPrefixes.Count);
        Assert.Contains(summary.MicrosoftPrefixes, p => p.Prefix == "msdyn");
        Assert.Contains(summary.MicrosoftPrefixes, p => p.Prefix == "cr3a7");
    }
}
