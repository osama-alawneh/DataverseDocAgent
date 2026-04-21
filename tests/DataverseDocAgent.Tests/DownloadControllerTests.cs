// F-040, FR-040, NFR-013, NFR-014 — Story 3.3 DownloadController unit tests
using System.Reflection;
using DataverseDocAgent.Api.Common;
using DataverseDocAgent.Api.Features.Download;
using DataverseDocAgent.Api.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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

    private static DownloadController NewController(IDocumentStore store)
    {
        // Controller writes Cache-Control on Response.Headers — Response is null until
        // a HttpContext is wired into ControllerContext, so every test that exercises
        // the success path needs a stub HttpContext. Using DefaultHttpContext keeps the
        // test free of Moq plumbing for the response side.
        var controller = new DownloadController(store);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return controller;
    }

    [Fact]
    public async Task Get_ValidToken_ReturnsFileWithDocxHeadersAndBytes()
    {
        var store = NewStore();
        var bytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00 };
        var token = await store.StoreAsync(bytes, TimeSpan.FromMinutes(5));
        var controller = NewController(store);

        var result = await controller.Get(token, CancellationToken.None);

        // File() return → FileContentResult with the docx MIME and the canonical
        // attachment filename. AC-1 pins all three: bytes, content-type, filename.
        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(bytes, file.FileContents);
        Assert.Equal(DocxMime, file.ContentType);
        Assert.Equal(ExpectedFileName, file.FileDownloadName);
    }

    [Fact]
    public async Task Get_ValidToken_SetsCacheControlNoStorePrivate()
    {
        // Token-bearing URL must not be cached by intermediaries (review patch P4).
        var store = NewStore();
        var token = await store.StoreAsync(new byte[] { 1, 2, 3 }, TimeSpan.FromMinutes(5));
        var controller = NewController(store);

        await controller.Get(token, CancellationToken.None);

        Assert.Equal("no-store, private", controller.Response.Headers["Cache-Control"]);
    }

    [Fact]
    public async Task Get_UnknownToken_ReturnsOk200WithStructuredErrorTokenExpired()
    {
        var store = NewStore();
        var controller = NewController(store);

        // 32-char unknown token — passes controller-level shape validation, hits the
        // store, misses, and takes the AC-2 error path.
        var result = await controller.Get(new string('a', 32), CancellationToken.None);

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
        var controller = NewController(store);

        // Wait well past TTL — IMemoryCache evicts lazily on access, so the
        // delay must exceed TTL with margin to avoid CI clock-resolution flake.
        await Task.Delay(TimeSpan.FromMilliseconds(250));

        var result = await controller.Get(token, CancellationToken.None);

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
        // Token is 32 chars so it passes the controller-level shape validation.
        var mock = new Mock<IDocumentStore>(MockBehavior.Strict);
        var bytes = new byte[] { 9, 9, 9 };
        var token = new string('b', 32);
        mock.Setup(s => s.RetrieveAsync(token)).ReturnsAsync(bytes);
        var controller = NewController(mock.Object);

        var result = await controller.Get(token, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(bytes, file.FileContents);
        mock.Verify(s => s.RetrieveAsync(token), Times.Once);
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
        var controller = NewController(store);

        var result = await controller.Get(token, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Empty(file.FileContents);
        Assert.Equal(DocxMime, file.ContentType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task Get_NullEmptyOrWhitespaceToken_ShortCircuitsToTokenExpired(string? token)
    {
        // Review patch P1 — without the controller-level guard, a null/whitespace
        // token reaches IMemoryCache.TryGetValue which throws ArgumentNullException
        // on null and the empty-string branch silently lookups a never-stored key.
        // Strict mock confirms the store is NOT called for these inputs.
        var mock = new Mock<IDocumentStore>(MockBehavior.Strict);
        var controller = NewController(mock.Object);

        var result = await controller.Get(token!, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<StructuredErrorResponse>(ok.Value);
        Assert.Equal("TOKEN_EXPIRED", payload.Code);
        mock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Get_OversizedToken_ShortCircuitsToTokenExpired()
    {
        // Review patch P1 — caps lookup-key length and prevents log/store amplification.
        // 33+ chars is invalid by definition (real tokens are 32-char hex GUIDs).
        var mock = new Mock<IDocumentStore>(MockBehavior.Strict);
        var controller = NewController(mock.Object);

        var result = await controller.Get(new string('x', 4096), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<StructuredErrorResponse>(ok.Value);
        Assert.Equal("TOKEN_EXPIRED", payload.Code);
        mock.VerifyNoOtherCalls();
    }

    [Fact]
    public void Controller_HasAllowAnonymousAndNoAuthorize()
    {
        // AC-3 — token is the sole authorization mechanism. Pin the [AllowAnonymous]
        // attribute so a future global authorization filter wired into Program.cs
        // cannot silently 401 this endpoint. Also assert that no [Authorize] has
        // been added by accident.
        var controllerType = typeof(DownloadController);
        Assert.NotNull(controllerType.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(controllerType.GetCustomAttribute<AuthorizeAttribute>());

        var action = controllerType.GetMethod(nameof(DownloadController.Get));
        Assert.NotNull(action);
        Assert.Null(action!.GetCustomAttribute<AuthorizeAttribute>());
    }
}
