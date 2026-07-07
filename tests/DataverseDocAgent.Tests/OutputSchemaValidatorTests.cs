// F-045 — Story 4.1 Mode 1 output schema contract + validator tests (ADR-006 / NFR-017 / NFR-007).
// Negative-assertion culture (R-HF-10): reject tests assert the forbidden thing fails, with a
// positive anchor (a known-good sample passing) so a total regression that accepts everything
// still fails noisily.
using System.Text.Json;
using System.Text.Json.Nodes;
using DataverseDocAgent.Api.Agent;

namespace DataverseDocAgent.Tests;

public class OutputSchemaValidatorTests
{
    private static readonly string[] Ac2SectionKeys =
    {
        "executive_summary", "publisher_prefix_summary", "tables", "fields", "relationships",
        "plugins", "flows", "workflows", "javascript", "business_rules", "security_roles",
        "app_users", "recommendations", "top_risks",
    };

    private static readonly string[] TransitionalRequiredKeys =
    {
        "organisation", "tables", "fields", "relationships", "applicationUsers", "keyObservations",
    };

    private static string SchemaPath =>
        Path.Combine(AppContext.BaseDirectory, "docs", "output-schema-mode1.json");

    // JsonSchema.Net auto-registers a schema's $id into a process-global static registry
    // on first evaluation, and forbids re-registering the same $id from a second instance.
    // Production uses a DI singleton (one registration for the process); tests mirror that
    // with a single shared instance rather than constructing one per test.
    internal static readonly OutputSchemaValidator SharedValidator = new();

    private static OutputSchemaValidator NewValidator() => SharedValidator;

    // A golden, minimal Epic-3-shape sample built from the shipped PromptBuilder contract
    // (post-R-HF-10 slim field/relationship shapes). The unique sentinel value below is used
    // by the reject tests to prove no INSTANCE VALUE leaks into the failure paths (NFR-007).
    private const string SentinelValue = "SENTINEL_INSTANCE_VALUE_ZZZ";

    private static string GoldenEpic3Sample() => """
        {
          "organisation": {
            "environmentName": "Contoso",
            "environmentUrl": null,
            "version": "9.2.0.0",
            "baseLanguageName": "English"
          },
          "tables": [
            { "logicalName": "vel_account", "displayName": "Account", "schemaName": "vel_Account",
              "solutionName": "VelSolution", "description": null, "purpose": "Customer master records" }
          ],
          "fields": {
            "vel_account": [
              { "logicalName": "vel_name", "displayName": "Name", "attributeType": "String",
                "requiredLevel": "ApplicationRequired", "description": null }
            ]
          },
          "relationships": {
            "vel_account": [
              { "schemaName": "vel_account_contacts", "relationshipType": "OneToMany",
                "relatedEntity": "vel_contact", "cascadeDelete": "RemoveLink",
                "businessMeaning": "An account owns many contacts" }
            ]
          },
          "applicationUsers": [
            { "displayName": "Integration App", "applicationId": "11111111-1111-1111-1111-111111111111",
              "email": null, "roles": ["Reader"] }
          ],
          "keyObservations": [
            "Single custom publisher prefix in use.",
            "One custom table with a self-referencing relationship.",
            "One application user with read-only access."
          ]
        }
        """;

    // ── Group 1: schema document structure (pins AC-1, AC-2, AC-3, AC-7) ─────────

    [Fact]
    public void SchemaFile_ExistsInRepoDocsFolder()
    {
        // Walk up from the test output dir to the repo root (dir with the .sln) and
        // assert the source-of-truth schema lives under docs/.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DataverseDocAgent.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        Assert.True(File.Exists(Path.Combine(dir!.FullName, "docs", "output-schema-mode1.json")),
            "docs/output-schema-mode1.json must exist at the repo root (ADR-006 deliverable).");
    }

    [Fact]
    public void SchemaFile_IsCopiedNextToAssembly_ForPathBasedLoad()
    {
        // AC-7 — the schema is delivered to the output dir by a csproj Content item so the
        // validator loads it by path (no schema string in C#).
        Assert.True(File.Exists(SchemaPath),
            "Schema must be copied to AppContext.BaseDirectory/docs via the csproj Content item.");
    }

    [Fact]
    public void Schema_DeclaresDraft2020_12_AndSemverVersion()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        var root = doc.RootElement;

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", root.GetProperty("$schema").GetString());
        Assert.Contains("1.0.0", root.GetProperty("$id").GetString());
        Assert.Equal("1.0.0", root.GetProperty("version").GetString());
        // Strict root — unknown keys are prompt drift (AC, Dev Notes: do NOT soften to true).
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void Schema_RequiresAllTransitionalEpic3Keys()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        var required = doc.RootElement.GetProperty("required")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        foreach (var key in TransitionalRequiredKeys)
        {
            Assert.Contains(key, required);
        }
    }

    [Fact]
    public void Schema_DefinesEveryAc2SectionKey()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        var props = doc.RootElement.GetProperty("properties");

        foreach (var key in Ac2SectionKeys)
        {
            Assert.True(props.TryGetProperty(key, out _),
                $"AC-2 section key '{key}' must be defined as a root property.");
        }
    }

    [Fact]
    public void Schema_ConfidenceLevel_IsExactlyTheThreeUppercaseValues()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        var enumValues = doc.RootElement
            .GetProperty("$defs").GetProperty("confidenceLevel").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).ToArray();

        Assert.Equal(new[] { "VERIFIED", "INFERRED", "ESTIMATED" }, enumValues);
    }

    [Fact]
    public void Schema_EveryDefWithConfidence_RequiresIt_AndRefsCanonicalEnum()
    {
        // AC-3 pinned structurally: any $defs object that declares a `confidence` property
        // MUST list it in `required` and reference the single canonical confidenceLevel enum.
        // Also asserts the three known finding/recommendation defs actually carry confidence
        // (positive anchor — so an empty walk cannot pass this test).
        using var doc = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        var defs = doc.RootElement.GetProperty("$defs");

        var confidenceCarriers = new List<string>();
        foreach (var def in defs.EnumerateObject())
        {
            if (def.Value.ValueKind != JsonValueKind.Object) continue;
            if (!def.Value.TryGetProperty("properties", out var props)) continue;
            if (!props.TryGetProperty("confidence", out var confidence)) continue;

            confidenceCarriers.Add(def.Name);

            var required = def.Value.TryGetProperty("required", out var req)
                ? req.EnumerateArray().Select(e => e.GetString()).ToList()
                : new List<string?>();
            Assert.Contains("confidence", required);

            Assert.Equal("#/$defs/confidenceLevel", confidence.GetProperty("$ref").GetString());
        }

        foreach (var expected in new[] { "finding", "recommendation", "topRisk", "executiveSummary" })
        {
            Assert.Contains(expected, confidenceCarriers);
        }
    }

    // ── Group 2: validator accept ────────────────────────────────────────────────

    [Fact]
    public void Validate_GoldenEpic3Sample_Passes()
    {
        var result = NewValidator().Validate(JsonNode.Parse(GoldenEpic3Sample()));
        Assert.True(result.IsValid, "Golden Epic-3-shape sample must satisfy the transitional contract. "
            + "Failure paths: " + string.Join(" | ", result.FailurePaths));
        Assert.Empty(result.FailurePaths);
    }

    [Fact]
    public void Validate_TargetSectionsPopulated_Passes()
    {
        const string sample = """
            {
              "organisation": { "environmentName": "Contoso" },
              "tables": [],
              "fields": {},
              "relationships": {},
              "applicationUsers": [],
              "keyObservations": ["one", "two", "three"],
              "executive_summary": { "narrative": "Moderately complex environment.", "confidence": "INFERRED" },
              "recommendations": [
                { "severity": "Critical", "category": "Code Safety", "entityName": "EquipmentStatusPlugin",
                  "what": "Add a try/catch.", "whyProblem": "Unhandled exception aborts the transaction.",
                  "consequence": "Records fail to save.", "howToFix": "Wrap in try/catch and log.",
                  "estimatedEffort": "2 hours", "confidence": "VERIFIED" }
              ],
              "top_risks": [
                { "severity": "High", "what": "Sync plugin with no error handling.",
                  "whyProblem": "Blocks the platform pipeline.", "consequence": "User-facing save failures.",
                  "howToFix": "Convert to async or add handling.", "estimatedEffort": "1 day",
                  "confidence": "ESTIMATED" }
              ]
            }
            """;

        var result = NewValidator().Validate(JsonNode.Parse(sample));
        Assert.True(result.IsValid, "Well-formed target sections must pass. Failure paths: "
            + string.Join(" | ", result.FailurePaths));
    }

    // ── Group 3: validator reject ────────────────────────────────────────────────

    [Fact]
    public void Validate_MissingRequiredTransitionalKey_Fails()
    {
        // Drop `applicationUsers` (a required transitional key).
        var node = JsonNode.Parse(GoldenEpic3Sample())!.AsObject();
        node.Remove("applicationUsers");

        var result = NewValidator().Validate(node);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.FailurePaths);
        Assert.Contains(result.FailurePaths, p => p.Contains("required"));
    }

    [Fact]
    public void Validate_UnknownRootKey_Fails_AdditionalPropertiesFalsePinned()
    {
        var node = JsonNode.Parse(GoldenEpic3Sample())!.AsObject();
        node["surprise_section"] = SentinelValue;

        var result = NewValidator().Validate(node);

        Assert.False(result.IsValid);
        Assert.Contains(result.FailurePaths, p => p.Contains("additionalProperties"));
        // NFR-007 — the offending instance VALUE must never appear in the failure detail.
        Assert.DoesNotContain(result.FailurePaths, p => p.Contains(SentinelValue));
    }

    [Fact]
    public void Validate_TargetSectionObjectMissingConfidence_Fails()
    {
        const string sample = """
            {
              "organisation": { "environmentName": "Contoso" },
              "tables": [], "fields": {}, "relationships": {}, "applicationUsers": [],
              "keyObservations": ["one", "two", "three"],
              "recommendations": [
                { "severity": "Critical", "category": "Code Safety", "entityName": "FooPlugin",
                  "what": "x", "whyProblem": "y", "consequence": "z", "howToFix": "a",
                  "estimatedEffort": "2h" }
              ]
            }
            """;

        var result = NewValidator().Validate(JsonNode.Parse(sample));

        Assert.False(result.IsValid);
        Assert.Contains(result.FailurePaths, p => p.Contains("required"));
    }

    [Fact]
    public void Validate_WrongConfidenceCasing_Fails()
    {
        const string sample = """
            {
              "organisation": { "environmentName": "Contoso" },
              "tables": [], "fields": {}, "relationships": {}, "applicationUsers": [],
              "keyObservations": ["one", "two", "three"],
              "executive_summary": { "narrative": "x", "confidence": "verified" }
            }
            """;

        var result = NewValidator().Validate(JsonNode.Parse(sample));

        Assert.False(result.IsValid);
        Assert.Contains(result.FailurePaths, p => p.Contains("enum"));
        // NFR-007 — the bad value "verified" must not be echoed in the paths.
        Assert.DoesNotContain(result.FailurePaths, p => p.Contains("verified"));
    }

    [Fact]
    public void Validate_FailurePaths_ContainInstanceLocationsAndNoInstanceValues()
    {
        // Two independent violations with a distinctive sentinel value present in the instance.
        var node = JsonNode.Parse(GoldenEpic3Sample())!.AsObject();
        node["organisation"]!["environmentName"] = SentinelValue; // still valid (string) — proves value absence
        node["another_unknown"] = "unused";                        // additionalProperties violation

        var result = NewValidator().Validate(node);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.FailurePaths);
        // Positive anchor — the paths reference instance/schema locations (JSON pointers).
        Assert.All(result.FailurePaths, p => Assert.Contains("::", p));
        // NFR-007 — never leak instance values.
        Assert.DoesNotContain(result.FailurePaths, p => p.Contains(SentinelValue));
    }

    // ── Group 5: bounded logging ─────────────────────────────────────────────────

    [Fact]
    public void Validate_ManyViolations_FailurePathsCappedAtTen()
    {
        // keyObservations with 15 integers → 15 item type violations. Detail is capped.
        var badObservations = string.Join(",", Enumerable.Range(1, 15));
        var sample = $$"""
            {
              "organisation": { "environmentName": "Contoso" },
              "tables": [], "fields": {}, "relationships": {}, "applicationUsers": [],
              "keyObservations": [{{badObservations}}]
            }
            """;

        var result = NewValidator().Validate(JsonNode.Parse(sample));

        Assert.False(result.IsValid);
        Assert.True(result.FailurePaths.Count <= OutputSchemaValidator.MaxFailurePaths,
            $"Failure paths must be capped at {OutputSchemaValidator.MaxFailurePaths}, got {result.FailurePaths.Count}.");
        Assert.Equal(OutputSchemaValidator.MaxFailurePaths, result.FailurePaths.Count);
    }
}
