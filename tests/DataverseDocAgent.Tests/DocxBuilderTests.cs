// F-013 — Story 3.5 DocxBuilder tests — section presence + structural validity
// F-047 — Story 3.6 adds three AC-5 narrative tests + empty-environment guard for
//         the Publisher Prefix Summary sub-section.
using DataverseDocAgent.Api.Documents;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DataverseDocAgent.Tests;

public class DocxBuilderTests
{
    [Fact]
    public void Build_ProducesValidDocxWithAllFourSectionHeadings()
    {
        var model = BuildSampleModel();

        var bytes = DocxBuilder.Build(model);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        // OPC packages always start with the PK ZIP magic — a quick sanity check.
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);

        using var ms = new MemoryStream(bytes, writable: false);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        var body = doc.MainDocumentPart!.Document!.Body!;

        var text = string.Join("\n", body.Descendants<Text>().Select(t => t.Text ?? string.Empty));

        Assert.Contains("1. Executive Summary", text);
        Assert.Contains("2. Custom Tables", text);
        Assert.Contains("3. Field Catalogue", text);
        Assert.Contains("4. Relationship Map", text);
    }

    [Fact]
    public void Build_IncludesEnvironmentNameAndComplexityRating()
    {
        var model = BuildSampleModel();

        var bytes = DocxBuilder.Build(model);

        using var ms = new MemoryStream(bytes, writable: false);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        var text = string.Join("\n",
            doc.MainDocumentPart!.Document!.Body!.Descendants<Text>().Select(t => t.Text ?? string.Empty));

        Assert.Contains("Contoso Sandbox", text);
        Assert.Contains("Medium", text); // complexityRating
        // Key observation rendered
        Assert.Contains("First key observation", text);
    }

    [Fact]
    public void Build_HandlesEmptyEnvironment_WithoutThrowing()
    {
        // Guards against rendering an environment with zero custom tables — common
        // for clean dev/test orgs. AC-6: the document still opens in Word.
        var model = new GeneratedDocumentModel
        {
            Summary = new ExecutiveSummary
            {
                EnvironmentName   = "Empty Org",
                ScanDate          = DateTime.UtcNow,
                ComplexityRating  = "Low",
                TableCount        = 0,
                FieldCount        = 0,
                RelationshipCount = 0,
                KeyObservations   = Array.Empty<string>(),
                PrefixSummary     = EmptyPrefixSummary(),
            },
            Tables        = Array.Empty<TableInfo>(),
            Fields        = new Dictionary<string, IReadOnlyList<FieldInfo>>(),
            Relationships = new Dictionary<string, IReadOnlyList<RelationshipInfo>>(),
        };

        var bytes = DocxBuilder.Build(model);

        Assert.NotEmpty(bytes);
        using var ms = new MemoryStream(bytes, writable: false);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        Assert.NotNull(body);

        // Story 3.6 AC-7 — for an empty environment the section header is omitted.
        var text = string.Join("\n", body.Descendants<Text>().Select(t => t.Text ?? string.Empty));
        Assert.DoesNotContain("Publisher Prefix Summary", text);
    }

    [Fact]
    public void Build_NullAndEmptyKeyObservations_AreFilteredAndDoNotThrow()
    {
        // Story 3.5 code-review P3 — JSON `null` deserialising into a List<string>
        // produces a real null entry; DocxBuilder must filter rather than NRE.
        var model = new GeneratedDocumentModel
        {
            Summary = new ExecutiveSummary
            {
                EnvironmentName   = "Edge Case Org",
                ScanDate          = DateTime.UtcNow,
                ComplexityRating  = "Low",
                TableCount        = 0,
                FieldCount        = 0,
                RelationshipCount = 0,
                KeyObservations   = new string?[] { null, "real observation", "", "   " }!
                    .Cast<string>()
                    .ToArray(),
                PrefixSummary     = EmptyPrefixSummary(),
            },
            Tables        = Array.Empty<TableInfo>(),
            Fields        = new Dictionary<string, IReadOnlyList<FieldInfo>>(),
            Relationships = new Dictionary<string, IReadOnlyList<RelationshipInfo>>(),
        };

        var bytes = DocxBuilder.Build(model);

        Assert.NotEmpty(bytes);
        using var ms = new MemoryStream(bytes, writable: false);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        var text = string.Join("\n",
            doc.MainDocumentPart!.Document!.Body!.Descendants<Text>().Select(t => t.Text ?? string.Empty));
        Assert.Contains("real observation", text);
        // Null/empty/whitespace entries are dropped — no stray "• " bullet.
        Assert.DoesNotContain("• \n",  text);
    }

    // ─── Story 3.6 — AC-5 narrative branches ─────────────────────────────────

    [Fact]
    public void Build_PublisherPrefixSection_SingleClientPrefix_RendersAllClientCustomisationsSentence()
    {
        var summary = new PublisherPrefixSummary
        {
            PrimaryClientPrefix    = "vel",
            MicrosoftPrefixes      = new[]
            {
                new PrefixCount("msdyn", 4),
                new PrefixCount("msft",  1),
            },
            ClientPrefixes         = new[] { new PrefixCount("vel", 7) },
            UnprefixedTables       = Array.Empty<PrefixCount>(),
            NoClientPrefixDetected = false,
        };

        var text = RenderText(BuildModelWithPrefix(summary, tableCount: 12));

        Assert.Contains("Publisher Prefix Summary", text);
        Assert.Contains("All client customisations use the prefix 'vel_'.", text);
        Assert.Contains("Microsoft components use msdyn_, msft_.", text);
        Assert.Contains("No third-party ISV components detected.", text);
        // Breakdown table rows appear AFTER the narrative; anchor the search
        // past the last narrative sentence so the assertion targets the table.
        var afterNarrative = text.IndexOf("No third-party ISV components detected.", StringComparison.Ordinal);
        Assert.True(afterNarrative > 0, "Narrative anchor sentence missing.");
        var msdynIdx = text.IndexOf("msdyn_", afterNarrative, StringComparison.Ordinal);
        var msftIdx  = text.IndexOf("msft_",  afterNarrative, StringComparison.Ordinal);
        var velIdx   = text.IndexOf("vel_",   afterNarrative, StringComparison.Ordinal);
        Assert.True(msdynIdx > 0 && msftIdx > msdynIdx && velIdx > msftIdx,
            $"Expected breakdown order msdyn_ → msft_ → vel_, got positions {msdynIdx}/{msftIdx}/{velIdx}.");
    }

    [Fact]
    public void Build_PublisherPrefixSection_NoClientPrefix_RendersFallbackSentence()
    {
        var summary = new PublisherPrefixSummary
        {
            PrimaryClientPrefix    = null,
            MicrosoftPrefixes      = new[] { new PrefixCount("msdyn", 3) },
            ClientPrefixes         = Array.Empty<PrefixCount>(),
            UnprefixedTables       = Array.Empty<PrefixCount>(),
            NoClientPrefixDetected = true,
        };

        var text = RenderText(BuildModelWithPrefix(summary, tableCount: 3));

        Assert.Contains("Publisher Prefix Summary", text);
        Assert.Contains(
            "No client-defined publisher prefix detected — all custom components use default or Microsoft prefixes.",
            text);
    }

    [Fact]
    public void Build_PublisherPrefixSection_MultipleClientPrefixes_RendersPrimarySentenceAndOrderedTable()
    {
        var summary = new PublisherPrefixSummary
        {
            PrimaryClientPrefix    = "vel",
            MicrosoftPrefixes      = new[] { new PrefixCount("msdyn", 2) },
            ClientPrefixes         = new[]
            {
                new PrefixCount("vel", 5),
                new PrefixCount("acme", 3),
            },
            UnprefixedTables       = new[] { new PrefixCount("(no prefix)", 1) },
            NoClientPrefixDetected = false,
        };

        var text = RenderText(BuildModelWithPrefix(summary, tableCount: 11));

        Assert.Contains(
            "Multiple custom prefixes detected — environment may have multiple development teams or migration history.",
            text);
        Assert.Contains("Primary client prefix: 'vel_' (5 components).", text);
        // Breakdown order: Microsoft → Client (vel desc, acme next) → Unprefixed.
        var msdynIdx = text.IndexOf("msdyn_", StringComparison.Ordinal);
        var velIdx   = text.IndexOf("vel_'", StringComparison.Ordinal); // primary sentence first
        // Find the *table-row* vel_ — search after the primary-sentence mention.
        var velRowIdx  = text.IndexOf("vel_", velIdx + 1, StringComparison.Ordinal);
        var acmeIdx    = text.IndexOf("acme_", StringComparison.Ordinal);
        var noPrefIdx  = text.IndexOf("(no prefix)", StringComparison.Ordinal);
        Assert.True(msdynIdx > 0 && velRowIdx > msdynIdx && acmeIdx > velRowIdx && noPrefIdx > acmeIdx,
            $"Expected msdyn_ → vel_ → acme_ → (no prefix), got positions {msdynIdx}/{velRowIdx}/{acmeIdx}/{noPrefIdx}.");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string RenderText(GeneratedDocumentModel model)
    {
        var bytes = DocxBuilder.Build(model);
        using var ms = new MemoryStream(bytes, writable: false);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        return string.Join("\n",
            doc.MainDocumentPart!.Document!.Body!.Descendants<Text>().Select(t => t.Text ?? string.Empty));
    }

    private static GeneratedDocumentModel BuildModelWithPrefix(PublisherPrefixSummary prefix, int tableCount)
        => new()
        {
            Summary = new ExecutiveSummary
            {
                EnvironmentName   = "Branch Test Org",
                ScanDate          = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc),
                ComplexityRating  = "Medium",
                TableCount        = tableCount,
                FieldCount        = 0,
                RelationshipCount = 0,
                KeyObservations   = Array.Empty<string>(),
                PrefixSummary     = prefix,
            },
            Tables        = Array.Empty<TableInfo>(),
            Fields        = new Dictionary<string, IReadOnlyList<FieldInfo>>(),
            Relationships = new Dictionary<string, IReadOnlyList<RelationshipInfo>>(),
        };

    private static PublisherPrefixSummary EmptyPrefixSummary() => new()
    {
        PrimaryClientPrefix    = null,
        MicrosoftPrefixes      = Array.Empty<PrefixCount>(),
        ClientPrefixes         = Array.Empty<PrefixCount>(),
        UnprefixedTables       = Array.Empty<PrefixCount>(),
        NoClientPrefixDetected = true,
    };

    private static GeneratedDocumentModel BuildSampleModel() => new()
    {
        Summary = new ExecutiveSummary
        {
            EnvironmentName   = "Contoso Sandbox",
            EnvironmentUrl    = "https://contoso.crm.dynamics.com",
            Version           = "9.2.0.0",
            BaseLanguageName  = "English (United States)",
            ScanDate          = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc),
            ComplexityRating  = "Medium",
            TableCount        = 12,
            FieldCount        = 150,
            RelationshipCount = 18,
            KeyObservations   = new[] { "First key observation", "Second key observation" },
            PrefixSummary     = new PublisherPrefixSummary
            {
                PrimaryClientPrefix    = "new",
                MicrosoftPrefixes      = Array.Empty<PrefixCount>(),
                ClientPrefixes         = new[] { new PrefixCount("new", 1) },
                UnprefixedTables       = Array.Empty<PrefixCount>(),
                NoClientPrefixDetected = false,
            },
        },
        Tables = new[]
        {
            new TableInfo
            {
                LogicalName = "new_widget",
                DisplayName = "Widget",
                SchemaName  = "new_Widget",
                SolutionName = "ContosoCore",
                Description  = "Widget description",
                Purpose      = "Tracks widgets used across the warehouse.",
            },
        },
        Fields = new Dictionary<string, IReadOnlyList<FieldInfo>>
        {
            ["new_widget"] = new[]
            {
                new FieldInfo
                {
                    LogicalName   = "new_serialnumber",
                    DisplayName   = "Serial Number",
                    AttributeType = "String",
                    RequiredLevel = "ApplicationRequired",
                    Description   = "Unique serial.",
                },
            },
        },
        Relationships = new Dictionary<string, IReadOnlyList<RelationshipInfo>>
        {
            ["new_widget"] = new[]
            {
                new RelationshipInfo
                {
                    SchemaName       = "new_widget_account",
                    RelationshipType = "OneToMany",
                    RelatedEntity    = "account",
                    CascadeDelete    = "RemoveLink",
                    BusinessMeaning  = "Widgets belong to an account.",
                },
            },
        },
    };
}
