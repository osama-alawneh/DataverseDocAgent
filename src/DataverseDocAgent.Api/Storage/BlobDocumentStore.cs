// F-040, FR-040, NFR-013 — Azure Blob document store stub (Story 3.2, Phase 2+)
namespace DataverseDocAgent.Api.Storage;

/// <summary>
/// Phase 2+ placeholder. The real implementation will upload to Azure Blob Storage
/// (using <c>Azure.Storage.Blobs</c>) and return either the blob name or a short-lived
/// SAS URL as the token. Until then this type exists solely to prove AC-3: swapping
/// Phase 1 → Phase 2 is a single DI line change in <c>Program.cs</c> and no interface
/// change. Not registered in DI; instantiation will fail fast.
/// </summary>
public sealed class BlobDocumentStore : IDocumentStore
{
    // Phase 2+ implementation — replace InMemoryDocumentStore registration in Program.cs.
    public Task<string> StoreAsync(byte[] documentBytes, TimeSpan ttl) =>
        throw new NotImplementedException("BlobDocumentStore is a Phase 2+ stub.");

    public Task<byte[]?> RetrieveAsync(string token) =>
        throw new NotImplementedException("BlobDocumentStore is a Phase 2+ stub.");
}
