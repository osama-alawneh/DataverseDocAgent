// Story 2.2 — Permission Pre-Flight Checker unit tests
// F-029, F-030, F-031
using DataverseDocAgent.Api.Common;
using DataverseDocAgent.Api.Dataverse;
using DataverseDocAgent.Api.Features.SecurityCheck;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseDocAgent.Tests;

public class SecurityCheckServiceTests
{
    // ── RequiredPrivileges ────────────��───────────────────────────────────────

    [Fact]
    public void RequiredPrivileges_HasExactly13Entries()
    {
        Assert.Equal(13, SecurityCheckService.RequiredPrivileges.Count);
    }

    [Theory]
    [InlineData("Read Entity")]
    [InlineData("Read Attribute")]
    [InlineData("Read Relationship")]
    [InlineData("Read PluginAssembly")]
    [InlineData("Read PluginType")]
    [InlineData("Read SdkMessageProcessingStep")]
    [InlineData("Read WebResource")]
    [InlineData("Read Workflow")]
    [InlineData("Read Role")]
    [InlineData("Read RolePrivilege")]
    [InlineData("Read SystemForm")]
    [InlineData("Read SavedQuery")]
    [InlineData("Read Organization")]
    public void RequiredPrivileges_ContainsExpectedPrivilege(string expected)
    {
        Assert.Contains(expected, SecurityCheckService.RequiredPrivileges);
    }

    // ── MapPrivilegeName ────────────────────────────────────��─────────────────

    [Theory]
    [InlineData("prvReadPluginAssembly", "Read PluginAssembly")]
    [InlineData("prvReadEntity", "Read Entity")]
    [InlineData("prvReadAttribute", "Read Attribute")]
    [InlineData("prvReadRelationship", "Read Relationship")]
    [InlineData("prvReadPluginType", "Read PluginType")]
    [InlineData("prvReadSdkMessageProcessingStep", "Read SdkMessageProcessingStep")]
    [InlineData("prvReadWebResource", "Read WebResource")]
    [InlineData("prvReadWorkflow", "Read Workflow")]
    [InlineData("prvReadRole", "Read Role")]
    [InlineData("prvReadRolePrivilege", "Read RolePrivilege")]
    [InlineData("prvReadSystemForm", "Read SystemForm")]
    [InlineData("prvReadSavedQuery", "Read SavedQuery")]
    [InlineData("prvReadOrganization", "Read Organization")]
    public void MapPrivilegeName_Read_MapsCorrectly(string input, string expected)
    {
        var result = SecurityCheckService.MapPrivilegeName(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("prvWriteContact", "Write Contact")]
    [InlineData("prvCreateAccount", "Create Account")]
    [InlineData("prvDeleteAccount", "Delete Account")]
    [InlineData("prvAppendToContact", "AppendTo Contact")]
    [InlineData("prvAppendContact", "Append Contact")]
    public void MapPrivilegeName_NonRead_MapsCorrectly(string input, string expected)
    {
        var result = SecurityCheckService.MapPrivilegeName(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ReadPlugin")]          // no "prv" prefix
    [InlineData("prv")]                 // prefix only, no action/entity
    [InlineData("prvRead")]             // action with no entity name — boundary case
    public void MapPrivilegeName_Invalid_ReturnsNull(string input)
    {
        var result = SecurityCheckService.MapPrivilegeName(input);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("prvPublishEntity", "PublishEntity")]       // unrecognised action — fallback
    [InlineData("prvExportSolution", "ExportSolution")]     // unrecognised action — fallback
    [InlineData("prvBulkDelete", "BulkDelete")]             // unrecognised action — fallback
    public void MapPrivilegeName_UnrecognizedAction_ReturnsFallback(string input, string expected)
    {
        var result = SecurityCheckService.MapPrivilegeName(input);
        Assert.Equal(expected, result);
    }

    // ── ComputePrivilegeSets ─────────��────────────────────────────────────────

    [Fact]
    public void ComputePrivilegeSets_AllPresent_EmptyMissingAndExtra()
    {
        var (passed, missing, extra) = SecurityCheckService.ComputePrivilegeSets(
            SecurityCheckService.RequiredPrivileges,
            SecurityCheckService.RequiredPrivileges);

        Assert.Equal(13, passed.Length);
        Assert.Empty(missing);
        Assert.Empty(extra);
    }

    [Fact]
    public void ComputePrivilegeSets_OneMissing_DetectedInMissing()
    {
        var userPrivileges = SecurityCheckService.RequiredPrivileges
            .Where(p => p != "Read PluginAssembly")
            .ToList();

        var (passed, missing, extra) = SecurityCheckService.ComputePrivilegeSets(
            userPrivileges,
            SecurityCheckService.RequiredPrivileges);

        Assert.Contains("Read PluginAssembly", missing);
        Assert.DoesNotContain("Read PluginAssembly", passed);
        Assert.Equal(12, passed.Length);
    }

    [Fact]
    public void ComputePrivilegeSets_ExtraPresent_DetectedInExtra()
    {
        var userPrivileges = SecurityCheckService.RequiredPrivileges
            .Concat(["Write Contact", "Delete Account"])
            .ToList();

        var (passed, missing, extra) = SecurityCheckService.ComputePrivilegeSets(
            userPrivileges,
            SecurityCheckService.RequiredPrivileges);

        Assert.Empty(missing);
        Assert.Contains("Write Contact", extra);
        Assert.Contains("Delete Account", extra);
    }

    [Fact]
    public void ComputePrivilegeSets_CaseInsensitive_RequiredMatch()
    {
        // Dataverse might return different casing
        var userPrivileges = SecurityCheckService.RequiredPrivileges
            .Select(p => p.ToUpperInvariant())
            .ToList();

        var (passed, missing, _) = SecurityCheckService.ComputePrivilegeSets(
            userPrivileges,
            SecurityCheckService.RequiredPrivileges);

        Assert.Equal(13, passed.Length);
        Assert.Empty(missing);
        // Verify passed contains original-cased values, not uppercased input
        foreach (var required in SecurityCheckService.RequiredPrivileges)
            Assert.Contains(required, passed);
    }

    // ── BuildRecommendation ────────���──────────────────────────────────────────

    [Fact]
    public void BuildRecommendation_AllClean_ReturnsConfirmation()
    {
        var result = SecurityCheckService.BuildRecommendation([], []);
        Assert.Equal("All permissions verified. Safe to run all modes.", result);
    }

    [Fact]
    public void BuildRecommendation_Missing_ReturnsBlockedMessage()
    {
        var result = SecurityCheckService.BuildRecommendation(
            ["Read PluginAssembly", "Read WebResource"], []);

        Assert.Contains("Cannot run", result);
        Assert.Contains("Read PluginAssembly", result);
        Assert.Contains("Read WebResource", result);
        Assert.Contains("DataverseDocAgent Reader role", result);
    }

    [Fact]
    public void BuildRecommendation_SingularMissing_UsesSingularGrammar()
    {
        var result = SecurityCheckService.BuildRecommendation(["Read PluginAssembly"], []);
        Assert.Contains("1 required permission is missing", result);
    }

    [Fact]
    public void BuildRecommendation_PluralMissing_UsesPluralGrammar()
    {
        var result = SecurityCheckService.BuildRecommendation(
            ["Read PluginAssembly", "Read WebResource"], []);
        Assert.Contains("2 required permissions are missing", result);
    }

    [Fact]
    public void BuildRecommendation_Extra_ReturnsLeastPrivilegeMessage()
    {
        var result = SecurityCheckService.BuildRecommendation([], ["Write Contact"]);

        Assert.Contains("Tool can run safely", result);
        Assert.Contains("Write Contact", result);
        Assert.Contains("minimise risk surface", result);
    }

    [Fact]
    public void BuildRecommendation_MissingTakesPrecedenceOverExtra()
    {
        // When both missing and extra, blocked message wins
        var result = SecurityCheckService.BuildRecommendation(
            ["Read PluginAssembly"], ["Write Contact"]);

        Assert.Contains("Cannot run", result);
        Assert.DoesNotContain("Tool can run safely", result);
    }

    // ── BuildResponse ─────────────────────────────────────────────────────────

    [Fact]
    public void BuildResponse_NoMissingOrExtra_StatusReady_SafeToRunTrue()
    {
        var response = SecurityCheckService.BuildResponse(
            passed: ["Read Entity"],
            missing: [],
            extra: []);

        Assert.Equal("ready", response.Status);
        Assert.True(response.SafeToRun);
    }

    [Fact]
    public void BuildResponse_HasMissing_StatusBlocked_SafeToRunFalse()
    {
        var response = SecurityCheckService.BuildResponse(
            passed: [],
            missing: ["Read PluginAssembly"],
            extra: []);

        Assert.Equal("blocked", response.Status);
        Assert.False(response.SafeToRun);
    }

    [Fact]
    public void BuildResponse_ExtraWithNoMissing_StatusReady_SafeToRunTrue()
    {
        var response = SecurityCheckService.BuildResponse(
            passed: SecurityCheckService.RequiredPrivileges.ToArray(),
            missing: [],
            extra: ["Write Contact"]);

        Assert.Equal("ready", response.Status);
        Assert.True(response.SafeToRun);
    }

    // ── Credential failure (AC-5) ───���─────────────────────────────────────────

    [Fact]
    public void CredentialFailureResponse_HasExpectedShape()
    {
        // Static helper — verifies the blocked/credential-failure response shape
        var response = SecurityCheckService.BuildCredentialFailureResponse();

        Assert.Equal("blocked", response.Status);
        Assert.False(response.SafeToRun);
        Assert.Empty(response.Passed);
        Assert.Empty(response.Missing);
        Assert.Empty(response.Extra);
        Assert.False(string.IsNullOrWhiteSpace(response.Recommendation));
    }

    // ── CheckAsync — credential failure path (AC-5) ──────��───────────────────

    [Fact]
    public async Task CheckAsync_CredentialFailure_ReturnsBlockedResponse()
    {
        var mockFactory = new Mock<IDataverseConnectionFactory>();
        mockFactory
            .Setup(f => f.ConnectAsync(It.IsAny<EnvironmentCredentials>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DataverseConnectionException("auth failed"));

        var service = new SecurityCheckService(
            mockFactory.Object,
            NullLogger<SecurityCheckService>.Instance);

        var request = new SecurityCheckRequest
        {
            EnvironmentUrl = "https://test.crm.dynamics.com",
            TenantId       = "00000000-0000-0000-0000-000000000001",
            ClientId       = "00000000-0000-0000-0000-000000000002",
            ClientSecret   = "test-secret",
        };

        var response = await service.CheckAsync(request);

        Assert.Equal("blocked", response.Status);
        Assert.False(response.SafeToRun);
        Assert.Empty(response.Passed);
        Assert.Empty(response.Missing);
        Assert.Empty(response.Extra);
        Assert.Contains("Verify", response.Recommendation);
    }

    // ── TargetMode default ─���────────────────────────────────────��─────────────

    [Fact]
    public void SecurityCheckRequest_TargetMode_DefaultsToAll()
    {
        var request = new SecurityCheckRequest
        {
            EnvironmentUrl = "https://env.crm.dynamics.com",
            TenantId = "00000000-0000-0000-0000-000000000001",
            ClientId = "00000000-0000-0000-0000-000000000002",
            ClientSecret = "secret",
        };

        Assert.Equal("all", request.TargetMode);
    }
}
