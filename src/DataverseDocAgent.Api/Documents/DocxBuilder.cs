// F-013 — FR-013 — DocxBuilder produces the Mode 1 .docx (Story 3.5, PRD §8.1)
// F-047 — FR-042 — Publisher Prefix Summary sub-section (Story 3.6)
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DataverseDocAgent.Api.Documents;

/// <summary>
/// Builds the Mode 1 .docx body from a <see cref="GeneratedDocumentModel"/>.
/// Sections (per PRD §8.1): (1) Executive Summary, (2) Custom Tables,
/// (3) Field Catalogue, (4) Relationship Map. Output is a fully-formed
/// OpenXML package — the caller receives raw bytes, not a stream, because
/// streams ownership across the storage layer is fragile.
/// </summary>
public static class DocxBuilder
{
    private const string AuthorName   = "DataverseDocAgent";
    private const string TitlePrefix  = "Environment Documentation";
    private const string FontName     = "Calibri";

    public static byte[] Build(GeneratedDocumentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // MemoryStream owns the buffer until the package is fully written. Returning
        // `ToArray()` after Save() decouples the caller from stream lifecycle —
        // IDocumentStore would otherwise see a closed stream once `using` exits.
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body!;

            ApplyCorePackageMetadata(doc, model.Summary.EnvironmentName);

            AppendTitle(body, $"{TitlePrefix} — {model.Summary.EnvironmentName ?? "Unknown Environment"}");
            AppendExecutiveSummary(body, model.Summary);
            AppendCustomTablesSection(body, model.Tables);
            AppendFieldCatalogueSection(body, model.Tables, model.Fields);
            AppendRelationshipMapSection(body, model.Tables, model.Relationships);

            mainPart.Document.Save();
        }

        return ms.ToArray();
    }

    // ── Sections ──────────────────────────────────────────────────────────────

    private static void AppendExecutiveSummary(Body body, ExecutiveSummary summary)
    {
        AppendHeading(body, "1. Executive Summary", level: 1);

        AppendKeyValueParagraph(body, "Environment",      summary.EnvironmentName);
        AppendKeyValueParagraph(body, "Environment URL",  summary.EnvironmentUrl);
        AppendKeyValueParagraph(body, "Version",          summary.Version);
        AppendKeyValueParagraph(body, "Base language",    summary.BaseLanguageName);
        AppendKeyValueParagraph(body, "Scan date (UTC)",  summary.ScanDate.ToString("u"));
        AppendKeyValueParagraph(body, "Complexity",       summary.ComplexityRating, bold: true);

        AppendHeading(body, "Counts", level: 2);
        var counts = BuildTable(
            new[] { "Metric", "Value" },
            new[]
            {
                new[] { "Custom tables",   summary.TableCount.ToString() },
                new[] { "Custom fields",   summary.FieldCount.ToString() },
                new[] { "Relationships",   summary.RelationshipCount.ToString() },
            });
        body.AppendChild(counts);

        // Story 3.6 — F-047 / FR-042. Renders between the counts table and
        // the key-observation bullets per AC-5. Omitted entirely for an
        // empty environment (AC-7).
        AppendPublisherPrefixSection(body, summary.PrefixSummary, summary.TableCount);

        AppendHeading(body, "Key observations", level: 2);
        // Story 3.5 code-review P3 — defensively filter null entries; JSON
        // `null` inside an array of strings deserialises to a real null and
        // would NRE the OpenXml Text element.
        var observations = summary.KeyObservations
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .ToList();
        if (observations.Count == 0)
        {
            AppendParagraph(body, "(No key observations were produced by the agent.)", italic: true);
        }
        else
        {
            foreach (var observation in observations)
            {
                AppendBulletParagraph(body, observation);
            }
        }
    }

    // ── Story 3.6 — Publisher Prefix Summary sub-section ─────────────────────

    private static void AppendPublisherPrefixSection(
        Body body,
        PublisherPrefixSummary prefixSummary,
        int tableCount)
    {
        // AC-7 — for an empty environment we render nothing (not even the
        // heading), so the Section 1 layout matches the Story 3.5 baseline.
        if (tableCount == 0) return;

        // Story 3.6 code-review P3 — TableCount can be >0 while all three
        // buckets are empty (Claude reports tables but `parsed.Tables` is empty
        // or every entry has a whitespace LogicalName). Without this guard the
        // section emits a header + the "no client prefix" sentence and no
        // table, which is visually confusing. Skip the section.
        var totalBuckets =
            prefixSummary.MicrosoftPrefixes.Count +
            prefixSummary.ClientPrefixes.Count +
            prefixSummary.UnprefixedTables.Count;
        if (totalBuckets == 0) return;

        AppendHeading(body, "Publisher Prefix Summary", level: 2);
        AppendParagraph(body, BuildPrefixNarrative(prefixSummary));

        // AC-6 — breakdown table: Microsoft (desc count) → Client/ISV (desc
        // count) → Unprefixed. Trailing underscore on bucketed prefixes,
        // literal `PrefixAnalyzer.UnprefixedLabel` for the Unprefixed row.
        var rows = new List<string[]>(totalBuckets);

        foreach (var row in prefixSummary.MicrosoftPrefixes)
        {
            rows.Add(new[] { row.Prefix + "_", row.ComponentCount.ToString() });
        }
        foreach (var row in prefixSummary.ClientPrefixes)
        {
            rows.Add(new[] { row.Prefix + "_", row.ComponentCount.ToString() });
        }
        foreach (var row in prefixSummary.UnprefixedTables)
        {
            // Already carries the literal label from PrefixAnalyzer — do not
            // append "_" or the row would read "(no prefix)_".
            rows.Add(new[] { row.Prefix, row.ComponentCount.ToString() });
        }

        body.AppendChild(BuildTable(
            new[] { "Prefix", "Component count" },
            rows.ToArray()));
    }

    private static string BuildPrefixNarrative(PublisherPrefixSummary p)
    {
        // AC-5 branches are deterministic on the analyzer output shape so the
        // sub-section prose is reproducible across runs (FR-042).
        //
        // Note: AC-5 variant 1 carries a pipe-alternation second form
        // ("n additional non-Microsoft prefixes detected — see breakdown below.")
        // whose trigger would be "≥2 prefixes in the Client/ISV bucket". That
        // condition routes into variant 3 instead, so the second form is
        // unreachable BY DESIGN — variant 1 always emits the
        // "No third-party ISV components detected." sentence. Story 3.6
        // code-review P6 — documented inline so a future maintainer does not
        // mistake the choice for a missing branch.
        if (p.ClientPrefixes.Count == 0)
        {
            return "No client-defined publisher prefix detected — all custom components use default or Microsoft prefixes.";
        }

        if (p.ClientPrefixes.Count == 1)
        {
            var primary = p.ClientPrefixes[0].Prefix;
            var sb = new StringBuilder();
            sb.Append("All client customisations use the prefix '").Append(primary).Append("_'.");
            if (p.MicrosoftPrefixes.Count > 0)
            {
                var msList = string.Join(", ", p.MicrosoftPrefixes.Select(m => m.Prefix + "_"));
                sb.Append(" Microsoft components use ").Append(msList).Append('.');
            }
            sb.Append(" No third-party ISV components detected.");
            return sb.ToString();
        }

        // Story 3.6 code-review P4 — singular/plural agreement: a primary with
        // ComponentCount == 1 must read "1 component", not "1 components".
        var top  = p.ClientPrefixes[0];
        var unit = top.ComponentCount == 1 ? "component" : "components";
        return
            "Multiple custom prefixes detected — environment may have multiple development teams or migration history. " +
            $"Primary client prefix: '{top.Prefix}_' ({top.ComponentCount} {unit}). See full breakdown below.";
    }

    private static void AppendCustomTablesSection(Body body, IReadOnlyList<TableInfo> tables)
    {
        AppendHeading(body, "2. Custom Tables", level: 1);
        if (tables.Count == 0)
        {
            AppendParagraph(body, "No custom tables were discovered in this environment.", italic: true);
            return;
        }

        foreach (var table in tables)
        {
            AppendHeading(body, table.DisplayName ?? table.LogicalName, level: 2);
            AppendKeyValueParagraph(body, "Logical name", table.LogicalName);
            AppendKeyValueParagraph(body, "Schema name",  table.SchemaName);
            AppendKeyValueParagraph(body, "Solution",     table.SolutionName);
            if (!string.IsNullOrWhiteSpace(table.Description))
            {
                AppendKeyValueParagraph(body, "Description", table.Description);
            }
            if (!string.IsNullOrWhiteSpace(table.Purpose))
            {
                AppendParagraph(body, table.Purpose!);
            }
        }
    }

    private static void AppendFieldCatalogueSection(
        Body body,
        IReadOnlyList<TableInfo> tables,
        IReadOnlyDictionary<string, IReadOnlyList<FieldInfo>> fieldsByTable)
    {
        AppendHeading(body, "3. Field Catalogue", level: 1);
        if (tables.Count == 0)
        {
            AppendParagraph(body, "No fields to catalogue (no custom tables).", italic: true);
            return;
        }

        foreach (var table in tables)
        {
            AppendHeading(body, table.DisplayName ?? table.LogicalName, level: 2);
            if (!fieldsByTable.TryGetValue(table.LogicalName, out var fields) || fields.Count == 0)
            {
                AppendParagraph(body, "No custom fields on this table.", italic: true);
                continue;
            }

            var rows = fields.Select(f => new[]
            {
                f.DisplayName ?? string.Empty,
                f.LogicalName,
                f.AttributeType ?? string.Empty,
                f.RequiredLevel ?? string.Empty,
                f.Description ?? string.Empty,
            }).ToArray();

            body.AppendChild(BuildTable(
                new[] { "Field Name", "Logical Name", "Type", "Required", "Description" },
                rows));
        }
    }

    private static void AppendRelationshipMapSection(
        Body body,
        IReadOnlyList<TableInfo> tables,
        IReadOnlyDictionary<string, IReadOnlyList<RelationshipInfo>> relsByTable)
    {
        AppendHeading(body, "4. Relationship Map", level: 1);
        if (tables.Count == 0)
        {
            AppendParagraph(body, "No relationships to map (no custom tables).", italic: true);
            return;
        }

        foreach (var table in tables)
        {
            AppendHeading(body, table.DisplayName ?? table.LogicalName, level: 2);
            if (!relsByTable.TryGetValue(table.LogicalName, out var rels) || rels.Count == 0)
            {
                AppendParagraph(body, "No custom relationships on this table.", italic: true);
                continue;
            }

            var rows = rels.Select(r => new[]
            {
                r.SchemaName ?? string.Empty,
                r.RelationshipType ?? string.Empty,
                r.RelatedEntity ?? string.Empty,
                r.CascadeDelete ?? string.Empty,
                r.BusinessMeaning ?? string.Empty,
            }).ToArray();

            body.AppendChild(BuildTable(
                new[] { "Relationship", "Type", "Related Table", "Cascade Delete", "Business Meaning" },
                rows));
        }
    }

    // ── Core package metadata ────────────────────────────────────────────────

    private static void ApplyCorePackageMetadata(WordprocessingDocument doc, string? environmentName)
    {
        // Core file properties are part of the OPC package, not the WordprocessingML
        // body. Word surfaces them in File → Info; setting them keeps the document
        // self-identifying when copied or attached.
        var corePart = doc.AddCoreFilePropertiesPart();
        var title    = $"{TitlePrefix} — {environmentName ?? "Unknown Environment"}";

        // Minimal Dublin-Core / DC-Terms doc — no XML namespaces juggling required
        // beyond the canonical OPC core-properties schema.
        var xml =
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <cp:coreProperties
                xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                xmlns:dc="http://purl.org/dc/elements/1.1/"
                xmlns:dcterms="http://purl.org/dc/terms/"
                xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <dc:title>{System.Security.SecurityElement.Escape(title)}</dc:title>
              <dc:creator>{AuthorName}</dc:creator>
              <cp:lastModifiedBy>{AuthorName}</cp:lastModifiedBy>
              <dcterms:created xsi:type="dcterms:W3CDTF">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</dcterms:created>
              <dcterms:modified xsi:type="dcterms:W3CDTF">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</dcterms:modified>
            </cp:coreProperties>
            """;

        using var stream = corePart.GetStream(FileMode.Create);
        using var writer = new StreamWriter(stream);
        writer.Write(xml);
    }

    // ── Primitive builders ────────────────────────────────────────────────────

    private static void AppendTitle(Body body, string text)
    {
        var run = MakeRun(text, bold: true, sizeHalfPoints: 36);
        body.AppendChild(new Paragraph(run));
    }

    private static void AppendHeading(Body body, string text, int level)
    {
        // Heading sizes step down with level: 28pt → 22pt → 18pt
        var size = level switch
        {
            1 => 28,
            2 => 22,
            _ => 18,
        };
        var run = MakeRun(text, bold: true, sizeHalfPoints: size * 2);
        body.AppendChild(new Paragraph(run));
    }

    private static void AppendParagraph(Body body, string text, bool italic = false)
    {
        body.AppendChild(new Paragraph(MakeRun(text, italic: italic)));
    }

    private static void AppendBulletParagraph(Body body, string text)
    {
        // Real numbering would require a numbering part — for the MVP, a leading
        // bullet character is visually equivalent and avoids the OPC numbering
        // boilerplate. Phase 3 can upgrade to proper Word lists.
        body.AppendChild(new Paragraph(MakeRun("• " + text)));
    }

    private static void AppendKeyValueParagraph(Body body, string key, string? value, bool bold = false)
    {
        var safeValue = string.IsNullOrWhiteSpace(value) ? "(not available)" : value!;
        var paragraph = new Paragraph(
            MakeRun(key + ": ", bold: true),
            MakeRun(safeValue, bold: bold));
        body.AppendChild(paragraph);
    }

    private static Run MakeRun(string? text, bool bold = false, bool italic = false, int? sizeHalfPoints = null)
    {
        // Story 3.5 code-review P3 — `Text(null)` throws in some OpenXml builds; coerce.
        var safeText = text ?? string.Empty;
        var runProps = new RunProperties();
        runProps.AppendChild(new RunFonts { Ascii = FontName, HighAnsi = FontName });
        if (bold)   runProps.AppendChild(new Bold());
        if (italic) runProps.AppendChild(new Italic());
        if (sizeHalfPoints.HasValue)
            runProps.AppendChild(new FontSize { Val = sizeHalfPoints.Value.ToString() });

        // Preserve internal whitespace so multi-space alignment in observations is not collapsed.
        var run = new Run(runProps,
            new Text(safeText) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    private static Table BuildTable(string[] headers, string[][] rows)
    {
        var table = new Table();

        var tblProps = new TableProperties(
            new TableBorders(
                new TopBorder    { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder   { Val = BorderValues.Single, Size = 4 },
                new RightBorder  { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder   { Val = BorderValues.Single, Size = 4 }));
        table.AppendChild(tblProps);

        // Header row
        var headerRow = new TableRow();
        foreach (var h in headers)
        {
            headerRow.AppendChild(BuildCell(h, bold: true));
        }
        table.AppendChild(headerRow);

        foreach (var row in rows)
        {
            var tableRow = new TableRow();
            foreach (var cell in row)
            {
                tableRow.AppendChild(BuildCell(cell));
            }
            table.AppendChild(tableRow);
        }

        return table;
    }

    private static TableCell BuildCell(string text, bool bold = false)
    {
        var cell = new TableCell();
        cell.AppendChild(new Paragraph(MakeRun(text, bold: bold)));
        return cell;
    }
}
