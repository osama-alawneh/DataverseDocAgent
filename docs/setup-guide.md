# DataverseDocAgent — Service Account Setup Guide

**Audience:** D365 consultants and Power Platform administrators preparing a Dataverse environment for DataverseDocAgent.
**Goal:** Create a locked-down, auditable, revocable service account with exactly the right read-only permissions — and nothing else — in roughly 15 minutes.

This guide assumes no prior experience with the product. Follow the five steps in order.

---

## Prerequisites

Before you begin, make sure you have:

- **Azure portal access** (`portal.azure.com`) with permission to create App Registrations in your tenant's Microsoft Entra ID.
- **Power Platform Admin Centre access** (`admin.powerplatform.microsoft.com`) with System Administrator rights on the target environment.
- **Power Apps Maker Portal access** (`make.powerapps.com`) for the same environment.
- The target **Dataverse environment URL** (e.g. `https://contoso.crm.dynamics.com`).
- The DataverseDocAgent API base URL — referred to as `{API_BASE_URL}` below. If you do not yet have this, contact your DataverseDocAgent point of contact. Replace the placeholder with the real URL in Step 5.
- The security role solution file `DataverseDocAgentSecurityRole_1_0_0_6_managed.zip`, downloaded from `/artefacts/` in the DataverseDocAgent repository.

Total time: ~15 minutes.

---

## Step 1 — Create a Microsoft Entra App Registration

This creates the identity DataverseDocAgent uses to authenticate to Dataverse. It is a service-to-service (daemon) application — no user ever signs in through it.

1. Navigate to **`portal.azure.com`** → **Microsoft Entra ID** → **App Registrations** → **New Registration**.
2. **Name:** `DataverseDocAgent-Reader`
3. **Supported account type:** *Accounts in this organizational directory only*.
4. **Redirect URI:** leave blank. No redirect URI is required for a daemon app.
5. Click **Register**.
6. On the app's Overview page, copy and save:
   - **Application (Client) ID**
   - **Directory (Tenant) ID**
7. In the left menu, go to **Certificates & Secrets** → **Client Secrets** → **New Client Secret**.
   - Description: `DataverseDocAgent`
   - Expiry: choose a value that matches your organisation's secret rotation policy (6, 12, or 24 months).
   - Click **Add**.
8. **Copy the secret `Value` immediately.** Azure only displays it once. If you lose it, you must create a new secret.

You now have three values to keep for Step 5: **Tenant ID**, **Client ID**, **Client Secret**.

---

## Step 2 — Create a Dataverse Application User

This registers the Entra app as a user inside your Dataverse environment so it can be granted a security role.

1. Navigate to **Power Platform Admin Centre** → select your **environment** → **Settings** → **Users** → **Application Users**. (In newer Admin Centre layouts this appears under **Users + permissions** → **Application Users**.)
2. Click **+ New app user**.
3. Click **+ Add an app**, search for the Entra app you created in Step 1 (`DataverseDocAgent-Reader`), select it, and click **Add**.
4. Select the correct **Business Unit** (usually the root business unit for the environment).
5. **Do NOT assign the System Administrator role.** Leave the role list empty for now — you will assign the least-privilege custom role in Step 4.
6. Click **Create**.

The Application User now exists with zero privileges. This is intentional.

---

## Step 3 — Import the DataverseDocAgent Security Role Solution

This imports a managed solution containing a single custom security role, `DataverseDocAgent Reader`, with exactly the read privileges required — nothing more.

1. Download `DataverseDocAgentSecurityRole_1_0_0_6_managed.zip` from the `/artefacts/` folder of the DataverseDocAgent repository.
2. Navigate to **`make.powerapps.com`** → select the same **environment** → **Solutions** → **Import solution**.
3. Click **Browse**, select the `.zip` file, then **Next** → **Import**.
4. Wait for the import to complete (typically under a minute).
5. Confirm the solution **DataverseDocAgentSecurityRole** appears in the Solutions list.
6. Open the solution and verify the role **DataverseDocAgent Reader** is present.

The role is now available in the environment but not yet assigned to any user.

---

## Step 4 — Assign the Role to the Application User

1. Return to **Power Platform Admin Centre** → your **environment** → **Settings** → **Users** → **Application Users**.
2. Find the `DataverseDocAgent-Reader` Application User created in Step 2.
3. Select the user → **Edit security roles** (or **Manage roles** depending on UI version).
4. Tick **DataverseDocAgent Reader**.
5. Click **Save**.

The service account now has exactly the read-only privileges it needs to operate — and no more.

---

## Step 5 — Run the Permission Checker

This is the final verification. The checker connects to your environment using the credentials and confirms the role assignment is correct before you attempt any documentation generation.

Send a `POST` request to the permission checker endpoint:

```
POST {API_BASE_URL}/api/security/check
Content-Type: application/json
```

**Request body:**

```json
{
  "environmentUrl": "https://contoso.crm.dynamics.com",
  "tenantId": "<Directory (Tenant) ID from Step 1>",
  "clientId": "<Application (Client) ID from Step 1>",
  "clientSecret": "<Client Secret value from Step 1>"
}
```

Replace `{API_BASE_URL}` with the DataverseDocAgent API base URL you were given. Replace the environment URL, tenant ID, client ID, and client secret with the values from your environment.

**Field requirements** (a request with bad formatting is rejected with HTTP 400 before the checker runs):

- `environmentUrl` must begin with `https://` — `http://` is rejected.
- `tenantId` and `clientId` must be GUIDs (36 characters, e.g. `11111111-2222-3333-4444-555555555555`). The tenant's vanity domain (for example `contoso.onmicrosoft.com`) is **not** accepted.
- `clientSecret` must be non-empty.

**Expected successful response:**

```json
{
  "status": "ready",
  "safeToRun": true,
  "passed": [
    "Read Entity", "Read Attribute", "Read Relationship",
    "Read PluginAssembly", "Read PluginType", "Read SdkMessageProcessingStep",
    "Read WebResource", "Read Workflow", "Read Role",
    "Read SystemForm", "Read Query", "Read Organization"
  ],
  "missing": [],
  "extra": [],
  "recommendation": "All permissions verified. Safe to run all modes."
}
```

**Only proceed to document generation if `safeToRun: true`.** If `missing[]` is populated, `safeToRun` will be `false` and you must address the missing privileges before continuing. If `extra[]` is populated but `safeToRun` is still `true`, you can run safely — the recommendation advises removing extras to minimise risk surface, but they do not block.

Note on HTTP status: a successful call to the permission checker always returns **HTTP 200**, even when it decides the environment is not ready. All pass/fail information is in the **response body** — specifically the `status` and `safeToRun` fields. Do not filter on HTTP status code.

---

## Troubleshooting

### Request rejected with HTTP 400

**Symptoms:** The call returns HTTP 400 with a validation error instead of reaching the checker.

**Likely cause:** A field in the request body does not match the required format (see *Field requirements* in Step 5).

**Resolution:** Verify `environmentUrl` uses `https://`, that `tenantId` and `clientId` are GUIDs (not vanity domains), and that `clientSecret` is non-empty.

### Response: `status: "blocked"` with a credential-related recommendation

**Symptoms:** HTTP 200 response body contains `status: "blocked"`, `safeToRun: false`, and a `recommendation` that asks you to verify the environment URL, tenant ID, client ID, and client secret.

**Likely cause:** The client secret value was mis-copied, or it has expired.

**Resolution:**

1. Return to **`portal.azure.com`** → **Microsoft Entra ID** → **App Registrations** → open `DataverseDocAgent-Reader`.
2. Go to **Certificates & Secrets**. Check whether your existing secret has expired (column: *Expires*).
3. Click **+ New Client Secret**, create a fresh secret, and copy the `Value` immediately.
4. Retry the `POST /api/security/check` call with the new secret.

### Response: `status: "blocked"` with privileges in `missing[]`

**Symptoms:** HTTP 200 response body contains `status: "blocked"`, `safeToRun: false`, and one or more privileges listed in the `missing` array (for example, `"Read Entity"`).

**Likely cause:** The `DataverseDocAgent Reader` role was not assigned to the Application User in Step 4, or the solution from Step 3 was not imported into the correct environment.

**Resolution:**

1. Confirm you are looking at the correct environment in Power Platform Admin Centre.
2. Repeat **Step 3** to verify the solution is imported in this environment.
3. Repeat **Step 4** to verify the role is ticked for the `DataverseDocAgent-Reader` Application User.
4. Retry the permission checker call.

### Response: `status: "ready"` but `extra[]` is populated

**Symptoms:** HTTP 200 response body contains `status: "ready"`, `safeToRun: true`, and one or more privilege names listed in the `extra` array.

**Likely cause:** The Application User has been assigned additional security roles beyond `DataverseDocAgent Reader` (often System Administrator or a custom role with broader access).

**Resolution:** The tool can run safely as-is, but the least-privilege guarantee is weakened. To minimise risk surface, return to **Power Platform Admin Centre** → **Application Users** → find the `DataverseDocAgent-Reader` user → **Edit security roles**, and untick any role other than `DataverseDocAgent Reader`. Re-run the permission checker to confirm `extra[]` is empty.

### Response: `status: "blocked"` with "Permission check timed out"

**Symptoms:** HTTP 200 response body contains `status: "blocked"`, `safeToRun: false`, and the `recommendation` reports that the permission check timed out.

**Likely cause:** The Dataverse environment is slow, unreachable, or blocked by a network policy. The checker enforces a 9-second upper bound per request.

**Resolution:** Retry the call. If it continues to time out, verify the environment URL is reachable from your network (a browser visit to the URL should return the Dataverse sign-in page), check any outbound firewall or proxy rules, and confirm the environment is not suspended or in a maintenance window.

---

## What Happens Next

Once `safeToRun: true` is returned, your environment is ready. You can now call `POST {API_BASE_URL}/api/document/generate` to produce documentation. See the API reference for full endpoint details.

If you need to revoke DataverseDocAgent access at any point, disable or delete the `DataverseDocAgent-Reader` App Registration in Microsoft Entra ID — this immediately invalidates the service account across every environment it was granted access to.
