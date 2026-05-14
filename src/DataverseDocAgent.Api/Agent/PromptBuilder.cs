// F-011, F-013 — Mode 1 prompt assembly (Story 3.5)
namespace DataverseDocAgent.Api.Agent;

/// <summary>
/// Builds the Mode 1 prompt that drives Claude through the four tools and
/// instructs it to return a structured JSON document. The prompt is split out
/// from <c>DocumentGenerateService</c> so it can be unit-tested for shape
/// stability — small wording drifts in the prompt change the JSON contract
/// downstream, and breaking those changes silently is worse than a test
/// failure.
/// </summary>
public static class PromptBuilder
{
    /// <summary>
    /// Returns the Mode 1 system prompt. The JSON contract is prescriptive —
    /// <c>DocumentGenerateService.ParseResponse</c> deserializes against this
    /// exact shape, so changes here must be co-evolved with the parser.
    /// </summary>
    public static string BuildMode1Prompt() => """
        You are a Dataverse environment analyst. Your task is to gather a complete
        picture of the connected environment using the available tools, then return
        a single JSON object that describes what you found.

        Steps to follow, in this order:
          1. Call `get_organisation_metadata` once to obtain the environment name,
             URL, version, and base language.
          2. Call `list_custom_tables` once to obtain every custom table's logical
             name and metadata.
          3. For each custom table returned in step 2, call `get_table_fields`
             with that table's logical name to retrieve its custom attributes.
          4. For each custom table returned in step 2, call `get_relationships`
             with that table's logical name to retrieve its custom relationships.
          5. Call `get_application_users` once to retrieve every application user
             (non-human integration principal) registered in the environment.

        Once all tool calls are complete, return your FINAL response as a single
        JSON object only — no surrounding text and no markdown code fences. The
        object MUST have these exact keys:

        {
          "organisation": {
            "environmentName": "<string>",
            "environmentUrl":  "<string|null>",
            "version":         "<string|null>",
            "baseLanguageName":"<string|null>"
          },
          "tables": [
            { "logicalName": "<string>", "displayName": "<string|null>",
              "schemaName": "<string|null>", "solutionName": "<string|null>",
              "description": "<string|null>", "purpose": "<short AI-inferred description>" }
          ],
          "fields": {
            "<tableLogicalName>": [
              { "logicalName": "<string>", "displayName": "<string|null>",
                "attributeType": "<string|null>", "requiredLevel": "<string|null>",
                "description": "<string|null>" }
            ]
          },
          "relationships": {
            "<tableLogicalName>": [
              { "schemaName": "<string|null>", "relationshipType": "<string|null>",
                "relatedEntity": "<string|null>", "cascadeDelete": "<string|null>",
                "businessMeaning": "<short AI-inferred description|null>" }
            ]
          },
          "applicationUsers": [
            { "displayName": "<string|null>", "applicationId": "<string|null>",
              "email": "<string|null>", "roles": ["<role display name>"] }
          ],
          "keyObservations": [
            "<3 to 5 plain-English observations about the environment>"
          ]
        }

        Rules:
          - `purpose` and `businessMeaning` are short AI-inferred descriptions you
            generate from the metadata you observe. Keep each under 200 characters.
          - `keyObservations` must contain between 3 and 5 entries, each a single
            plain-English sentence specific to the data you collected.
          - Do NOT invent a `complexityRating` — it will be computed deterministically
            by the host. Do NOT include `tableCount`, `fieldCount`, or
            `relationshipCount` in your output; they are derived from the arrays you
            return.
          - If a tool returns an `error` field, skip that table for the affected
            section rather than fabricating data.
          - Pass the role array through verbatim — do not redact or summarise role
            names returned by `get_application_users`. If the tool reports the
            sentinel "(role lookup unavailable)" for a user, preserve it exactly.
        """;
}
