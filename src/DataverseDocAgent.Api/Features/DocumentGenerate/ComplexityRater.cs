// F-011 — FR-011 — Deterministic environment complexity rating (Story 3.5)
namespace DataverseDocAgent.Api.Features.DocumentGenerate;

/// <summary>
/// Deterministic Low/Medium/High rating for the scanned environment. Computed
/// in C# from counts so the executive summary's <c>complexityRating</c> is
/// reproducible — Claude is explicitly NOT trusted with this value (FR-011).
/// Thresholds (AC-5):
///   High   = tableCount &gt; 50  OR fieldCount &gt; 500
///   Medium = tableCount &gt;= 10 OR fieldCount &gt;= 100
///   Low    = otherwise
/// <paramref name="relationshipCount"/> is accepted for symmetry with the
/// upstream counts but does not currently influence the rating.
/// </summary>
public static class ComplexityRater
{
    public const string Low    = "Low";
    public const string Medium = "Medium";
    public const string High   = "High";

    public static string Rate(int tableCount, int fieldCount, int relationshipCount)
    {
        // Negative counts indicate a caller bug — clamp to zero rather than emit
        // a misleading rating from garbage input.
        var tables = Math.Max(0, tableCount);
        var fields = Math.Max(0, fieldCount);
        _ = relationshipCount; // reserved for future tuning; explicit discard.

        if (tables > 50 || fields > 500)  return High;
        if (tables >= 10 || fields >= 100) return Medium;
        return Low;
    }
}
