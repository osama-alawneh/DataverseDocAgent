// F-045 — Mode 1 output schema validation gate (Story 4.1)
// ADR-006 (confidence-layer output schema) — NFR-017 (untagged AI output = quality failure)
// NFR-007 — failure detail is schema paths + keywords ONLY; never instance values.
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace DataverseDocAgent.Api.Agent;

/// <summary>
/// Validates Claude's Mode 1 final JSON against the versioned output contract
/// (<c>docs/output-schema-mode1.json</c>, JSON Schema draft 2020-12) BEFORE it is
/// typed-deserialised and handed to <c>DocxBuilder</c>. This is the ADR-006
/// enforcement gate: malformed or contract-drifting output is rejected here with
/// named schema paths rather than surfacing as an opaque downstream parse failure.
/// </summary>
public interface IOutputSchemaValidator
{
    /// <summary>
    /// Evaluates <paramref name="instance"/> against the Mode 1 output schema.
    /// Never throws for a validation failure — the caller decides the failure
    /// semantics. The returned detail is bounded (<see cref="OutputSchemaValidator.MaxFailurePaths"/>)
    /// and contains schema/instance <em>paths</em> only, never instance values (NFR-007).
    /// </summary>
    OutputSchemaValidationResult Validate(JsonNode? instance);
}

/// <summary>
/// Outcome of a single schema evaluation. <see cref="FailurePaths"/> is empty when
/// <see cref="IsValid"/> is <c>true</c>, otherwise a bounded, de-duplicated list of
/// <c>instanceLocation :: evaluationPath</c> strings suitable for a log line.
/// </summary>
public sealed record OutputSchemaValidationResult(bool IsValid, IReadOnlyList<string> FailurePaths);

/// <summary>
/// Singleton implementation. The schema is loaded once, lazily, from a file path
/// resolved against <see cref="AppContext.BaseDirectory"/> — the file is delivered
/// to the output directory by a <c>Content</c> item in the API csproj, so no schema
/// string is embedded in C# code (AC-7).
/// </summary>
public sealed class OutputSchemaValidator : IOutputSchemaValidator
{
    /// <summary>Upper bound on failure-path entries returned/logged (NFR-007 discipline — bounded output).</summary>
    public const int MaxFailurePaths = 10;

    private static readonly EvaluationOptions s_options = new() { OutputFormat = OutputFormat.List };

    private readonly Lazy<JsonSchema> _schema;

    /// <summary>Production constructor — resolves the schema shipped next to the assembly.</summary>
    public OutputSchemaValidator()
        : this(Path.Combine(AppContext.BaseDirectory, "docs", "output-schema-mode1.json"))
    {
    }

    /// <summary>Test/advanced-DI constructor — load the schema from an explicit path.</summary>
    internal OutputSchemaValidator(string schemaPath)
    {
        // Lazy so a missing/broken schema surfaces on first Mode 1 run (observable,
        // retryable) rather than as a fatal host-startup crash that would also block
        // the security-check endpoint (which has no Mode 1 dependency).
        _schema = new Lazy<JsonSchema>(
            () => JsonSchema.FromFile(schemaPath),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public OutputSchemaValidationResult Validate(JsonNode? instance)
    {
        // JsonSchema.Net 9.x evaluates a JsonElement; convert the node once.
        var element = JsonSerializer.SerializeToElement(instance);
        var results = _schema.Value.Evaluate(element, s_options);
        if (results.IsValid)
        {
            return new OutputSchemaValidationResult(true, Array.Empty<string>());
        }

        // OutputFormat.List flattens the evaluation tree into Details, but the failing
        // keyword can also live on the root node (e.g. `required` / `additionalProperties`
        // attach to the object node itself). Walk the root AND every detail; for each
        // invalid node, emit one entry PER failing keyword (the Errors dictionary KEY —
        // never its message VALUE, which may echo instance data (NFR-007)). Nodes that are
        // invalid only by propagation (no own Errors) are recorded location-only as a
        // fallback so the detail is never empty.
        var keyworded = new List<string>();
        var located   = new List<string>();

        void Visit(EvaluationResults node)
        {
            if (node.IsValid)
            {
                return;
            }

            var loc      = FormatLocation(node.InstanceLocation.ToString());
            var evalPath = node.EvaluationPath.ToString();

            AddDistinct(located, $"{loc} :: {evalPath}");

            if (node.Errors is { Count: > 0 } errors)
            {
                foreach (var keyword in errors.Keys)
                {
                    // keyword only — the schema keyword that failed, e.g. required / enum.
                    AddDistinct(keyworded, $"{loc} :: {evalPath}/{keyword}");
                }
            }
        }

        Visit(results);
        foreach (var detail in results.Details ?? Enumerable.Empty<EvaluationResults>())
        {
            Visit(detail);
        }

        var chosen = keyworded.Count > 0 ? keyworded : located;
        var paths = chosen.Take(MaxFailurePaths).ToList();

        if (paths.Count == 0)
        {
            // Defensive: a top-level failure with no enumerable detail. Record the root
            // location so the log line is never empty.
            paths.Add($"{FormatLocation(results.InstanceLocation.ToString())} :: {results.EvaluationPath}");
        }

        return new OutputSchemaValidationResult(false, paths);
    }

    private static void AddDistinct(List<string> list, string entry)
    {
        if (!list.Contains(entry))
        {
            list.Add(entry);
        }
    }

    // instanceLocation/evaluationPath are JSON Pointers into the instance SHAPE and the
    // schema — they never contain instance VALUES (NFR-007).
    private static string FormatLocation(string instanceLocation)
        => string.IsNullOrEmpty(instanceLocation) ? "(root)" : instanceLocation;
}
