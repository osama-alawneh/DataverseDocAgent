// F-055 — FR-050 — GetApplicationUsersTool unit tests (Story 3.7)
using System.ServiceModel;
using System.Text.Json;
using DataverseDocAgent.Api.Agent.Tools;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;

namespace DataverseDocAgent.Tests;

public class GetApplicationUsersToolTests
{
    // ── Contract tests ────────────────────────────────────────────────────────

    [Fact]
    public void Name_Is_get_application_users()
    {
        var tool = new GetApplicationUsersTool(new Mock<IOrganizationService>().Object);
        Assert.Equal("get_application_users", tool.Name);
    }

    [Fact]
    public void Description_IsNonEmpty()
    {
        var tool = new GetApplicationUsersTool(new Mock<IOrganizationService>().Object);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    [Fact]
    public void InputSchema_IsObjectWithNoProperties()
    {
        var tool   = new GetApplicationUsersTool(new Mock<IOrganizationService>().Object);
        var schema = tool.InputSchema;

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.Equal("object", schema.GetProperty("type").GetString());
        var props = schema.GetProperty("properties");
        Assert.Empty(props.EnumerateObject());
    }

    [Fact]
    public void Constructor_NullService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new GetApplicationUsersTool(null!));
    }

    // ── AC-2 / AC-3 happy path ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_PopulatedEnvironment_ReturnsUsersWithRoles()
    {
        var userId1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var appId1  = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var userId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var appId2  = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var systemUsers = new EntityCollection
        {
            Entities =
            {
                BuildSystemUserEntity(userId1, "Integration Sync",
                    appId1, "sync@contoso.onmicrosoft.com"),
                BuildSystemUserEntity(userId2, "Logic Apps Connector",
                    appId2, internalEmail: null),
            },
        };

        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q => IsSystemUserQuery(q))))
           .Returns(systemUsers);

        // Per-user role-lookup mocks: dispatch by the systemuserid link
        // criteria so each user gets a distinct result.
        svc.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q => IsRoleQueryForUser(q, userId1))))
           .Returns(BuildRoleCollection("Reader", "Custom Writer"));
        svc.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q => IsRoleQueryForUser(q, userId2))))
           .Returns(BuildRoleCollection("Workflow Runner"));

        var tool = new GetApplicationUsersTool(svc.Object);
        var json = await tool.ExecuteAsync(EmptyInput());

        var root = JsonDocument.Parse(json).RootElement;
        Assert.True(root.TryGetProperty("applicationUsers", out var users));
        Assert.Equal(2, users.GetArrayLength());

        var u0 = users[0];
        Assert.Equal("Integration Sync",                   u0.GetProperty("displayName").GetString());
        Assert.Equal(appId1.ToString(),                    u0.GetProperty("applicationId").GetString());
        Assert.Equal("sync@contoso.onmicrosoft.com",       u0.GetProperty("email").GetString());
        var roles0 = u0.GetProperty("roles").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "Reader", "Custom Writer" },  roles0);

        var u1 = users[1];
        Assert.Equal("Logic Apps Connector",               u1.GetProperty("displayName").GetString());
        Assert.Equal(appId2.ToString(),                    u1.GetProperty("applicationId").GetString());
        // null email is dropped by WhenWritingNull policy — the JSON object
        // simply has no `email` property in that case.
        Assert.False(u1.TryGetProperty("email", out _));
        var roles1 = u1.GetProperty("roles").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "Workflow Runner" },          roles1);
    }

    // ── AC-4 — per-user role-lookup failure isolated ───────────────────────────

    [Fact]
    public async Task ExecuteAsync_PerUserRoleLookupFailure_SurfacesSentinelForBadUserOnly()
    {
        var goodId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var badId  = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var systemUsers = new EntityCollection
        {
            Entities =
            {
                BuildSystemUserEntity(goodId, "Healthy App",
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "healthy@contoso.com"),
                BuildSystemUserEntity(badId, "Faulty App",
                    Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), null),
            },
        };

        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q => IsSystemUserQuery(q))))
           .Returns(systemUsers);
        svc.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q => IsRoleQueryForUser(q, goodId))))
           .Returns(BuildRoleCollection("Reader"));
        svc.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q => IsRoleQueryForUser(q, badId))))
           .Throws(new FaultException<OrganizationServiceFault>(
               new OrganizationServiceFault { Message = "transient SDK fault" }));

        var tool = new GetApplicationUsersTool(svc.Object);
        var json = await tool.ExecuteAsync(EmptyInput());

        var users = JsonDocument.Parse(json).RootElement.GetProperty("applicationUsers");
        Assert.Equal(2, users.GetArrayLength());

        // Healthy user kept its role; bad user surfaced sentinel.
        var goodRoles = users[0].GetProperty("roles").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "Reader" }, goodRoles);

        var badRoles = users[1].GetProperty("roles").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Single(badRoles);
        Assert.Equal(GetApplicationUsersTool.RoleLookupUnavailableSentinel, badRoles[0]);
    }

    // ── AC-3 / AC-12 — empty environment returns empty array, not error ───────

    [Fact]
    public async Task ExecuteAsync_EmptyEnvironment_ReturnsEmptyApplicationUsersArray()
    {
        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q => IsSystemUserQuery(q))))
           .Returns(new EntityCollection());

        var tool = new GetApplicationUsersTool(svc.Object);
        var json = await tool.ExecuteAsync(EmptyInput());

        var root = JsonDocument.Parse(json).RootElement;
        Assert.True(root.TryGetProperty("applicationUsers", out var users));
        Assert.Equal(JsonValueKind.Array, users.ValueKind);
        Assert.Equal(0, users.GetArrayLength());
        // No `error` key — empty list is a success, not a failure.
        Assert.False(root.TryGetProperty("error", out _));
    }

    // ── AC-5 — outer SDK fault returns structured error ───────────────────────

    [Fact]
    public async Task ExecuteAsync_OuterFault_ReturnsStructuredError()
    {
        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>()))
           .Throws(new FaultException<OrganizationServiceFault>(
               new OrganizationServiceFault { Message = "Authentication failed for tenant abc-123" }));

        var tool = new GetApplicationUsersTool(svc.Object);
        var json = await tool.ExecuteAsync(EmptyInput());

        var root = JsonDocument.Parse(json).RootElement;
        Assert.True(root.TryGetProperty("error", out var err));
        Assert.Equal("Failed to list application users", err.GetString());
        // No applicationUsers key on the error path.
        Assert.False(root.TryGetProperty("applicationUsers", out _));
    }

    [Fact]
    public async Task ExecuteAsync_OuterTimeout_ReturnsStructuredError()
    {
        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>()))
           .Throws(new TimeoutException("network slow"));

        var tool = new GetApplicationUsersTool(svc.Object);
        var json = await tool.ExecuteAsync(EmptyInput());

        var root = JsonDocument.Parse(json).RootElement;
        Assert.True(root.TryGetProperty("error", out var err));
        Assert.Equal("Failed to list application users", err.GetString());
    }

    // ── AC-5 — OperationCanceledException MUST propagate (do not sanitize) ───

    [Fact]
    public async Task ExecuteAsync_AlreadyCancelledToken_Throws()
    {
        var tool = new GetApplicationUsersTool(new Mock<IOrganizationService>().Object);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => tool.ExecuteAsync(EmptyInput(), cts.Token));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static JsonElement EmptyInput() =>
        JsonSerializer.Deserialize<JsonElement>("{}");

    private static Entity BuildSystemUserEntity(
        Guid systemUserId,
        string fullName,
        Guid applicationId,
        string? internalEmail)
    {
        var e = new Entity("systemuser") { Id = systemUserId };
        e["systemuserid"] = systemUserId;
        e["fullname"]     = fullName;
        e["applicationid"] = applicationId;
        if (internalEmail is not null)
            e["internalemailaddress"] = internalEmail;
        return e;
    }

    private static EntityCollection BuildRoleCollection(params string[] roleNames)
    {
        var col = new EntityCollection();
        foreach (var name in roleNames)
        {
            var r = new Entity("role") { Id = Guid.NewGuid() };
            r["name"] = name;
            col.Entities.Add(r);
        }
        return col;
    }

    private static bool IsSystemUserQuery(QueryBase q) =>
        q is QueryExpression qx && string.Equals(qx.EntityName, "systemuser", StringComparison.OrdinalIgnoreCase);

    private static bool IsRoleQueryForUser(QueryBase q, Guid userId)
    {
        if (q is not QueryExpression qx) return false;
        if (!string.Equals(qx.EntityName, "role", StringComparison.OrdinalIgnoreCase)) return false;
        // Walk linked entities for a systemuserroles link whose criteria
        // matches the requested systemuserid.
        foreach (var link in qx.LinkEntities)
        {
            if (!string.Equals(link.LinkToEntityName, "systemuserroles", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var cond in link.LinkCriteria.Conditions)
            {
                if (string.Equals(cond.AttributeName, "systemuserid", StringComparison.OrdinalIgnoreCase)
                    && cond.Values.Count == 1
                    && cond.Values[0] is Guid g
                    && g == userId)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
