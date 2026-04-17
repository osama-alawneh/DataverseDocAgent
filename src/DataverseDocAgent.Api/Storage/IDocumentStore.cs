// F-040, FR-040, NFR-013 — Generated-document store contract (Story 3.2)
namespace DataverseDocAgent.Api.Storage;

/// <summary>
/// Ephemeral store for generated document bytes keyed by an opaque token.
/// Phase 1 is backed by <see cref="InMemoryDocumentStore"/>; Phase 2+ will
/// swap to a <see cref="BlobDocumentStore"/> registration. Implementations
/// must honour the supplied TTL absolutely — documents older than the TTL
/// must never be returned (NFR-013 retention cap).
/// </summary>
public interface IDocumentStore
{
    /// <summary>
    /// Stores <paramref name="documentBytes"/> under a freshly generated
    /// token with an absolute expiry of <paramref name="ttl"/> and returns
    /// the token. The store does not inspect or transform the bytes.
    /// </summary>
    Task<string> StoreAsync(byte[] documentBytes, TimeSpan ttl);

    /// <summary>
    /// Returns the bytes previously stored under <paramref name="token"/>,
    /// or <c>null</c> if the token is unknown or its TTL has elapsed.
    /// Never throws for a missing token.
    /// </summary>
    Task<byte[]?> RetrieveAsync(string token);
}
