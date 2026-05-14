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

    // E2E hotfix 2026-05-14 (R-HF-5) — Claude routinely chats before the
    // JSON despite the "JSON only" prompt rule. TrimToJsonObject anchors
    // on the first `{`/`[` and last `}`/`]` so a prose preamble / tail
    // does not abort parsing.
    [Fact]
    public void TrimToJsonObject_ProsePreamble_TrimsToObject()
    {
        const string raw =
            "All data has been collected. Here is the JSON.\n\n"
            + "- Some bullet point about tables.\n\n"
            + "{ \"organisation\": { \"environmentName\": \"x\" } }";
        var trimmed = DocumentGenerateService.TrimToJsonObject(raw);
        Assert.StartsWith("{", trimmed);
        Assert.EndsWith("}",   trimmed);
        Assert.DoesNotContain("All data has been collected.", trimmed);
        Assert.DoesNotContain("Some bullet point",            trimmed);
    }

    [Fact]
    public void TrimToJsonObject_ProseTail_TrimsToObject()
    {
        const string raw =
            "{ \"organisation\": { \"environmentName\": \"x\" } }\n\n"
            + "Let me know if you need more detail.";
        var trimmed = DocumentGenerateService.TrimToJsonObject(raw);
        Assert.EndsWith("}", trimmed);
        Assert.DoesNotContain("Let me know", trimmed);
    }

    [Fact]
    public void TrimToJsonObject_AlreadyClean_IsNoOp()
    {
        const string raw = "{\"x\":1}";
        Assert.Equal(raw, DocumentGenerateService.TrimToJsonObject(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TrimToJsonObject_NullOrWhitespace_ReturnsEmpty(string? raw)
    {
        Assert.Equal(string.Empty, DocumentGenerateService.TrimToJsonObject(raw!));
    }

    [Fact]
    public void TrimToJsonObject_NoBraces_PassesThroughForUpstreamError()
    {
        // No `{` or `[` — return as-is so the JSON parser (or empty-
        // response guard) surfaces the actual problem rather than this
        // helper silently swallowing it.
        const string raw = "Claude returned only prose, no JSON at all.";
        Assert.Equal(raw, DocumentGenerateService.TrimToJsonObject(raw));
    }

    [Fact]
    public void ParseAgentJson_ProsePreambleAroundJson_ParsesCleanly()
    {
        // End-to-end check: ParseAgentJson must survive a Claude reply
        // that wraps a valid JSON object in conversational prose. The
        // existing code-fence + new prose-trim path collapse to the
        // object before deserialisation.
        const string raw =
            "All data has been collected. Here is the JSON:\n\n"
            + "```json\n"
            + "{ \"organisation\": { \"environmentName\": \"Contoso\" }, \"tables\": [], \"keyObservations\": [] }\n"
            + "```\n\n"
            + "Let me know if you need more detail.";
        var parsed = DocumentGenerateService.ParseAgentJson(raw);
        Assert.NotNull(parsed);
        Assert.Equal("Contoso", parsed.Organisation?.EnvironmentName);
    }

    // E2E hotfix 2026-05-14 — pin the runtime hypothesis that drove the
    // RateLimitsExceeded catch reordering in `RunAsync`. The Anthropic SDK
    // makes `RateLimitsExceeded` derive from
    // `System.Net.Http.HttpRequestException`; if a future SDK upgrade
    // flips the base class, the network-fault filter no longer captures
    // rate-limit 429s and the catch ordering can be reconsidered. This
    // assertion is the single source of truth for the ordering invariant.
    [Fact]
    public void RateLimitsExceeded_DerivesFromHttpRequestException_PinsCatchOrdering()
    {
        Assert.True(
            typeof(Anthropic.SDK.RateLimitsExceeded).IsSubclassOf(typeof(System.Net.Http.HttpRequestException)),
            "Catch ordering in DocumentGenerateService.RunAsync depends on this hierarchy — "
            + "if RateLimitsExceeded no longer derives from HttpRequestException, the dedicated "
            + "Anthropic catch can be moved back below the network-fault filter without misclassification.");
    }

    // E2E hotfix 2026-05-14 — `TruncateForLog` bounds the forensic dump
    // emitted when a Mode 1 JSON parse fails. Pin the head/tail/elision
    // contract so a future "let's just log the whole response" regression
    // is caught.
    [Theory]
    [InlineData(null,       "(empty)")]
    [InlineData("",         "(empty)")]
    [InlineData("short",    "short")]
    public void TruncateForLog_SmallOrEmptyInput_PassesThrough(string? raw, string expected)
    {
        Assert.Equal(expected, DocumentGenerateService.TruncateForLog(raw, headChars: 5, tailChars: 5));
    }

    [Fact]
    public void TruncateForLog_LongInput_EmitsHeadAndTailWithElisionMarker()
    {
        var raw = new string('a', 100) + new string('b', 200) + new string('c', 100);
        var result = DocumentGenerateService.TruncateForLog(raw, headChars: 50, tailChars: 50);

        Assert.StartsWith(new string('a', 50), result);
        Assert.EndsWith(new string('c', 50),   result);
        // 400 - 50 - 50 = 300 chars elided.
        Assert.Contains("[300 chars elided]", result);
        // Total log line bounded: head + tail + elision marker — no quadratic blow-up.
        Assert.True(result.Length < raw.Length);
    }

    // Story 3.7 code-review P9 — a Claude response with a wrong-shape
    // `applicationUsers` value (object / string instead of array) falls
    // outside the AC-11 defence-in-depth contract: the missing-key path is
    // explicitly tolerated, but a wrong-shape value still raises AI_ERROR
    // (the safe-coalesce can only normalise NULL or array shapes — there is
    // no semantically-correct fallback for "applicationUsers" returned as a
    // string). Test pins the expected AI_ERROR behaviour so a future
    // tolerant converter is an intentional design move rather than a silent
    // regression of the parser strictness.
    [Theory]
    [InlineData("""{ "applicationUsers": "not-an-array" }""")]
    [InlineData("""{ "applicationUsers": 42 }""")]
    [InlineData("""{ "applicationUsers": { "displayName": "x" } }""")]
    public void ParseAgentJson_WrongShapeApplicationUsersKey_RaisesAiError(string raw)
    {
        var ex = Assert.Throws<GenerationFailureException>(
            () => DocumentGenerateService.ParseAgentJson(raw));
        Assert.Equal(JobFailureCodes.AiError, ex.Code);
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
