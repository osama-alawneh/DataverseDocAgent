---
name: dataverse-table-analysis
description: Interpret extracted Dataverse table and field metadata in a bounded documentation stage.
metadata:
  version: 1.0.0
---

# dataverse-table-analysis

Version: 1.0.0

Describe likely business purpose, field meaning, required-level implications, and metadata quality. A logical name or publisher prefix is a clue, not proof of ownership or business intent. Do not infer automation, validation, security, or solution membership from naming alone. For fields, use parent-table context when supplied.

Apply [the evidence and output contract](reference.md). Return one JSON object conforming to [output-schema.json](output-schema.json). The scheduler embeds this skill, reference, and complete stage input in stdin; no tools, app integrations, shell commands, or additional agents are needed.

