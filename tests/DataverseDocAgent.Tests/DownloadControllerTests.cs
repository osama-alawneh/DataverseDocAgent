// F-040, FR-040, NFR-014 — Story 3.3 DownloadController unit tests
using DataverseDocAgent.Api.Common;
using DataverseDocAgent.Api.Features.Download;
using DataverseDocAgent.Api.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace DataverseDocAgent.Tests;

public class DownloadControllerTests
{
    private const string DocxMime =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private const string ExpectedFileName = "DataverseDocAgent-Report.docx";

    private static InMemoryDocumentStore NewStore() =>
        new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task Get_ValidToken_ReturnsFileWithDocxHeadersAndBytes()
    {
        var store = NewStore();
        var bytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00 };
        var token = await store.StoreAsync(bytes, TimeSpan.FromMinutes(5));
        var controller = new DownloadController(store);

        var result = await controller.Get(token);

        // File() return → FileContentResult with the docx MIME and the canonical
        // attachment filename. AC-1 pins all three: bytes, content-type, filename.
        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(bytes, file.FileContents);
        Assert.Equal(DocxMime, file.ContentType);
        Assert.Equal(ExpectedFileName, file.FileDownloadName);
    }

    [Fact]
    public async Task Get_UnknownToken_ReturnsOk200WithStructuredErrorTokenExpired()
    {
        var store = NewStore();
        var controller = new DownloadController(store);

        var result = await controller.Get("does-not-exist");

        // AC-2: HTTP 200 (NOT 404) with structured error in body. Mirrors the
        // JobStatusController contract — a missing/expired token is encoded in
        // the body so HTTP client libraries do not throw on 4xx.
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
        var payload = Assert.IsType<StructuredErrorResponse>(ok.Value);
        Assert.Equal("Download token not found or expired", payload.Error);
        Assert.Equal("TOKEN_EXPIRED", payload.Code);
        Assert.False(payload.SafeToRetry);
    }

    [Fact]
    public async Task Get_ExpiredToken_ReturnsOk200WithStructuredErrorTokenExpired()
    {
        var store = NewStore();
        var token = await store.StoreAsync(new byte[] { 1 }, TimeSpan.FromMilliseconds(50));
        var controller = new DownloadController(store);

        // Wait well past TTL — IMemoryCache evicts lazily on access, so the
        // delay must exceed TTL with margin to avoid CI clock-resolution flake.
        await Task.Delay(TimeSpan.FromMilliseconds(250));

        var result = await controller.Get(token);

        // Expired tokens take the same body shape as unknown tokens — the caller
        // cannot distinguish "never existed" from "expired" by design (NFR-014).
        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<StructuredErrorResponse>(ok.Value);
        Assert.Equal("TOKEN_EXPIRED", payload.Code);
    }

    [Fact]
    public async Task Get_DelegatesEntirelyToDocumentStoreRetrieve()
    {
        // AC-4 — controller does not implement its own storage. The mock asserts
        // exactly one RetrieveAsync call with the supplied token; if the controller
        // ever grows a side cache, in-process lookup, or fallback path, this fails.
        var mock = new Mock<IDocumentStore>(MockBehavior.Strict);
        var bytes = new byte[] { 9, 9, 9 };
        mock.Setup(s => s.RetrieveAsync("tok-abc")).ReturnsAsync(bytes);
        var controller = new DownloadController(mock.Object);

        var result = await controller.Get("tok-abc");

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(bytes, file.FileContents);
        mock.Verify(s => s.RetrieveAsync("tok-abc"), Times.Once);
        mock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Get_EmptyBytesStored_ReturnsFileWithEmptyBody()
    {
        // Defensive: an upstream pipeline bug could store an empty buffer. The
        // controller must still deliver a well-formed file response rather than
        // collapsing into the error path — debugging is easier when the empty
        // file makes it through.
        var store = NewStore();
        var token = await store.StoreAsync(Array.Empty<byte>(), TimeSpan.FromMinutes(1));
        var controller = new DownloadController(store);

        var result = await controller.Get(token);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Empty(file.FileContents);
        Assert.Equal(DocxMime, file.ContentType);
    }
}
