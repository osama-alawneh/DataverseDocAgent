# Story 3.2: IDocumentStore — In-Memory Implementation (Phase 1)

Status: ready-for-dev

## Story

As a developer,
I want an `InMemoryDocumentStore` that stores generated .docx bytes with a 24-hour TTL,
so that the download endpoint can retrieve documents by token without any external storage dependency in Phase 1.

## Acceptance Criteria

1. `IDocumentStore` interface is defined with: `Task<string> StoreAsync(byte[] documentBytes, TimeSpan ttl)` (returns a token) and `Task<byte[]?> RetrieveAsync(string token)` (returns null for expired/invalid tokens).
2. `InMemoryDocumentStore` implements `IDocumentStore` using `IMemoryCache`. `StoreAsync` stores the bytes under a UUID token with an absolute expiry equal to `ttl`. `RetrieveAsync` returns `null` (not an exception) for expired or unknown tokens.
3. Switching from `InMemoryDocumentStore` to a future `BlobDocumentStore` (Phase 2) requires only a single DI registration change in `Program.cs` — no other code changes.
4. `InMemoryDocumentStore` is registered as the `IDocumentStore` implementation in `Program.cs` for Phase 1.
5. The store accepts any byte array — it does not inspect or transform the document content.

## Tasks / Subtasks

- [ ] Define `IDocumentStore` interface (AC: 1, 3)
  - [ ] Create `src/DataverseDocAgent.Api/Storage/IDocumentStore.cs`
  - [ ] `Task<string> StoreAsync(byte[] documentBytes, TimeSpan ttl)`
  - [ ] `Task<byte[]?> RetrieveAsync(string token)`
- [ ] Implement `InMemoryDocumentStore` (AC: 2, 5)
  - [ ] Create `src/DataverseDocAgent.Api/Storage/InMemoryDocumentStore.cs`
  - [ ] Constructor: inject `IMemoryCache`
  - [ ] `StoreAsync`:
    - Generate `token = Guid.NewGuid().ToString("N")` (no hyphens, URL-safe)
    - `_cache.Set(token, documentBytes, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl })`
    - Return `token`
  - [ ] `RetrieveAsync`:
    - `_cache.TryGetValue(token, out byte[]? bytes)` → return `bytes` (null if not found or expired)
  - [ ] Annotate: `// F-040 — FR-040, NFR-013`
- [ ] Create `BlobDocumentStore` stub (AC: 3)
  - [ ] Create `src/DataverseDocAgent.Api/Storage/BlobDocumentStore.cs` — stub only, `throw new NotImplementedException()`
  - [ ] Comment: `// Phase 2+ implementation — replace InMemoryDocumentStore registration in Program.cs`
- [ ] Register in DI (AC: 4)
  - [ ] In `Program.cs`: `builder.Services.AddMemoryCache()` (if not already registered)
  - [ ] `builder.Services.AddSingleton<IDocumentStore, InMemoryDocumentStore>()`
  - [ ] Comment above the registration: `// Phase 2+: swap to BlobDocumentStore here`
- [ ] Manual test (AC: 2)
  - [ ] Unit-test or manual verify: store bytes → retrieve within TTL → returns bytes
  - [ ] Store bytes → wait for TTL → retrieve → returns null (or use a short TTL in test)

## Dev Notes

- **Token format:** `Guid.NewGuid().ToString("N")` produces a 32-character hex string without hyphens (e.g., `a1b2c3d4e5f6...`). This is URL-safe and unpredictable enough for the 24-hour token use case.
- **`IMemoryCache` size limits:** For MVP, do not set a size limit on the cache. With 3 concurrent requests (NFR-011) generating .docx files, memory usage is negligible. A typical Mode 1 .docx is unlikely to exceed a few MB.
- **`AddSingleton` vs `AddScoped`:** `InMemoryDocumentStore` must be `AddSingleton` because `IMemoryCache` is a singleton. Using `AddScoped` would create a new store instance per request that doesn't share the cache state.
- **Phase 2 swap:** The `BlobDocumentStore` will use `Azure.Storage.Blobs` and will either stream the blob directly or generate a short-lived SAS URL. The `IDocumentStore` interface is designed to accommodate both approaches — `RetrieveAsync` returns raw bytes (stream from blob), and in Phase 2, `StoreAsync` will upload to blob storage and return the blob name as the token (the SAS URL is generated at retrieval time).
- **NFR-013 compliance:** `IMemoryCache` absolute expiry handles the "24-hour deletion" requirement automatically. No cleanup job is needed for Phase 1.

### Project Structure Notes

Files created:
- `src/DataverseDocAgent.Api/Storage/IDocumentStore.cs`
- `src/DataverseDocAgent.Api/Storage/InMemoryDocumentStore.cs` — `// F-040`
- `src/DataverseDocAgent.Api/Storage/BlobDocumentStore.cs` — stub only

Modified:
- `src/DataverseDocAgent.Api/Program.cs` — DI registration

### References

- [Source: _bmad-output/planning-artifacts/architecture.md#7-storage-abstraction] — IDocumentStore interface definition
- [Source: docs/prd.md#functional-requirements — FR-040] — token lifetime and expiry behaviour
- [Source: docs/prd.md#7-non-functional-requirements — NFR-013] — document retention prohibition

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

### File List
