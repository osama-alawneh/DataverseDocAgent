---
name: dataverse-relationship-analysis
description: Interpret extracted Dataverse relationship direction and lookup metadata in a bounded documentation stage.
metadata:
  version: 1.0.0
---

# dataverse-relationship-analysis

Version: 1.0.0

Explain the relationship using referencingEntity, referencedEntity, and referencingAttribute for OneToMany metadata. A lookup resides on the referencing entity. Do not reverse this based on the queried parent table. For ManyToMany use entity1LogicalName and entity2LogicalName without inventing a lookup. Distinguish cascadeDelete metadata from inferred lifecycle intent. relatedEntity is query-relative; use the authoritative endpoints.

Apply [the evidence and output contract](reference.md). Return one JSON object conforming to [output-schema.json](output-schema.json). The scheduler embeds this skill, reference, and complete stage input in stdin; no tools, app integrations, shell commands, or additional agents are needed.

