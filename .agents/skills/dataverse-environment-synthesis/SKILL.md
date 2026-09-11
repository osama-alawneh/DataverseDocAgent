---
name: dataverse-environment-synthesis
description: Synthesize bounded Dataverse evidence and validated prior findings for a documentation report.
metadata:
  version: 1.0.0
---

# dataverse-environment-synthesis

Version: 1.0.0

Connect the supplied targets to likely business processes and application-user roles. Prior findings remain interpretations; cross-check them against supplied evidence. Do not create a global completeness claim from this bounded batch. Identify missing context, contradictions, and excluded coverage. Describe automation only when directly supported; the current inventory does not extract plugins, flows, or JavaScript.

Apply [the evidence and output contract](reference.md). Return one JSON object conforming to [output-schema.json](output-schema.json). The scheduler embeds this skill, reference, and complete stage input in stdin; no tools, app integrations, shell commands, or additional agents are needed.

