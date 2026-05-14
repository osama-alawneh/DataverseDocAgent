// F-055 — FR-050 — Integration Signal Detection: App User Inventory (Story 3.7)
using System.Net.Http;
using System.ServiceModel;
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
        catch (Exception ex) when (ex is FaultException<OrganizationServiceFault>
                                       or TimeoutException
                                       or CommunicationException
                                       or HttpRequestException)
        {
            // NFR-007 / sibling-tool contract — never let raw SDK fault text reach
            // Claude. Structured JSON keeps the agent loop alive and lets the
            // orchestrator continue with the rest of the Mode 1 plan.
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
        // matches AC-3 verbatim.
        if (!user.Attributes.TryGetValue("applicationid", out var raw) || raw is null)
            return null;
        return raw switch
        {
            Guid g     => g.ToString(),
            string s   => string.IsNullOrWhiteSpace(s) ? null : s,
            _          => raw.ToString(),
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
                if (entity.GetAttributeValue<string?>("name") is { Length: > 0 } name)
                    names.Add(name);
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
        catch (Exception ex) when (ex is FaultException<OrganizationServiceFault>
                                       or TimeoutException
                                       or CommunicationException
                                       or HttpRequestException)
        {
            // NFR-007 — never echo ex.Message; the SDK fault can carry
            // connection-string fragments. The sentinel array preserves the
            // renderer's "no roles" vs "lookup failed" distinction (AC-4).
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
