# Sanitized evidence fixture

`sanitized-snapshot/` is a version 1 evidence snapshot produced by `SnapshotStore.SaveAsync` and verified with `LoadAsync`.
It contains one demonstration organisation, two custom tables (`demo_project`, `demo_task`), three fields, one shared relationship, and one application user. All names and identifiers are fabricated; no credentials or live Dataverse data are included.

The snapshot includes explicit extraction coverage and limitations. Its SHA-256 manifest detects changed evidence files; it is an integrity check, not an authenticity signature. Keep additional files outside the snapshot directory. Treat the snapshot as immutable and create a new directory for different evidence.
