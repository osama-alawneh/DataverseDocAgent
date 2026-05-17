// F-011 — Story 3.5 deterministic complexity rating
using DataverseDocAgent.Api.Features.DocumentGenerate;

namespace DataverseDocAgent.Tests;

public class ComplexityRaterTests
{
    [Theory]
    [InlineData(0, 0, 0,    "Low")]
    [InlineData(9, 99, 0,   "Low")]
    [InlineData(10, 0, 0,   "Medium")]   // tableCount >= 10
    [InlineData(0, 100, 0,  "Medium")]   // fieldCount >= 100
    [InlineData(50, 500, 0, "Medium")]   // boundary — both at the upper Medium edge
    [InlineData(51, 0, 0,   "High")]     // tableCount > 50
    [InlineData(0, 501, 0,  "High")]     // fieldCount > 500
    [InlineData(100, 1000, 50, "High")]
    public void Rate_DeterministicAcrossBoundary(int tables, int fields, int rels, string expected)
    {
        Assert.Equal(expected, ComplexityRater.Rate(tables, fields, rels));
    }

    [Fact]
    public void Rate_NegativeCountsClampToZero()
    {
        // Negative counts indicate caller bugs — must not produce a misleading rating.
        Assert.Equal("Low", ComplexityRater.Rate(-5, -10, -1));
    }
}
