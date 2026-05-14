// F-013 — Story 3.5 DocxBuilder tests — section presence + structural validity
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
            },
            Tables        = Array.Empty<TableInfo>(),
            Fields        = new Dictionary<string, IReadOnlyList<FieldInfo>>(),
            Relationships = new Dictionary<string, IReadOnlyList<RelationshipInfo>>(),
        };

        var bytes = DocxBuilder.Build(model);

        Assert.NotEmpty(bytes);
        using var ms = new MemoryStream(bytes, writable: false);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        Assert.NotNull(doc.MainDocumentPart!.Document!.Body);
    }

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
