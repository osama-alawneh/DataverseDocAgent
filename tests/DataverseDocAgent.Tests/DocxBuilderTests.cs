// F-013 — Story 3.5 DocxBuilder tests — section presence + structural validity
// F-047 — Story 3.6 adds three AC-5 narrative tests + empty-environment guard for
//         the Publisher Prefix Summary sub-section.
// F-055 — Story 3.7 adds Section 5 (Application Users) populated + empty tests.
using DataverseDocAgent.Api.Documents;
using DataverseDocAgent.Api.Features.DocumentGenerate;
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
            Tables           = Array.Empty<TableInfo>(),
            Fields           = new Dictionary<string, IReadOnlyList<FieldInfo>>(),
            Relationships    = new Dictionary<string, IReadOnlyList<RelationshipInfo>>(),
            ApplicationUsers = Array.Empty<ApplicationUserInfo>(),
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
        // Story 3.7 AC-10 — Section 5 is suppressed only when both tables AND
        // application users are empty. For this empty-environment fixture the
        // section header must NOT render.
        Assert.DoesNotContain("5. Application Users (Integration Signals)", text);
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
            Tables           = Array.Empty<TableInfo>(),
            Fields           = new Dictionary<string, IReadOnlyList<FieldInfo>>(),
            Relationships    = new Dictionary<string, IReadOnlyList<RelationshipInfo>>(),
            ApplicationUsers = Array.Empty<ApplicationUserInfo>(),
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
            UnprefixedTables       = new[] { new PrefixCount(PrefixAnalyzer.UnprefixedLabel, 1) },
            NoClientPrefixDetected = false,
        };

        var text = RenderText(BuildModelWithPrefix(summary, tableCount: 11));

        Assert.Contains(
            "Multiple custom prefixes detected — environment may have multiple development teams or migration history.",
            text);
        Assert.Contains("Primary client prefix: 'vel_' (5 components).", text);
        // Story 3.6 code-review P10 — variant 3 must NOT emit the variant-1
        // "Microsoft components use …" sentence. Lock the prose absence.
        Assert.DoesNotContain("Microsoft components use", text);
        // Story 3.6 code-review P14 — anchor breakdown-table assertion on a
        // stable phrase ("See full breakdown below.") instead of the
        // primary-sentence apostrophe shape, which is brittle to prose tweaks.
        var afterNarrative = text.IndexOf("See full breakdown below.", StringComparison.Ordinal);
        Assert.True(afterNarrative > 0, "Narrative anchor sentence missing.");
        var msdynIdx  = text.IndexOf("msdyn_", afterNarrative, StringComparison.Ordinal);
        var velRowIdx = text.IndexOf("vel_",   afterNarrative, StringComparison.Ordinal);
        var acmeIdx   = text.IndexOf("acme_",  afterNarrative, StringComparison.Ordinal);
        var noPrefIdx = text.IndexOf(PrefixAnalyzer.UnprefixedLabel, afterNarrative, StringComparison.Ordinal);
        Assert.True(msdynIdx > 0 && velRowIdx > msdynIdx && acmeIdx > velRowIdx && noPrefIdx > acmeIdx,
            $"Expected msdyn_ → vel_ → acme_ → (no prefix), got positions {msdynIdx}/{velRowIdx}/{acmeIdx}/{noPrefIdx}.");
    }

    [Fact]
    public void Build_PublisherPrefixSection_SingleClient_NoMicrosoft_DropsMicrosoftSentence()
    {
        // Story 3.6 code-review P9 — the "Microsoft components use …" sentence
        // is gated on a non-empty Microsoft bucket. Lock the gating so a future
        // refactor cannot emit stray prose like "Microsoft components use .".
        var summary = new PublisherPrefixSummary
        {
            PrimaryClientPrefix    = "vel",
            MicrosoftPrefixes      = Array.Empty<PrefixCount>(),
            ClientPrefixes         = new[] { new PrefixCount("vel", 4) },
            UnprefixedTables       = Array.Empty<PrefixCount>(),
            NoClientPrefixDetected = false,
        };

        var text = RenderText(BuildModelWithPrefix(summary, tableCount: 4));

        Assert.Contains("All client customisations use the prefix 'vel_'.", text);
        Assert.DoesNotContain("Microsoft components use", text);
        Assert.Contains("No third-party ISV components detected.", text);
    }

    [Fact]
    public void Build_PublisherPrefixSection_AllBucketsEmptyWithNonZeroTableCount_OmitsHeader()
    {
        // Story 3.6 code-review P3 — defensive: TableCount > 0 but every
        // PrefixAnalyzer bucket empty (Claude reported tables but the array
        // came back empty / whitespace-only LogicalName). Section is omitted
        // entirely to avoid a confusing header + lonely sentence.
        var summary = new PublisherPrefixSummary
        {
            PrimaryClientPrefix    = null,
            MicrosoftPrefixes      = Array.Empty<PrefixCount>(),
            ClientPrefixes         = Array.Empty<PrefixCount>(),
            UnprefixedTables       = Array.Empty<PrefixCount>(),
            NoClientPrefixDetected = true,
        };

        var text = RenderText(BuildModelWithPrefix(summary, tableCount: 5));

        Assert.DoesNotContain("Publisher Prefix Summary", text);
    }

    [Fact]
    public void Build_PublisherPrefixSection_PrimaryWithOneComponent_UsesSingularUnit()
    {
        // Story 3.6 code-review P4 — pluralisation: "(1 component)" not
        // "(1 components)" when the primary client prefix has count 1.
        var summary = new PublisherPrefixSummary
        {
            PrimaryClientPrefix    = "acme",
            MicrosoftPrefixes      = Array.Empty<PrefixCount>(),
            ClientPrefixes         = new[]
            {
                new PrefixCount("acme", 1),
                new PrefixCount("zeta", 1),
            },
            UnprefixedTables       = Array.Empty<PrefixCount>(),
            NoClientPrefixDetected = false,
        };

        var text = RenderText(BuildModelWithPrefix(summary, tableCount: 2));

        Assert.Contains("Primary client prefix: 'acme_' (1 component).", text);
        Assert.DoesNotContain("(1 components)", text);
    }

    // ─── Story 3.7 — Section 5 Application Users ─────────────────────────────

    [Fact]
    public void Build_ApplicationUsersSection_Populated_RendersHeadingProseAndTable()
    {
        var users = new[]
        {
            new ApplicationUserInfo
            {
                DisplayName   = "Integration Sync",
                ApplicationId = "11111111-1111-1111-1111-111111111111",
                Email         = "sync@contoso.onmicrosoft.com",
                Roles         = new[] { "Reader", "Custom Writer" },
            },
            new ApplicationUserInfo
            {
                DisplayName   = "Logic Apps Connector",
                ApplicationId = "22222222-2222-2222-2222-222222222222",
                Email         = null,
                Roles         = new[]
                {
                    GetApplicationUsersTool_RoleLookupSentinel,
                },
            },
        };
        var model = BuildModelWithApplicationUsers(users, tableCount: 4);

        var text = RenderText(model);

        // AC-9 — heading + literal FR-050 prose paragraph.
        Assert.Contains("5. Application Users (Integration Signals)", text);
        Assert.Contains(
            "Application users are typically used by external integrations. The following "
            + "application users are registered and may be writing to tables in this environment.",
            text);
        // AC-9 — three-column header.
        Assert.Contains("Display Name", text);
        Assert.Contains("Application ID", text);
        Assert.Contains("Roles", text);
        // Per-user rows.
        Assert.Contains("Integration Sync",                                text);
        Assert.Contains("11111111-1111-1111-1111-111111111111",            text);
        Assert.Contains("Reader, Custom Writer",                           text);
        Assert.Contains("Logic Apps Connector",                            text);
        Assert.Contains("22222222-2222-2222-2222-222222222222",            text);
        // Sentinel preserved verbatim per AC-4 / AC-9.
        Assert.Contains(GetApplicationUsersTool_RoleLookupSentinel,        text);
        // Empty list fallback prose must NOT be emitted when users exist.
        Assert.DoesNotContain("No application users registered in this environment.", text);
    }

    [Fact]
    public void Build_ApplicationUsersSection_EmptyList_PopulatedEnvironment_RendersHeadingAndFallbackProse()
    {
        // AC-10 — Section 5 must still render its heading + literal prose +
        // the "No application users registered" sentence whenever the document
        // is otherwise non-empty (Tables.Count > 0). The breakdown table is
        // omitted in this branch.
        var model = BuildModelWithApplicationUsers(
            users: Array.Empty<ApplicationUserInfo>(),
            tableCount: 3);

        var text = RenderText(model);

        Assert.Contains("5. Application Users (Integration Signals)",       text);
        Assert.Contains(
            "Application users are typically used by external integrations. The following "
            + "application users are registered and may be writing to tables in this environment.",
            text);
        Assert.Contains("No application users registered in this environment.", text);
        // No table headers when the user list is empty.
        Assert.DoesNotContain("Display Name", text);
        Assert.DoesNotContain("Application ID", text);
    }

    [Fact]
    public void Build_ApplicationUsersSection_EmptyRolesList_RendersNoRolesAssignedLiteral()
    {
        // AC-9 final clause — an empty roles array must render the literal
        // "(no roles assigned)" so the cell is never blank. Distinct from the
        // AC-4 sentinel which means the lookup itself failed.
        var users = new[]
        {
            new ApplicationUserInfo
            {
                DisplayName   = "Bare User",
                ApplicationId = "33333333-3333-3333-3333-333333333333",
                Email         = null,
                Roles         = Array.Empty<string>(),
            },
        };

        var text = RenderText(BuildModelWithApplicationUsers(users, tableCount: 1));

        Assert.Contains("Bare User",          text);
        Assert.Contains("(no roles assigned)", text);
        Assert.DoesNotContain(GetApplicationUsersTool_RoleLookupSentinel, text);
    }

    [Fact]
    public void Build_ApplicationUsersSection_FullyEmptyEnvironment_OmitsSectionHeader()
    {
        // AC-10 — when Tables.Count == 0 AND ApplicationUsers.Count == 0 the
        // section heading must NOT render. This is the only branch where the
        // header is suppressed.
        var model = BuildModelWithApplicationUsers(
            users: Array.Empty<ApplicationUserInfo>(),
            tableCount: 0);

        var text = RenderText(model);

        Assert.DoesNotContain("5. Application Users (Integration Signals)", text);
        Assert.DoesNotContain(
            "Application users are typically used by external integrations.",
            text);
    }

    private const string GetApplicationUsersTool_RoleLookupSentinel = "(role lookup unavailable)";

    private static GeneratedDocumentModel BuildModelWithApplicationUsers(
        IReadOnlyList<ApplicationUserInfo> users,
        int tableCount)
    {
        // Section-5 visibility (AC-10) is gated on the model's Tables
        // collection size, not the summary's TableCount counter — populate
        // the collection so the fixture exercises the "tables present" branch.
        var tables = Enumerable.Range(0, tableCount)
            .Select(i => new TableInfo { LogicalName = $"fake_table_{i}" })
            .ToArray();
        return new GeneratedDocumentModel
        {
            Summary = new ExecutiveSummary
            {
                EnvironmentName   = "App Users Test Org",
                ScanDate          = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc),
                ComplexityRating  = "Medium",
                TableCount        = tableCount,
                FieldCount        = 0,
                RelationshipCount = 0,
                KeyObservations   = Array.Empty<string>(),
                PrefixSummary     = EmptyPrefixSummary(),
            },
            Tables           = tables,
            Fields           = new Dictionary<string, IReadOnlyList<FieldInfo>>(),
            Relationships    = new Dictionary<string, IReadOnlyList<RelationshipInfo>>(),
            ApplicationUsers = users,
        };
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
            Tables           = Array.Empty<TableInfo>(),
            Fields           = new Dictionary<string, IReadOnlyList<FieldInfo>>(),
            Relationships    = new Dictionary<string, IReadOnlyList<RelationshipInfo>>(),
            ApplicationUsers = Array.Empty<ApplicationUserInfo>(),
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
        ApplicationUsers = Array.Empty<ApplicationUserInfo>(),
    };
}
