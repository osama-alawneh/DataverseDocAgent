// F-013 — FR-013 — DocxBuilder produces the Mode 1 .docx (Story 3.5, PRD §8.1)
// F-047 — FR-042 — Publisher Prefix Summary sub-section (Story 3.6)
// F-055 — FR-050 — Section 5 "Application Users (Integration Signals)" (Story 3.7)
using System.Text;
using DataverseDocAgent.Api.Agent.Tools;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DataverseDocAgent.Api.Documents;

/// <summary>
/// Builds the Mode 1 .docx body from a <see cref="GeneratedDocumentModel"/>.
/// Sections (per PRD §8.1): (1) Executive Summary, (2) Custom Tables,
/// (3) Field Catalogue, (4) Relationship Map, (5) Application Users
/// (Integration Signals — FR-050 / Story 3.7). Output is a fully-formed
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
            AppendHeading(body, "Collection and analysis coverage", level: 1);
            AppendParagraph(body, "Counts describe collected evidence only. Missing or failed collection is not evidence of absence. Complexity is based on the collected scope.");
            if (model.Coverage.Count == 0) AppendParagraph(body, "Coverage was not supplied; completeness is unknown.");
            foreach (var item in model.Coverage) AppendBulletParagraph(body, item);
            AppendCustomTablesSection(body, model.Tables);
            AppendFieldCatalogueSection(body, model.Tables, model.Fields);
            AppendRelationshipMapSection(body, model.Tables, model.Relationships);
            AppendApplicationUsersSection(body, model.Tables, model.ApplicationUsers);
            AppendHeading(body, "6. Authoritative evidence references", level: 1);
            AppendParagraph(body, "The following records preserve the collected metadata. AI observations above are labelled separately and do not replace these facts.");
            foreach (var item in model.Evidence)
            {
                AppendHeading(body, item.Id, level: 2);
                AppendParagraph(body, $"Kind: {item.Kind}; parent: {item.ParentId ?? "none"}");
                AppendParagraph(body, item.RawJson);
            }

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
        return "Prefix names are a naming heuristic. Publisher ownership is unknown without solution and publisher metadata; prefixes do not establish Microsoft, client, or ISV ownership.";
    }
    private static void AppendCustomTablesSection(Body body, IReadOnlyList<TableInfo> tables)
    {
        AppendHeading(body, "2. Custom Tables", level: 1);
        if (tables.Count == 0)
        {
            AppendParagraph(body, "No custom table records are available in this snapshot. See collection coverage.", italic: true);
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
            AppendParagraph(body, "No field catalogue is available. See collection coverage.", italic: true);
            return;
        }

        foreach (var table in tables)
        {
            AppendHeading(body, table.DisplayName ?? table.LogicalName, level: 2);
            if (!fieldsByTable.TryGetValue(table.LogicalName, out var fields) || fields.Count == 0)
            {
                AppendParagraph(body, "No custom field records are available for this table. See collection coverage.", italic: true);
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

    // ── Story 3.7 — Section 5: Application Users (Integration Signals) ───────

    private const string ApplicationUsersProse =
        "Application users are typically used by external integrations. The following "
        + "application users are registered and may be writing to tables in this environment.";

    private const string NoApplicationUsersSentence =
        "No application user records are available in this snapshot. See collection coverage.";

    private static void AppendApplicationUsersSection(
        Body body,
        IReadOnlyList<TableInfo> tables,
        IReadOnlyList<ApplicationUserInfo> applicationUsers)
    {
        // Always retain the section so an empty or failed extraction remains visible.


        AppendHeading(body, "5. Application Users (Integration Signals)", level: 1);
        AppendParagraph(body, ApplicationUsersProse);

        if (applicationUsers.Count == 0)
        {
            AppendParagraph(body, NoApplicationUsersSentence);
            return;
        }

        // AC-9 — three-column table: Display Name | Application ID | Roles.
        // An explicit empty role list renders "(no roles assigned)" so the
        // cell is never blank; the sentinel "(role lookup unavailable)" from
        // GetApplicationUsersTool is preserved verbatim. Story 3.7
        // code-review P6 routes role-list rendering through a single helper
        // so null/whitespace entries, duplicates, and the sentinel-mixed-
        // with-real-roles case (Claude prompt drift) cannot produce
        // misleading cells like "Reader, , Reader" or
        // "(role lookup unavailable), Reader".
        var rows = applicationUsers.Select(u => new[]
        {
            u.DisplayName   ?? string.Empty,
            u.ApplicationId ?? string.Empty,
            FormatRolesCell(u.Roles),
        }).ToArray();

        body.AppendChild(BuildTable(
            new[] { "Display Name", "Application ID", "Roles" },
            rows));
    }

    internal static string FormatRolesCell(IReadOnlyList<string>? roles)
    {
        // Story 3.7 code-review P6 / P7.
        // 1. Missing roles mean unavailable; an explicit empty array means no roles returned.
        // 2. If the role-lookup sentinel appears anywhere in the array,
        //    render the sentinel ALONE (the lookup failed; mixing it with
        //    other entries would be semantically meaningless and is the
        //    Claude-fabrication failure mode the prompt rule tries to
        //    prevent).
        // 3. Otherwise: filter null / whitespace entries, dedupe (preserves
        //    first-occurrence order), and join with ", ".
        if (roles is null) return GetApplicationUsersTool.RoleLookupUnavailableSentinel;
        if (roles.Count == 0) return "(no roles assigned)";

        foreach (var role in roles)
        {
            if (string.Equals(role, GetApplicationUsersTool.RoleLookupUnavailableSentinel, StringComparison.Ordinal))
                return GetApplicationUsersTool.RoleLookupUnavailableSentinel;
        }

        var cleaned = roles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return cleaned.Count == 0 ? GetApplicationUsersTool.RoleLookupUnavailableSentinel : string.Join(", ", cleaned);
    }

    private static void AppendRelationshipMapSection(
        Body body,
        IReadOnlyList<TableInfo> tables,
        IReadOnlyDictionary<string, IReadOnlyList<RelationshipInfo>> relsByTable)
    {
        AppendHeading(body, "4. Relationship Map", level: 1);
        if (tables.Count == 0)
        {
            AppendParagraph(body, "No relationship map is available. See collection coverage.", italic: true);
            return;
        }

        foreach (var table in tables)
        {
            AppendHeading(body, table.DisplayName ?? table.LogicalName, level: 2);
            if (!relsByTable.TryGetValue(table.LogicalName, out var rels) || rels.Count == 0)
            {
                AppendParagraph(body, "No relationship records are available for this table. See collection coverage.", italic: true);
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
                new LeftBorder   { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder  { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder   { Val = BorderValues.Single, Size = 4 }));
        table.AppendChild(tblProps);
        table.AppendChild(new TableGrid(headers.Select(_ => new GridColumn())));

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
