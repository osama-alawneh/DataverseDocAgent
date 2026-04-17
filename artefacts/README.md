# DataverseDocAgent Artefacts

## DataverseDocAgentSecurityRole_1_0_0_6_managed.zip

Dataverse **managed** solution containing the **DataverseDocAgent Reader** security role.
This role grants read-only access to exactly 12 metadata entities required by the agent (see [PRD Section 5.4](../docs/prd.md)).

### Privileges Included

| Entity | Privilege | Scope |
|--------|-----------|-------|
| Entity (EntityMetadata) | Read | Organisation |
| Attribute (AttributeMetadata) | Read | Organisation |
| Relationship | Read | Organisation |
| PluginAssembly | Read | Organisation |
| PluginType | Read | Organisation |
| SdkMessageProcessingStep | Read | Organisation |
| WebResource | Read | Organisation |
| Workflow | Read | Organisation |
| Role | Read | Organisation |
| SystemForm | Read | Organisation |
| SavedQuery (View) — privilege `prvReadQuery` | Read | Organisation |
| Organization | Read | Organisation |

### Import Instructions

1. Go to [make.powerapps.com](https://make.powerapps.com) and select your target environment.
2. Navigate to **Solutions** in the left sidebar.
3. Click **Import solution** → **Browse** → select `DataverseDocAgentSecurityRole_1_0_0_6_managed.zip`.
4. Click **Next** → **Import**. Wait for the import to complete.
5. Verify: navigate to **Settings** → **Security Roles** and confirm **DataverseDocAgent Reader** exists.

Re-importing the solution is safe (idempotent) — Dataverse matches by the solution's internal GUID, not the display name, so it updates the existing role without creating duplicates. If you manually created a role named "DataverseDocAgent Reader" outside this solution, both will coexist; delete the manual one before importing.

### After Import

Assign the **DataverseDocAgent Reader** role to your application user, then run `POST /api/security/check` to verify all 12 privileges are detected.

For full setup instructions, see [`docs/setup-guide.md`](../docs/setup-guide.md).
