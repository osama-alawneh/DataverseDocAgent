# POC Baseline

This document is created in Story 1.2 and completed in Story 1.4.

---

## Credential Handling Code Review

The following checklist confirms the credential-handling contract defined in
PRD §5.2 (FR-034) and Architecture §6 prior to any API surface being exposed.
Items are verified in Story 1.4 as part of the POC baseline gate.

- [ ] (a) No credential value (`ClientSecret`, `ClientId`, `TenantId`) appears in any console
       output, log entry, or exception message at any point during the test run.
- [ ] (b) `ClientSecret` is not passed as a raw string outside the `EnvironmentCredentials`
       wrapper (i.e., it is only accessed via `credentials.ClientSecret` inside
       `DataverseConnectionFactory.ConnectAsync`).
- [ ] (c) `EnvironmentCredentials` is not serialized via `JsonSerializer`, `ToString()`,
       or any other mechanism that would enumerate its property values.
