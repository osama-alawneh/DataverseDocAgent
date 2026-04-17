// F-040 — Story 3.2 InMemoryDocumentStore unit tests
using DataverseDocAgent.Api.Storage;
using Microsoft.Extensions.Caching.Memory;

namespace DataverseDocAgent.Tests;

public class InMemoryDocumentStoreTests
{
    private static InMemoryDocumentStore NewStore() =>
        new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task StoreAsync_ThenRetrieveAsync_ReturnsSameBytes()
    {
        var store = NewStore();
        var bytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 }; // PK.. — .docx/.zip signature

        var token = await store.StoreAsync(bytes, TimeSpan.FromMinutes(1));
        var retrieved = await store.RetrieveAsync(token);

        Assert.NotNull(retrieved);
        Assert.Equal(bytes, retrieved);
    }

    [Fact]
    public async Task StoreAsync_ReturnsTokenAs32CharHexGuid()
    {
        var store = NewStore();

        var token = await store.StoreAsync(new byte[] { 1 }, TimeSpan.FromMinutes(1));

        // Guid.NewGuid().ToString("N") → 32-char hex, no hyphens. Pinning the surface
        // shape so a future refactor to "D" (with hyphens) or a random-string token
        // cannot silently reshape download URLs that Phase 2's blob naming depends on.
        Assert.Equal(32, token.Length);
        Assert.True(Guid.TryParseExact(token, "N", out _));
    }

    [Fact]
    public async Task RetrieveAsync_UnknownToken_ReturnsNull()
    {
        var store = NewStore();

        var result = await store.RetrieveAsync("does-not-exist");

        Assert.Null(result);
    }

    [Fact]
    public async Task RetrieveAsync_AfterTtlElapsed_ReturnsNull()
    {
        var store = NewStore();
        var ttl = TimeSpan.FromMilliseconds(50);

        var token = await store.StoreAsync(new byte[] { 9, 9, 9 }, ttl);

        // Wait well past the TTL. IMemoryCache evicts lazily on access, so TryGetValue
        // is what actually drives expiry — our Task.Delay must exceed TTL by a margin
        // large enough to avoid flake on CI clock resolution, hence ~5× TTL.
        await Task.Delay(TimeSpan.FromMilliseconds(250));

        var result = await store.RetrieveAsync(token);

        Assert.Null(result);
    }

    [Fact]
    public async Task StoreAsync_TwoCalls_YieldDistinctTokens()
    {
        var store = NewStore();

        var t1 = await store.StoreAsync(new byte[] { 1 }, TimeSpan.FromMinutes(1));
        var t2 = await store.StoreAsync(new byte[] { 2 }, TimeSpan.FromMinutes(1));

        Assert.NotEqual(t1, t2);
    }

    [Fact]
    public async Task StoreAsync_EmptyByteArray_RoundTripsWithoutThrowing()
    {
        var store = NewStore();
        var empty = Array.Empty<byte>();

        var token = await store.StoreAsync(empty, TimeSpan.FromMinutes(1));
        var retrieved = await store.RetrieveAsync(token);

        Assert.NotNull(retrieved);
        Assert.Empty(retrieved!);
    }

    [Fact]
    public async Task StoreAsync_NullBytes_Throws()
    {
        var store = NewStore();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => store.StoreAsync(null!, TimeSpan.FromMinutes(1)));
    }

    [Theory]
    [InlineData(0)]                     // TimeSpan.Zero
    [InlineData(-1)]                    // negative
    [InlineData(-TimeSpan.TicksPerDay)] // large negative
    public async Task StoreAsync_NonPositiveTtl_Throws(long ticks)
    {
        var store = NewStore();

        // Pins the boundary check applied in review patch P1 — without it, a
        // caller passing TimeSpan.Zero hit a mysterious ArgumentOutOfRangeException
        // deep inside MemoryCacheEntryOptions, surfacing as a 500.
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.StoreAsync(new byte[] { 1 }, new TimeSpan(ticks)));
    }

    [Fact]
    public async Task StoreAsync_TtlAboveRetentionCap_Throws()
    {
        var store = NewStore();
        var beyondCap = InMemoryDocumentStore.MaxTtl + TimeSpan.FromMinutes(1);

        // NFR-013 — retention cap is enforced at the store boundary so a
        // misconfigured caller cannot silently extend document retention.
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.StoreAsync(new byte[] { 1 }, beyondCap));
    }

    [Fact]
    public async Task StoreAsync_TtlAtRetentionCap_Succeeds()
    {
        var store = NewStore();

        var token = await store.StoreAsync(new byte[] { 1 }, InMemoryDocumentStore.MaxTtl);

        Assert.NotNull(await store.RetrieveAsync(token));
    }
}
