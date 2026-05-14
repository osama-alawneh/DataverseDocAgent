// F-001, F-013, NFR-014 — Story 3.5 DocumentGenerateService pipeline tests
using System.Text.Json;
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
}
