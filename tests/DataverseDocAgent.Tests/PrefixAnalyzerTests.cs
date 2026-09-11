using DataverseDocAgent.Api.Documents;
using DataverseDocAgent.Api.Features.DocumentGenerate;
namespace DataverseDocAgent.Tests;

public class PrefixAnalyzerTests
{
    [Theory]
    [InlineData("cr_table")]
    [InlineData("cr3a7_table")]
    [InlineData("crm_widget")]
    [InlineData("crystal_thing")]
    [InlineData("msdyn_table")]
    [InlineData("adx_setting")]
    public void Analyze_PrefixDoesNotProveMicrosoftOwnership(string logicalName)
    {
        var result = PrefixAnalyzer.Analyze(new[] { new TableInfo { LogicalName = logicalName } });
        Assert.Empty(result.MicrosoftPrefixes);
        Assert.Equal(logicalName.Split('_')[0], Assert.Single(result.ClientPrefixes).Prefix);
    }
    [Fact]
    public void Analyze_CountsAndSortsObservedPrefixesDeterministically()
    {
        var result = PrefixAnalyzer.Analyze(new[] { "z_a", "alpha_a", "ALPHA_b", "z_b", "alone", "_bad" }
            .Select(n => new TableInfo { LogicalName = n }).ToArray());
        Assert.Equal(new[] { "alpha", "z" }, result.ClientPrefixes.Select(p => p.Prefix));
        Assert.All(result.ClientPrefixes, p => Assert.Equal(2, p.ComponentCount));
        Assert.Equal(2, Assert.Single(result.UnprefixedTables).ComponentCount);
    }
    [Fact]
    public void Analyze_EmptyAndMalformedNamesDoNotInventPrefixes()
    {
        var result = PrefixAnalyzer.Analyze(new TableInfo[] { null!, new() { LogicalName = " " } });
        Assert.Empty(result.MicrosoftPrefixes);
        Assert.Empty(result.ClientPrefixes);
        Assert.Empty(result.UnprefixedTables);
    }
}
