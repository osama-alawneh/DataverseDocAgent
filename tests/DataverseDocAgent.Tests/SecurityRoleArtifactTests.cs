using System.IO.Compression;
using System.Xml.Linq;
using DataverseDocAgent.Api.Features.SecurityCheck;
namespace DataverseDocAgent.Tests;

public class SecurityRoleArtifactTests
{
    [Fact]
    public void PackagedReaderRoleContainsEveryPrivilegeRequiredByGeneration()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DataverseDocAgent.sln"))) root = root.Parent;
        Assert.NotNull(root);
        using var zip = ZipFile.OpenRead(Path.Combine(root!.FullName, "artefacts", "DataverseDocAgentSecurityRole_1_0_0_7_managed.zip"));
        using var customizations = zip.GetEntry("customizations.xml")!.Open();
        var xml = XDocument.Load(customizations);
        var privileges = xml.Descendants("RolePrivilege").ToArray();
        var names = privileges.Select(p => SecurityCheckService.MapPrivilegeName(p.Attribute("name")!.Value)).ToArray();
        foreach (var required in SecurityCheckService.RequiredPrivileges) Assert.Contains(required, names);
        Assert.All(privileges.Where(p => SecurityCheckService.RequiredPrivileges.Contains(
            SecurityCheckService.MapPrivilegeName(p.Attribute("name")!.Value))), p => Assert.Equal("Global", p.Attribute("level")!.Value));
        using var manifest = zip.GetEntry("solution.xml")!.Open();
        Assert.Equal("1.0.0.7", XDocument.Load(manifest).Descendants("Version").Single().Value);
    }
}
