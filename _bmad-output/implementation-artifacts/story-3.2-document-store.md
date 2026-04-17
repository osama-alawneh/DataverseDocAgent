# Story 3.2: IDocumentStore — In-Memory Implementation (Phase 1)

Status: review

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

- [x] Define `IDocumentStore` interface (AC: 1, 3)
  - [x] Create `src/DataverseDocAgent.Api/Storage/IDocumentStore.cs`
  - [x] `Task<string> StoreAsync(byte[] documentBytes, TimeSpan ttl)`
  - [x] `Task<byte[]?> RetrieveAsync(string token)`
- [x] Implement `InMemoryDocumentStore` (AC: 2, 5)
  - [x] Create `src/DataverseDocAgent.Api/Storage/InMemoryDocumentStore.cs`
  - [x] Constructor: inject `IMemoryCache`
  - [x] `StoreAsync`:
    - Generate `token = Guid.NewGuid().ToString("N")` (no hyphens, URL-safe)
    - `_cache.Set(token, documentBytes, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl })`
    - Return `token`
  - [x] `RetrieveAsync`:
    - `_cache.TryGetValue(token, out byte[]? bytes)` → return `bytes` (null if not found or expired)
  - [x] Annotate: `// F-040 — FR-040, NFR-013`
- [x] Create `BlobDocumentStore` stub (AC: 3)
  - [x] Create `src/DataverseDocAgent.Api/Storage/BlobDocumentStore.cs` — stub only, `throw new NotImplementedException()`
  - [x] Comment: `// Phase 2+ implementation — replace InMemoryDocumentStore registration in Program.cs`
- [x] Register in DI (AC: 4)
  - [x] In `Program.cs`: `builder.Services.AddMemoryCache()` (if not already registered)
  - [x] `builder.Services.AddSingleton<IDocumentStore, InMemoryDocumentStore>()`
  - [x] Comment above the registration: `// Phase 2+: swap to BlobDocumentStore here`
- [x] Manual test (AC: 2)
  - [x] Unit-test or manual verify: store bytes → retrieve within TTL → returns bytes
  - [x] Store bytes → wait for TTL → retrieve → returns null (or use a short TTL in test)

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

- All five acceptance criteria satisfied. `IDocumentStore` exposes exactly the two asynchronous members specified (AC-1); `InMemoryDocumentStore` delegates to `IMemoryCache` with `AbsoluteExpirationRelativeToNow = ttl` so retention is enforced without a cleanup job, and `RetrieveAsync` returns `null` for both unknown and expired tokens instead of throwing (AC-2); the stubbed `BlobDocumentStore` proves the interface-swap contract so Phase 2 only edits the DI line in `Program.cs` (AC-3); registration is a single `AddSingleton<IDocumentStore, InMemoryDocumentStore>()` alongside `AddMemoryCache()` (AC-4); and the store never inspects document bytes — empty arrays round-trip and null input is rejected with `ArgumentNullException` at the boundary (AC-5).
- Picked `AddSingleton` over `AddScoped` deliberately. `IMemoryCache` is itself a singleton; scoping the store would create a fresh wrapper per HTTP request that still reads the same cache but obscures ownership, and any future store that holds additional state would silently leak it across requests. Spec's Dev Notes call this out explicitly.
- Null-input contract: chose to throw `ArgumentNullException` from `StoreAsync` on null bytes rather than silently swallow. The parameter is typed as non-nullable `byte[]`; a null slip-through would almost certainly indicate an upstream bug in the generation pipeline. Empty arrays still round-trip cleanly, so legitimate zero-length documents are not blocked.
- Test count delta: +7 new tests (baseline 92 → 99). Suite passes `0 Warning(s) 0 Error(s)` on `dotnet build DataverseDocAgent.sln --no-incremental` and `Failed: 0, Passed: 99` on `dotnet test DataverseDocAgent.sln --no-build`.
- NFR-013 audit: `MemoryCacheEntryOptions.AbsoluteExpirationRelativeToNow` is set to the caller-supplied TTL, not `SlidingExpiration`, so the 24-hour cap cannot be reset by repeated `RetrieveAsync` calls. Confirmed by the `RetrieveAsync_AfterTtlElapsed_ReturnsNull` test.
- NFR-007 audit: trivially clean. The store handles opaque `byte[]` payloads and never touches connection strings, secrets, or credential material. No log lines emitted on any code path.

### File List

- Added: `src/DataverseDocAgent.Api/Storage/IDocumentStore.cs`
- Added: `src/DataverseDocAgent.Api/Storage/InMemoryDocumentStore.cs`
- Added: `src/DataverseDocAgent.Api/Storage/BlobDocumentStore.cs`
- Added: `tests/DataverseDocAgent.Tests/InMemoryDocumentStoreTests.cs`
- Modified: `src/DataverseDocAgent.Api/Program.cs`
