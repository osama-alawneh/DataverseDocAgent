// F-029, F-030, F-031 — Permission pre-flight checker service
// FR-029, FR-030, FR-031 — Missing/extra privilege detection
using DataverseDocAgent.Api.Common;
using DataverseDocAgent.Api.Dataverse;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseDocAgent.Api.Features.SecurityCheck;

public sealed class SecurityCheckService
{
    // F-029, F-030, F-031 — All 12 required privileges (PRD Section 5.4)
    // All three modes require identical permissions.
    // Read Role covers the roleprivileges intersect table implicitly — Dataverse
    // has no standalone prvReadRolePrivilege.
    public static readonly IReadOnlyList<string> RequiredPrivileges =
    [
        "Read Entity",
        "Read Attribute",
        "Read Relationship",
        "Read PluginAssembly",
        "Read PluginType",
        "Read SdkMessageProcessingStep",
        "Read WebResource",
        "Read Workflow",
        "Read Role",
        "Read SystemForm",
        "Read Query",
        "Read Organization",
    ];

    // Privileges that Dataverse auto-grants to every security role when the
    // SharePoint Document Management Integration feature is enabled on an env.
    // They cannot be removed via the role editor or by editing customizations.xml
    // (Dataverse re-adds them on import). Harmless for this tool — document
    // generation never touches SharePoint entities. Excluded from extra[] so the
    // checker does not surface false-positive "remove these" recommendations.
    public static readonly IReadOnlyList<string> KnownHarmlessExtraPrivileges =
    [
        "Read SharePointData",
        "Write SharePointData",
        "Create SharePointData",
        "Read SharePointDocument",
    ];

    private readonly IDataverseConnectionFactory _connectionFactory;
    private readonly ILogger<SecurityCheckService> _logger;

    public SecurityCheckService(
        IDataverseConnectionFactory connectionFactory,
        ILogger<SecurityCheckService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>
    /// Checks whether the service account described by <paramref name="request"/>
    /// holds exactly the required Dataverse privileges.
    /// Always returns HTTP 200 — semantic failures are encoded in the response body.
    /// </summary>
    public async Task<SecurityCheckResponse> CheckAsync(
        SecurityCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        // NFR-002 — 9-second inner timeout; caller's token also respected
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(9));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        var credentials = new EnvironmentCredentials
        {
            EnvironmentUrl = request.EnvironmentUrl!,
            TenantId       = request.TenantId!,
            ClientId       = request.ClientId!,
            ClientSecret   = request.ClientSecret!,
        };

        // Step 1 — Connect (AC-5: auth failure → blocked, HTTP 200, no credential logging)
        ServiceClient client;
        try
        {
            client = await _connectionFactory.ConnectAsync(credentials, linkedCts.Token);
        }
        catch (DataverseConnectionException)
        {
            // AC-7: credential values must not appear in log output
            _logger.LogWarning("Permission check: authentication to Dataverse failed");
            return BuildCredentialFailureResponse();
        }

        using (client)
        {
            try
            {
                // Step 2 — Resolve caller's own systemuserid via WhoAmI.
                // Cannot query the systemuser entity directly — that requires prvReadUser,
                // which is intentionally NOT in the DataverseDocAgent Reader role (PRD 5.4
                // lists exactly 12 privileges, Read User is not among them).
                var systemUserId = await GetCallerUserIdAsync(client, linkedCts.Token);

                // Step 3 — Retrieve all privilege names for that user
                var userPrivilegeNames = await GetUserPrivilegeNamesAsync(
                    client, systemUserId, linkedCts.Token);

                // Step 4 — Compute passed / missing / extra
                var (passed, missing, extra) = ComputePrivilegeSets(userPrivilegeNames, RequiredPrivileges);

                // Step 5 — Build and return response
                return BuildResponse(passed, missing, extra);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Permission check timed out after 9 seconds");
                return BuildErrorResponse(
                    "Permission check timed out. The Dataverse environment may be slow or unreachable. Please try again.");
            }
            catch (InvalidOperationException ex)
            {
                // e.g. "No application user found" — safe to surface message
                _logger.LogWarning("Permission check failed: {Message}", ex.Message);
                return BuildErrorResponse(ex.Message);
            }
            catch (Exception ex)
            {
                // Dataverse SDK errors (FaultException, etc.) — do not leak details
                _logger.LogWarning(ex, "Permission check failed during privilege retrieval");
                return BuildErrorResponse(
                    "An error occurred while checking permissions. Please verify the environment is accessible and try again.");
            }
        }
    }

    // ── Internal helpers (internal for unit-test visibility via InternalsVisibleTo) ──

    internal static (string[] passed, string[] missing, string[] extra) ComputePrivilegeSets(
        IReadOnlyList<string> userPrivilegeNames,
        IReadOnlyList<string> requiredPrivileges)
    {
        var userSet     = new HashSet<string>(userPrivilegeNames, StringComparer.OrdinalIgnoreCase);
        var requiredSet = new HashSet<string>(requiredPrivileges, StringComparer.OrdinalIgnoreCase);
        var ignoredSet  = new HashSet<string>(KnownHarmlessExtraPrivileges, StringComparer.OrdinalIgnoreCase);

        var passed  = requiredPrivileges.Where(p => userSet.Contains(p)).ToArray();
        var missing = requiredPrivileges.Where(p => !userSet.Contains(p)).ToArray();
        var extra   = userPrivilegeNames
            .Where(p => !requiredSet.Contains(p) && !ignoredSet.Contains(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return (passed, missing, extra);
    }

    internal static string BuildRecommendation(string[] missing, string[] extra)
    {
        if (missing.Length > 0)
        {
            var list = string.Join(", ", missing);
            var noun = missing.Length == 1 ? "permission is" : "permissions are";
            return $"Cannot run — {missing.Length} required {noun} missing. " +
                   $"Please add {list} to the DataverseDocAgent Reader role and re-run this check.";
        }

        if (extra.Length > 0)
        {
            var list = string.Join(", ", extra);
            var noun = extra.Length == 1 ? "unnecessary privilege" : "unnecessary privileges";
            return $"Tool can run safely. However, we detected {extra.Length} {noun}. " +
                   $"We recommend removing {list} from the service account to minimise risk surface.";
        }

        return "All permissions verified. Safe to run all modes.";
    }

    internal static SecurityCheckResponse BuildResponse(
        string[] passed,
        string[] missing,
        string[] extra)
    {
        bool blocked = missing.Length > 0;
        return new SecurityCheckResponse
        {
            Status         = blocked ? "blocked" : "ready",
            SafeToRun      = !blocked,
            Passed         = passed,
            Missing        = missing,
            Extra          = extra,
            Recommendation = BuildRecommendation(missing, extra),
        };
    }

    internal static SecurityCheckResponse BuildCredentialFailureResponse() =>
        BuildErrorResponse(
            "Cannot connect to the Dataverse environment. Verify that the environment URL, " +
            "tenant ID, client ID, and client secret are correct, then re-run this check.");

    internal static SecurityCheckResponse BuildErrorResponse(string recommendation) =>
        new()
        {
            Status         = "blocked",
            SafeToRun      = false,
            Passed         = [],
            Missing        = [],
            Extra          = [],
            Recommendation = recommendation,
        };

    /// <summary>Maps "prvReadPluginAssembly" → "Read PluginAssembly" (and other actions).
    /// Unrecognized actions fall back to raw name without "prv" prefix to avoid undercounting extras.</summary>
    internal static string? MapPrivilegeName(string privilegeName)
    {
        if (!privilegeName.StartsWith("prv", StringComparison.OrdinalIgnoreCase))
            return null;

        var withoutPrefix = privilegeName[3..];

        if (withoutPrefix.Length == 0)
            return null;

        // AppendTo must precede Append to avoid partial match
        string[] actions = ["AppendTo", "Append", "Read", "Write", "Create", "Delete", "Share", "Assign"];

        foreach (var action in actions)
        {
            if (withoutPrefix.StartsWith(action, StringComparison.OrdinalIgnoreCase)
                && withoutPrefix.Length > action.Length)
            {
                return $"{action} {withoutPrefix[action.Length..]}";
            }
        }

        // If withoutPrefix is exactly a known action with no entity, it's invalid (e.g., "prvRead")
        foreach (var action in actions)
        {
            if (withoutPrefix.Equals(action, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        // Fallback: unrecognized action (e.g., prvPublishEntity) — include raw to avoid
        // silently dropping privileges and undercounting extra[]
        return withoutPrefix;
    }

    // ── Private Dataverse helpers ──────────────────────────────────────────────

    private static async Task<Guid> GetCallerUserIdAsync(
        ServiceClient client,
        CancellationToken cancellationToken)
    {
        var response = (WhoAmIResponse)await client.ExecuteAsync(new WhoAmIRequest(), cancellationToken);

        if (response.UserId == Guid.Empty)
            throw new InvalidOperationException("WhoAmI returned an empty UserId for the supplied credentials.");

        return response.UserId;
    }

    private static async Task<IReadOnlyList<string>> GetUserPrivilegeNamesAsync(
        ServiceClient client,
        Guid systemUserId,
        CancellationToken cancellationToken)
    {
        // RetrieveUserPrivilegesRequest covers all roles, including inherited ones
        var request  = new RetrieveUserPrivilegesRequest { UserId = systemUserId };
        var response = (RetrieveUserPrivilegesResponse)await client.ExecuteAsync(request, cancellationToken);

        if (response.RolePrivileges is null or { Length: 0 })
            return [];

        // Privilege names are not on RolePrivilege — query the privilege entity by GUID
        var ids = response.RolePrivileges
            .Select(p => p.PrivilegeId)
            .Distinct()
            .ToArray();

        return await FetchPrivilegeNamesByIdAsync(client, ids, cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> FetchPrivilegeNamesByIdAsync(
        ServiceClient client,
        Guid[] privilegeIds,
        CancellationToken cancellationToken)
    {
        if (privilegeIds.Length == 0)
            return [];

        // Batch fetch all privilege names in a single query
        var query = new QueryExpression("privilege")
        {
            ColumnSet = new ColumnSet("name"),
        };
        query.Criteria.AddCondition(
            "privilegeid",
            ConditionOperator.In,
            privilegeIds.Cast<object>().ToArray());

        var result = await client.RetrieveMultipleAsync(query, cancellationToken);

        var names = new List<string>(result.Entities.Count);
        foreach (var entity in result.Entities)
        {
            if (entity.Attributes.TryGetValue("name", out var nameObj)
                && nameObj is string rawName)
            {
                var mapped = MapPrivilegeName(rawName);
                if (mapped is not null)
                    names.Add(mapped);
            }
        }

        return names;
    }
}
