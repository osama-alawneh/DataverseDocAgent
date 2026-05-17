// F-047 — Story 3.6 PrefixAnalyzer unit tests (AC-8 coverage)
using DataverseDocAgent.Api.Documents;
using DataverseDocAgent.Api.Features.DocumentGenerate;

namespace DataverseDocAgent.Tests;

public class PrefixAnalyzerTests
{
    [Fact]
    public void Analyze_SplitsOnFirstUnderscoreEvenWithMultipleSegments()
    {
        // AC-8 — logical name with multiple underscores must yield only the
        // segment before the FIRST underscore. `new_my_widget` → "new".
        var tables = new[]
        {
            Table("new_my_widget"),
            Table("new_account"),
        };

        var summary = PrefixAnalyzer.Analyze(tables);

        Assert.Single(summary.ClientPrefixes);
        Assert.Equal("new", summary.ClientPrefixes[0].Prefix);
        Assert.Equal(2,     summary.ClientPrefixes[0].ComponentCount);
    }

    [Theory]
    [InlineData("msdyn_thing",   true)]
    [InlineData("msft_widget",   true)]
    [InlineData("adx_setting",   true)]
    [InlineData("cr_legacy",     true)]    // bare cr (AC-2 exact set)
    [InlineData("cr3a7_table",   true)]    // Power Apps default-environment auto-prefix
    [InlineData("crf2b_table",   true)]
    [InlineData("crxyz_table",   true)]
    // Story 3.6 code-review P8 — spec-accepted over-match: any cr* prefix is
    // bucketed Microsoft. Pin this with a negative case to prevent a future
    // regex tightening from silently flipping classification. The risk is
    // documented in deferred-work.md.
    [InlineData("crm_widget",    true)]    // intentional over-match: real ISV with cr* prefix → Microsoft.
    [InlineData("crystal_thing", true)]
    [InlineData("vel_account",   false)]
    [InlineData("new_widget",    false)]
    [InlineData("doc_form",      false)]
    public void Analyze_BucketsMicrosoftPrefixes(string logicalName, bool expectedMicrosoft)
    {
        var summary = PrefixAnalyzer.Analyze(new[] { Table(logicalName) });

        if (expectedMicrosoft)
        {
            Assert.Single(summary.MicrosoftPrefixes);
            Assert.Empty(summary.ClientPrefixes);
            Assert.True(summary.NoClientPrefixDetected);
        }
        else
        {
            Assert.Empty(summary.MicrosoftPrefixes);
            Assert.Single(summary.ClientPrefixes);
            Assert.False(summary.NoClientPrefixDetected);
        }
    }

    [Fact]
    public void Analyze_TieBreakOnAlphabeticalLowercasePrefix()
    {
        // Two client prefixes with the same count — primary is alphabetically
        // first on the lowercase prefix string (AC-3, deterministic discipline).
        var tables = new[]
        {
            Table("zeta_a"), Table("zeta_b"), Table("zeta_c"),
            Table("alpha_a"), Table("alpha_b"), Table("alpha_c"),
        };

        var summary = PrefixAnalyzer.Analyze(tables);

        Assert.Equal("alpha", summary.PrimaryClientPrefix);
        // ClientPrefixes ordered by count desc, ties by ordinal ascending.
        Assert.Equal(new[] { "alpha", "zeta" },
            summary.ClientPrefixes.Select(p => p.Prefix).ToArray());
    }

    [Fact]
    public void Analyze_EmptyClientBucket_SetsNoClientPrefixFlag()
    {
        var tables = new[]
        {
            Table("msdyn_thing"),
            Table("cr3a7_widget"),
        };

        var summary = PrefixAnalyzer.Analyze(tables);

        Assert.Null(summary.PrimaryClientPrefix);
        Assert.True(summary.NoClientPrefixDetected);
        Assert.Empty(summary.ClientPrefixes);
        Assert.Equal(2, summary.MicrosoftPrefixes.Sum(p => p.ComponentCount));
    }

    [Fact]
    public void Analyze_UnprefixedBucket_RoutesNamesWithoutUnderscore()
    {
        // Defensive: list_custom_tables filters on IsCustomEntity=true so an
        // unprefixed custom table is an artefact. Still bucket it cleanly
        // rather than emit an empty-string prefix.
        var tables = new[]
        {
            Table("standalonetable"),
            Table("vel_account"),
        };

        var summary = PrefixAnalyzer.Analyze(tables);

        Assert.Single(summary.UnprefixedTables);
        Assert.Equal("(no prefix)", summary.UnprefixedTables[0].Prefix);
        Assert.Equal(1, summary.UnprefixedTables[0].ComponentCount);
        Assert.Equal("vel", summary.PrimaryClientPrefix);
    }

    [Fact]
    public void Analyze_LeadingUnderscoreLogicalName_IsBucketedAsUnprefixed()
    {
        // Story 3.6 code-review P7 — `_widget` has underscoreIndex == 0,
        // which the analyzer treats as "no recognisable prefix" → Unprefixed.
        // Pin this so a future maintainer does not silently flip the rule.
        var summary = PrefixAnalyzer.Analyze(new[] { Table("_widget") });

        Assert.Single(summary.UnprefixedTables);
        Assert.Equal(PrefixAnalyzer.UnprefixedLabel, summary.UnprefixedTables[0].Prefix);
        Assert.Empty(summary.MicrosoftPrefixes);
        Assert.Empty(summary.ClientPrefixes);
    }

    [Fact]
    public void Analyze_NullTableEntry_IsSilentlySkipped()
    {
        // Story 3.6 code-review P1 — JSON `[null, {...}]` produces a real null
        // element in `parsed.Tables`. Service-level filter also runs, but the
        // analyzer must defend independently so direct callers cannot NRE.
        var tables = new TableInfo?[] { null, new TableInfo { LogicalName = "vel_account" } };
        var summary = PrefixAnalyzer.Analyze(tables!);

        Assert.Equal("vel", summary.PrimaryClientPrefix);
        Assert.Equal(1, summary.ClientPrefixes[0].ComponentCount);
    }

    [Fact]
    public void Analyze_EmptyInput_ReturnsAllEmptyBucketsAndNoClientFlag()
    {
        var summary = PrefixAnalyzer.Analyze(Array.Empty<TableInfo>());

        Assert.Null(summary.PrimaryClientPrefix);
        Assert.True(summary.NoClientPrefixDetected);
        Assert.Empty(summary.MicrosoftPrefixes);
        Assert.Empty(summary.ClientPrefixes);
        Assert.Empty(summary.UnprefixedTables);
    }

    [Fact]
    public void Analyze_MicrosoftAndClientCountsAreInDescendingOrderWithStableTieBreak()
    {
        // 3× msdyn_*, 1× cr_*, 2× vel_*, 2× acme_*.
        var tables = new[]
        {
            Table("msdyn_a"), Table("msdyn_b"), Table("msdyn_c"),
            Table("cr_x"),
            Table("vel_a"), Table("vel_b"),
            Table("acme_a"), Table("acme_b"),
        };

        var summary = PrefixAnalyzer.Analyze(tables);

        Assert.Equal(new[] { "msdyn", "cr" },
            summary.MicrosoftPrefixes.Select(p => p.Prefix).ToArray());
        // 2× vs 2× tie → alphabetical asc on prefix: acme before vel.
        Assert.Equal(new[] { "acme", "vel" },
            summary.ClientPrefixes.Select(p => p.Prefix).ToArray());
        Assert.Equal("acme", summary.PrimaryClientPrefix);
    }

    private static TableInfo Table(string logicalName) => new()
    {
        LogicalName = logicalName,
    };
}
