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
        // Story 3.6 code-review P11 — assert the prefix value too, not just
        // the count, so a regression that bucketed by full logical name
        // (instead of segment) would still fail this test.
        Assert.Equal("vel", summary.ClientPrefixes[0].Prefix);
        Assert.Equal(2, summary.ClientPrefixes[0].ComponentCount);
        // msdyn + cr3a7 → Microsoft bucket (cr3a7 matches ^cr[a-z0-9]*$).
        Assert.Equal(2, summary.MicrosoftPrefixes.Count);
        Assert.Contains(summary.MicrosoftPrefixes, p => p.Prefix == "msdyn");
        Assert.Contains(summary.MicrosoftPrefixes, p => p.Prefix == "cr3a7");
    }

    // Story 3.7 AC-11 — DocumentGenerateService must defend against a Claude
    // response that omits the `applicationUsers` key entirely (e.g. older
    // four-key shape from Story 3.5, or a prompt-drift regression). The
    // missing key parses as a null `ApplicationUsers` on AgentJsonModel and
    // is safe-coalesced to an empty list before the renderer sees it — the
    // pipeline must NOT raise AI_ERROR for this case.
    [Fact]
    public void ParseAgentJson_MissingApplicationUsersKey_DeserialisesToNull_ForSafeCoalesce()
    {
        const string raw = """
            {
              "organisation": { "environmentName": "Contoso" },
              "tables": [
                { "logicalName": "vel_account" }
              ]
            }
            """;

        var parsed = DocumentGenerateService.ParseAgentJson(raw);

        // Null sentinel — service-level safe-coalesce (parsed.ApplicationUsers?…
        // ?? Array.Empty<ApplicationUserInfo>()) turns this into the empty
        // Section 5 render branch (AC-10).
        Assert.Null(parsed.ApplicationUsers);
    }

    // Story 3.7 — null entry inside the applicationUsers array (analogous to
    // the Story 3.6 P13 tables-null filter). The service strips nulls before
    // the renderer touches the list.
    [Fact]
    public void ParseAgentJson_NullApplicationUserEntry_SurvivesAndIsFilteredAtServiceBoundary()
    {
        const string raw = """
            {
              "applicationUsers": [
                null,
                { "displayName": "Sync App", "applicationId": "11111111-1111-1111-1111-111111111111",
                  "roles": ["Reader"] }
              ]
            }
            """;

        var parsed = DocumentGenerateService.ParseAgentJson(raw);
        Assert.NotNull(parsed.ApplicationUsers);
        var filtered = parsed.ApplicationUsers!.Where(u => u is not null).ToList();

        Assert.Single(filtered);
        Assert.Equal("Sync App", filtered[0].DisplayName);
        Assert.Equal(new[] { "Reader" }, filtered[0].Roles);
    }

    // Story 3.6 code-review P13 — JSON `tables: [null, {...}]` is a real
    // possibility from a flaky agent payload. ParseAgentJson deserialises
    // the null as a real element; the service-level filter removes it before
    // the analyzer (and the downstream model consumers) see it.
    [Fact]
    public void ParseAgentJson_NullTableEntry_SurvivesAndIsFilteredAtServiceBoundary()
    {
        const string raw = """
            {
              "tables": [
                null,
                { "logicalName": "vel_account" }
              ]
            }
            """;

        var parsed = DocumentGenerateService.ParseAgentJson(raw);
        Assert.NotNull(parsed.Tables);
        // The service strips nulls before invoking PrefixAnalyzer / DocxBuilder.
        var filtered = parsed.Tables!.Where(t => t is not null).ToList();
        var summary  = PrefixAnalyzer.Analyze(filtered);

        Assert.Single(filtered);
        Assert.Equal("vel", summary.PrimaryClientPrefix);
    }
}
