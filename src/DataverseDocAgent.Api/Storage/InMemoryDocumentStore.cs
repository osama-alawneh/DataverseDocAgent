// F-040, FR-040, NFR-013 — In-memory document store (Story 3.2, Phase 1)
using Microsoft.Extensions.Caching.Memory;

namespace DataverseDocAgent.Api.Storage;

/// <summary>
/// In-memory <see cref="IDocumentStore"/> backed by <see cref="IMemoryCache"/>.
/// Tokens are 32-char hex GUIDs (<see cref="Guid.NewGuid"/> with the "N" format
/// specifier) — URL-safe and unpredictable enough for a 24-hour download window.
/// Expiry is enforced via <see cref="MemoryCacheEntryOptions.AbsoluteExpirationRelativeToNow"/>,
/// which satisfies NFR-013's retention cap without a background cleanup job.
/// Registered as a singleton so the cache survives across scoped HTTP requests.
/// </summary>
public sealed class InMemoryDocumentStore : IDocumentStore
{
    private readonly IMemoryCache _cache;

    public InMemoryDocumentStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    public Task<string> StoreAsync(byte[] documentBytes, TimeSpan ttl)
    {
        ArgumentNullException.ThrowIfNull(documentBytes);

        // Guid.NewGuid().ToString("N") → 32 hex chars, no hyphens. The "N" format is
        // deliberate: Phase 2's BlobDocumentStore will use the same surface shape for
        // blob names, so the DI swap does not reshape download URLs.
        var token = Guid.NewGuid().ToString("N");

        var options = new MemoryCacheEntryOptions
        {
            // AbsoluteExpirationRelativeToNow, not SlidingExpiration — the 24-hour
            // cap in NFR-013 must not reset on every RetrieveAsync.
            AbsoluteExpirationRelativeToNow = ttl,
        };

        _cache.Set(token, documentBytes, options);
        return Task.FromResult(token);
    }

    public Task<byte[]?> RetrieveAsync(string token)
    {
        // TryGetValue returns false for both unknown and expired entries; both map to null.
        // Never throws for a missing token (IDocumentStore contract, AC-2).
        _cache.TryGetValue(token, out byte[]? bytes);
        return Task.FromResult(bytes);
    }
}
