// F-055 — FR-050 — Integration Signal Detection: App User Inventory (Story 3.7)
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseDocAgent.Api.Agent.Tools;

/// <summary>
/// Lists every application (non-human integration) user registered in the
/// connected environment, along with the security-role names assigned to each.
/// Definition of an application user (per FR-050): a <c>SystemUser</c> row whose
/// <c>isdisabled = false</c>, <c>islicensed = false</c>, and <c>applicationid</c>
/// is populated. The per-user role list is built by a secondary
/// <c>QueryExpression</c> against <c>systemuserroles</c> joined to <c>role</c>;
/// failures of that secondary query are isolated per user so a single bad row
/// cannot poison the whole result — the bad user is surfaced with the sentinel
/// <c>["(role lookup unavailable)"]</c> rather than an empty array, so the
/// renderer can distinguish "no roles" from "lookup failed".
/// Any outer SDK fault (the initial <c>SystemUser</c> query itself) is
/// converted into the sibling-tool error shape <c>{ "error": "Failed to list
/// application users" }</c> so the agent loop receives a tool result rather
/// than an exception (NFR-007 / sibling-tool contract from Story 3.4 / 3.5).
/// </summary>
public sealed class GetApplicationUsersTool : IDataverseTool
{
    public const string RoleLookupUnavailableSentinel = "(role lookup unavailable)";

    private readonly IOrganizationService _service;

    private static readonly JsonElement s_inputSchema =
        JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{}}""");

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public GetApplicationUsersTool(IOrganizationService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public string      Name        => "get_application_users";
    public string      Description =>
        "Returns all application (non-human, integration) users registered in the environment "
        + "along with their assigned security roles.";
    public JsonElement InputSchema => s_inputSchema;

    public Task<string> ExecuteAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var users = FetchApplicationUsers();
            var dtos  = new List<ApplicationUserDto>(users.Count);
            foreach (var user in users)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Story 3.7 code-review P1 — defensive null-entry guard. The
                // SDK's `EntityCollection.Entities` is documented as non-null
                // in practice but the contract is not enforced; a regression
                // or odd SDK path could surface a null element and an outer
                // NRE would otherwise convert into the "Failed to list…"
                // contract for the whole environment.
                if (user is null) continue;
                dtos.Add(BuildDto(user, cancellationToken));
            }
            return Task.FromResult(JsonSerializer.Serialize(
                new { applicationUsers = dtos }, s_jsonOptions));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancellation must short-circuit, not be sanitized into a tool result —
            // otherwise the orchestrator's cancellation contract is silently broken.
            throw;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Story 3.7 code-review P2 — sibling-tool contract demands the
            // agent loop receive a tool result rather than an exception for
            // ANY non-cancellation failure. The narrower FaultException /
            // TimeoutException / CommunicationException / HttpRequestException
            // filter was insufficient — an unexpected exception type (e.g.
            // InvalidOperationException from a malformed Entity attribute
            // cast, NRE from an unexpected SDK shape) would otherwise unwind
            // into the orchestrator and crash Mode 1. NFR-007 holds: no
            // exception details are echoed; the structured error is fixed
            // text keyed on the tool name.
            return Task.FromResult(JsonSerializer.Serialize(
                new { error = "Failed to list application users" }, s_jsonOptions));
        }
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private IReadOnlyList<Entity> FetchApplicationUsers()
    {
        // FR-050 definition of an application user. `isdisabled = false` is a
        // defensive extension (disabled app users are stale registrations and
        // should not be flagged as live integration signals — see story Dev Notes).
        var query = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("systemuserid", "fullname", "applicationid", "internalemailaddress"),
        };
        query.Criteria.AddCondition("isdisabled",    ConditionOperator.Equal,    false);
        query.Criteria.AddCondition("islicensed",    ConditionOperator.Equal,    false);
        query.Criteria.AddCondition("applicationid", ConditionOperator.NotNull);

        var result = _service.RetrieveMultiple(query);
        return result?.Entities is null ? Array.Empty<Entity>() : (IReadOnlyList<Entity>)result.Entities;
    }

    private ApplicationUserDto BuildDto(Entity user, CancellationToken cancellationToken)
    {
        // Tool-output JSON keys deliberately mirror the Claude-output shape
        // described in PromptBuilder (`displayName`, `applicationId`, `email`,
        // `roles`) so Claude can "pass the role array through verbatim" per
        // AC-7's prompt rule. The Dataverse source attributes are still the
        // canonical SDK names (`fullname`, `applicationid`,
        // `internalemailaddress`) per AC-3.
        var dto = new ApplicationUserDto
        {
            DisplayName   = user.GetAttributeValue<string?>("fullname"),
            ApplicationId = ExtractApplicationId(user),
            Email         = NullIfBlank(user.GetAttributeValue<string?>("internalemailaddress")),
            Roles         = FetchRolesForUser(user.Id, cancellationToken),
        };
        return dto;
    }

    private static string? ExtractApplicationId(Entity user)
    {
        // applicationid surfaces as either a string or a Guid depending on SDK
        // path; normalise to lowercase string with no braces so the JSON shape
        // matches AC-3 verbatim. Story 3.7 code-review P4 — `Guid.Empty` is
        // an all-zeros sentinel that does not identify a real Azure AD app
        // registration; surface as null so the renderer cell is "(not
        // available)" rather than a meaningless zero-Guid string.
        // Code-review P4 — also handle EntityReference (some SDK paths
        // surface this for Lookup-shaped Uniqueidentifier columns); without
        // this arm the fallback ToString() would emit the runtime type name.
        if (!user.Attributes.TryGetValue("applicationid", out var raw) || raw is null)
            return null;
        return raw switch
        {
            Guid g when g == Guid.Empty => null,
            Guid g                      => g.ToString(),
            string s                    => string.IsNullOrWhiteSpace(s) ? null : s,
            EntityReference er          => er.Id == Guid.Empty ? null : er.Id.ToString(),
            _                           => null,
        };
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private IReadOnlyList<string> FetchRolesForUser(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var query = new QueryExpression("role")
            {
                ColumnSet = new ColumnSet("name"),
            };
            var link = query.AddLink("systemuserroles", "roleid", "roleid");
            link.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);

            var result = _service.RetrieveMultiple(query);
            if (result?.Entities is null || result.Entities.Count == 0)
                return Array.Empty<string>();

            var names = new List<string>(result.Entities.Count);
            foreach (var entity in result.Entities)
            {
                // Story 3.7 code-review P1 — defensive null-entity guard for
                // the role result (mirrors the outer-loop guard).
                if (entity is null) continue;
                // Story 3.7 code-review P5 — whitespace-only role names are
                // semantically equivalent to "no name" and would render as
                // ", , " in the Word cell. Filter at the boundary.
                var name = entity.GetAttributeValue<string?>("name");
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name!);
            }
            return names;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // NFR-007 — cancellation MUST propagate; do NOT swallow it inside
            // the per-user catch. Without the `when` filter the broader catch
            // below would silently mask a cancelling orchestrator.
            throw;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // NFR-007 — never echo ex.Message; the SDK fault can carry
            // connection-string fragments. The sentinel array preserves the
            // renderer's "no roles" vs "lookup failed" distinction (AC-4).
            // Story 3.7 code-review P3 — broadened from the narrow
            // FaultException / Timeout / Communication / HttpRequest filter
            // to ANY non-cancellation exception, so a single unexpected SDK
            // shape (NRE, InvalidCast, ArgumentException) on one user does
            // not abort enumeration of every remaining user — AC-4 mandates
            // per-user isolation, not "isolation only for documented fault
            // types."
            return new[] { RoleLookupUnavailableSentinel };
        }
    }

    // ── DTO ───────────────────────────────────────────────────────────────────

    private sealed class ApplicationUserDto
    {
        public string?               DisplayName   { get; set; }
        public string?               ApplicationId { get; set; }
        public string?               Email         { get; set; }
        public IReadOnlyList<string> Roles         { get; set; } = Array.Empty<string>();
    }
}
