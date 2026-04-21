// F-001, F-002, F-003 — Story 3.4 Mode 1 tool registration
using Microsoft.Xrm.Sdk;

namespace DataverseDocAgent.Api.Agent.Tools;

/// <summary>
/// Builds the canonical Mode 1 tool set from a per-request <see cref="IOrganizationService"/>.
/// Centralises the registration so the Mode 1 generation handler (Story 3.5) does not need
/// to know which concrete tools belong to Mode 1 — adding or removing a tool is a one-line
/// change here, and the agent loop receives a stable ordering on every call.
/// </summary>
public static class DataverseToolFactory
{
    /// <summary>
    /// Returns the three Mode 1 tools — <c>list_custom_tables</c>, <c>get_table_fields</c>,
    /// <c>get_relationships</c> — bound to the supplied <paramref name="service"/>.
    /// </summary>
    public static IReadOnlyList<IDataverseTool> CreateMode1Tools(IOrganizationService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return new IDataverseTool[]
        {
            new ListCustomTablesTool(service),
            new GetTableFieldsTool(service),
            new GetRelationshipsTool(service),
        };
    }
}
