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

    // ── Code-review patches: broadened fault catches + edge guards ──────────

    // P10 — per-user role-lookup fault sentinel must surface for ALL fault
    // types in the catch filter, not only FaultException.
    public static IEnumerable<object[]> PerUserFaultTypes => new[]
    {
        new object[] { new FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { Message = "fault" }) },
        new object[] { new TimeoutException("timeout") },
        new object[] { new CommunicationException("wcf channel fault") },
        new object[] { new System.Net.Http.HttpRequestException("dns") },
        // P3 — broadened catch must ALSO surface sentinel for unexpected
        // exception types (InvalidOperationException, ArgumentException) so
        // per-user isolation holds against unknown SDK shapes.
        new object[] { new InvalidOperationException("unexpected") },
        new object[] { new ArgumentException("unexpected") },
    };

    [Theory]
    [MemberData(nameof(PerUserFaultTypes))]
    public async Task ExecuteAsync_PerUserRoleLookupFault_AllFaultTypes_SurfaceSentinel(Exception fault)
    {
        var userId = Guid.NewGuid();
        var appId  = Guid.NewGuid();
        var systemUsers = new EntityCollection
        {
            Entities = { BuildSystemUserEntity(userId, "Faulty App", appId, null) },
        };

        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q => IsSystemUserQuery(q))))
           .Returns(systemUsers);
        svc.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q => IsRoleQueryForUser(q, userId))))
           .Throws(fault);

        var tool = new GetApplicationUsersTool(svc.Object);
        var json = await tool.ExecuteAsync(EmptyInput());

        var users    = JsonDocument.Parse(json).RootElement.GetProperty("applicationUsers");
        Assert.Equal(1, users.GetArrayLength());
        var roles = users[0].GetProperty("roles").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Single(roles);
        Assert.Equal(GetApplicationUsersTool.RoleLookupUnavailableSentinel, roles[0]);
    }

    // P11 — outer (initial systemuser query) fault must surface the structured
    // error for ALL fault types, not only FaultException + Timeout.
    public static IEnumerable<object[]> OuterFaultTypes => new[]
    {
        new object[] { new FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { Message = "fault" }) },
        new object[] { new TimeoutException("timeout") },
        new object[] { new CommunicationException("wcf channel fault") },
        new object[] { new System.Net.Http.HttpRequestException("dns") },
        new object[] { new InvalidOperationException("unexpected") },
    };

    [Theory]
    [MemberData(nameof(OuterFaultTypes))]
    public async Task ExecuteAsync_OuterFault_AllFaultTypes_ReturnStructuredError(Exception fault)
    {
        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Throws(fault);

        var tool = new GetApplicationUsersTool(svc.Object);
        var json = await tool.ExecuteAsync(EmptyInput());

        var root = JsonDocument.Parse(json).RootElement;
        Assert.True(root.TryGetProperty("error", out var err));
        Assert.Equal("Failed to list application users", err.GetString());
    }

    // P12 — null EntityCollection on the initial systemuser query is the
    // documented `result?.Entities is null` guard; pin it against regression.
    [Fact]
    public async Task ExecuteAsync_RetrieveMultipleReturnsNull_ReturnsEmptyArray()
    {
        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>()))
           .Returns((EntityCollection)null!);

        var tool = new GetApplicationUsersTool(svc.Object);
        var json = await tool.ExecuteAsync(EmptyInput());

        var root = JsonDocument.Parse(json).RootElement;
        Assert.True(root.TryGetProperty("applicationUsers", out var users));
        Assert.Equal(0, users.GetArrayLength());
        Assert.False(root.TryGetProperty("error", out _));
    }

    // P1 — null Entity element inside the EntityCollection must be skipped,
    // not NRE'd. Both the outer user list and the inner role list paths are
    // covered.
    [Fact]
    public async Task ExecuteAsync_NullUserEntityInCollection_IsSilentlySkipped()
    {
        var goodId = Guid.NewGuid();
        var appId  = Guid.NewGuid();
        var systemUsers = new EntityCollection();
        // Inject null first, then a real user. The null must not abort the
        // enumeration or leak as the error contract.
        systemUsers.Entities.Add(null!);
        systemUsers.Entities.Add(BuildSystemUserEntity(goodId, "Survivor App", appId, null));

        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q => IsSystemUserQuery(q))))
           .Returns(systemUsers);
        svc.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q => IsRoleQueryForUser(q, goodId))))
           .Returns(BuildRoleCollection("Reader"));

        var tool = new GetApplicationUsersTool(svc.Object);
        var json = await tool.ExecuteAsync(EmptyInput());

        var users = JsonDocument.Parse(json).RootElement.GetProperty("applicationUsers");
        Assert.Equal(1, users.GetArrayLength());
        Assert.Equal("Survivor App", users[0].GetProperty("displayName").GetString());
    }

    // P5 — whitespace-only role name attribute is filtered at the tool
    // boundary so the renderer never sees " " entries.
    [Fact]
    public async Task ExecuteAsync_RoleEntityWhitespaceName_IsDroppedFromRolesArray()
    {
        var userId = Guid.NewGuid();
        var appId  = Guid.NewGuid();
        var systemUsers = new EntityCollection
        {
            Entities = { BuildSystemUserEntity(userId, "Whitespace App", appId, null) },
        };

        var roleResult = new EntityCollection();
        var whitespaceRole = new Entity("role") { Id = Guid.NewGuid() };
        whitespaceRole["name"] = "   ";
        var realRole = new Entity("role") { Id = Guid.NewGuid() };
        realRole["name"] = "Reader";
        roleResult.Entities.Add(whitespaceRole);
        roleResult.Entities.Add(realRole);

        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q => IsSystemUserQuery(q))))
           .Returns(systemUsers);
        svc.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q => IsRoleQueryForUser(q, userId))))
           .Returns(roleResult);

        var tool = new GetApplicationUsersTool(svc.Object);
        var json = await tool.ExecuteAsync(EmptyInput());

        var roles = JsonDocument.Parse(json).RootElement
            .GetProperty("applicationUsers")[0]
            .GetProperty("roles")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();
        Assert.Single(roles);
        Assert.Equal("Reader", roles[0]);
    }

    // P4 — applicationid surfaces in three SDK shapes (Guid / string /
    // EntityReference); pin the round-trip for each.
    [Fact]
    public async Task ExecuteAsync_ApplicationIdAsString_IsRoundTripped()
    {
        var userId = Guid.NewGuid();
        const string raw = "ABCDEF12-1234-5678-9012-3456789ABCDE";
        var e = new Entity("systemuser") { Id = userId };
        e["systemuserid"]  = userId;
        e["fullname"]      = "String App";
        e["applicationid"] = raw;

        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q => IsSystemUserQuery(q))))
           .Returns(new EntityCollection { Entities = { e } });
        svc.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q => IsRoleQueryForUser(q, userId))))
           .Returns(BuildRoleCollection("Reader"));

        var tool = new GetApplicationUsersTool(svc.Object);
        var json = await tool.ExecuteAsync(EmptyInput());

        var user = JsonDocument.Parse(json).RootElement.GetProperty("applicationUsers")[0];
        Assert.Equal(raw, user.GetProperty("applicationId").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ApplicationIdAsEntityReference_UnwrapsToGuid()
    {
        var userId = Guid.NewGuid();
        var inner  = Guid.NewGuid();
        var e = new Entity("systemuser") { Id = userId };
        e["systemuserid"]  = userId;
        e["fullname"]      = "Ref App";
        e["applicationid"] = new EntityReference("application", inner);

        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q => IsSystemUserQuery(q))))
           .Returns(new EntityCollection { Entities = { e } });
        svc.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q => IsRoleQueryForUser(q, userId))))
           .Returns(BuildRoleCollection("Reader"));

        var tool = new GetApplicationUsersTool(svc.Object);
        var json = await tool.ExecuteAsync(EmptyInput());

        var user = JsonDocument.Parse(json).RootElement.GetProperty("applicationUsers")[0];
        Assert.Equal(inner.ToString(), user.GetProperty("applicationId").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ApplicationIdAsEmptyGuid_IsDroppedFromJson()
    {
        var userId = Guid.NewGuid();
        var e = new Entity("systemuser") { Id = userId };
        e["systemuserid"]  = userId;
        e["fullname"]      = "Zero App";
        e["applicationid"] = Guid.Empty;

        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q => IsSystemUserQuery(q))))
           .Returns(new EntityCollection { Entities = { e } });
        svc.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q => IsRoleQueryForUser(q, userId))))
           .Returns(BuildRoleCollection("Reader"));

        var tool = new GetApplicationUsersTool(svc.Object);
        var json = await tool.ExecuteAsync(EmptyInput());

        var user = JsonDocument.Parse(json).RootElement.GetProperty("applicationUsers")[0];
        // Guid.Empty collapses to null and the WhenWritingNull policy drops
        // the key from the JSON.
        Assert.False(user.TryGetProperty("applicationId", out _));
    }

    // P13 — mid-iteration cancellation: token cancels between the initial
    // RetrieveMultiple returning users and the first per-user role lookup.
    [Fact]
    public async Task ExecuteAsync_CancellationBetweenUsers_PropagatesOCE()
    {
        var userIdA = Guid.NewGuid();
        var userIdB = Guid.NewGuid();
        var systemUsers = new EntityCollection
        {
            Entities =
            {
                BuildSystemUserEntity(userIdA, "First",  Guid.NewGuid(), null),
                BuildSystemUserEntity(userIdB, "Second", Guid.NewGuid(), null),
            },
        };

        using var cts = new CancellationTokenSource();
        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q => IsSystemUserQuery(q))))
           .Returns(systemUsers);
        // First user's role query returns successfully but cancels the token
        // before the foreach moves to the second user — the next loop
        // iteration's ThrowIfCancellationRequested must short-circuit.
        svc.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q => IsRoleQueryForUser(q, userIdA))))
           .Callback(() => cts.Cancel())
           .Returns(BuildRoleCollection("Reader"));

        var tool = new GetApplicationUsersTool(svc.Object);

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
