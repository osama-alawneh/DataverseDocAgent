using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DataverseDocAgent.Api.Pipeline;

public sealed record EvidenceComponent(string Id, string Kind, string? ParentId, JsonElement Data);
public sealed record CoverageEntry(string Scope, string Status, string Detail);
public sealed record EvidenceSnapshot(int Version, string RunId, DateTime CreatedUtc,
    IReadOnlyList<EvidenceComponent> Components, IReadOnlyList<CoverageEntry> Coverage);

/// <summary>Versioned, write-once evidence. Hashes detect corruption; they are not a digital signature.</summary>
public static class SnapshotStore
{
    public const int CurrentVersion = 1;
    private const int BatchSize = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private sealed record FileEntry(string Path, string Sha256);
    private sealed record Manifest(int Version, string RunId, DateTime CreatedUtc, int ComponentCount, IReadOnlyList<FileEntry> Files);

    public static async Task SaveAsync(EvidenceSnapshot snapshot, string directory, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Validate(snapshot);
        var destination = Path.GetFullPath(directory);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("Evidence directory already exists; evidence cannot be overwritten.");
        var parent = Path.GetDirectoryName(destination) ?? throw new IOException("Invalid evidence directory.");
        Directory.CreateDirectory(parent);
        // Stage beside the destination and atomically move: concurrent writers cannot merge snapshots.
        var staging = Path.Combine(parent, ".evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var files = new List<FileEntry>();
            async Task WriteAsync<T>(string name, T data)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);
                await using var stream = new FileStream(Path.Combine(staging, name), FileMode.CreateNew, FileAccess.Write);
                await stream.WriteAsync(bytes, ct);
                files.Add(new(name, Convert.ToHexString(SHA256.HashData(bytes))));
            }
            await WriteAsync("coverage.json", snapshot.Coverage);
            var ordered = snapshot.Components.OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
            for (var index = 0; index < ordered.Length; index += BatchSize)
                await WriteAsync($"components-{index / BatchSize:D5}.json", ordered.Skip(index).Take(BatchSize).ToArray());
            await using (var stream = new FileStream(Path.Combine(staging, "manifest.json"), FileMode.CreateNew, FileAccess.Write))
                await JsonSerializer.SerializeAsync(stream, new Manifest(snapshot.Version, snapshot.RunId,
                    snapshot.CreatedUtc, ordered.Length, files), JsonOptions, ct);
            ct.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);
        }
        finally
        {
            // This path is a private GUID sibling created above; never delete the requested destination.
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    public static async Task<EvidenceSnapshot> LoadAsync(string directory, CancellationToken ct = default)
    {
        try
        {
            var root = Path.GetFullPath(directory);
            RejectLink(root);
            var manifestPath = Path.Combine(root, "manifest.json");
            RejectLink(manifestPath);
            var manifest = JsonSerializer.Deserialize<Manifest>(await File.ReadAllBytesAsync(manifestPath, ct), JsonOptions)
                ?? throw new InvalidDataException("Missing evidence manifest.");
            if (manifest.Version != CurrentVersion || manifest.Files is null || manifest.ComponentCount < 0)
                throw new InvalidDataException("Unsupported or invalid evidence manifest.");
            var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var components = new List<EvidenceComponent>();
            IReadOnlyList<CoverageEntry>? coverage = null;
            foreach (var entry in manifest.Files)
            {
                ct.ThrowIfCancellationRequested();
                if (entry is null || string.IsNullOrWhiteSpace(entry.Path) || !seenFiles.Add(entry.Path)
                    || !(entry.Path == "coverage.json" || Regex.IsMatch(entry.Path, @"^components-[0-9]{5}\.json$"))
                    || entry.Sha256 is null || !Regex.IsMatch(entry.Sha256, "^[A-Fa-f0-9]{64}$"))
                    throw new InvalidDataException("Invalid evidence file entry.");
                var path = Path.Combine(root, entry.Path);
                RejectLink(path);
                var bytes = await File.ReadAllBytesAsync(path, ct);
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Evidence hash mismatch.");
                if (entry.Path == "coverage.json")
                    coverage = JsonSerializer.Deserialize<CoverageEntry[]>(bytes, JsonOptions);
                else
                    components.AddRange(JsonSerializer.Deserialize<EvidenceComponent[]>(bytes, JsonOptions)
                        ?? throw new InvalidDataException("Invalid component batch."));
            }
            if (coverage is null || components.Count != manifest.ComponentCount
                || Directory.EnumerateFileSystemEntries(root).Any(p => Path.GetFileName(p) != "manifest.json" && !seenFiles.Contains(Path.GetFileName(p))))
                throw new InvalidDataException("Evidence manifest does not match directory contents.");
            var snapshot = new EvidenceSnapshot(manifest.Version, manifest.RunId, manifest.CreatedUtc,
                Array.AsReadOnly(components.OrderBy(c => c.Id, StringComparer.Ordinal).ToArray()), Array.AsReadOnly(coverage.ToArray()));
            Validate(snapshot);
            return snapshot;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or KeyNotFoundException or NullReferenceException or InvalidOperationException)
        {
            throw new InvalidDataException("Invalid evidence snapshot.");
        }
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Evidence paths must not be links.");
    }

    public static void Validate(EvidenceSnapshot snapshot)
    {
        if (snapshot is null || snapshot.Version != CurrentVersion || string.IsNullOrWhiteSpace(snapshot.RunId)
            || snapshot.CreatedUtc == default || snapshot.CreatedUtc.Kind != DateTimeKind.Utc
            || snapshot.Components is null || snapshot.Coverage is null)
            throw new InvalidDataException("Invalid evidence snapshot header.");
        var byId = new Dictionary<string, EvidenceComponent>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in snapshot.Components)
        {
            ValidateComponent(component);
            if (!byId.TryAdd(component.Id, component)) throw new InvalidDataException("Duplicate evidence component identifier.");
        }
        foreach (var component in snapshot.Components)
        {
            if (component.ParentId is not null && (!byId.TryGetValue(component.ParentId, out var parent)
                || parent.Kind != "table" || parent.Id != component.ParentId))
                throw new InvalidDataException("Missing component parent table.");
        }
        foreach (var entry in snapshot.Coverage)
            if (entry is null || string.IsNullOrWhiteSpace(entry.Scope) || string.IsNullOrWhiteSpace(entry.Detail)
                || entry.Status is not ("complete" or "partial" or "failed" or "excluded" or "limitation"))
                throw new InvalidDataException("Invalid evidence coverage entry.");
    }

    internal static void ValidateComponent(EvidenceComponent component)
    {
        if (component is null || string.IsNullOrWhiteSpace(component.Id) || component.Data.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Invalid evidence component.");
        var data = component.Data;
        if (data.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != data.EnumerateObject().Count())
            throw new InvalidDataException("Duplicate evidence data property.");
        string Required(string key)
        {
            var value = String(data, key);
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Missing component identity or reference.");
            return value;
        }
        string Identifier(string key)
        {
            var value = Required(key);
            if (!Regex.IsMatch(value, "^[A-Za-z0-9_][A-Za-z0-9_.-]*$"))
                throw new InvalidDataException("Invalid metadata identifier.");
            return value;
        }
        void Match(string expected)
        {
            if (component.Id != expected) throw new InvalidDataException("Component identifier does not match its data.");
        }
        switch (component.Kind)
        {
            case "organisation":
                Match("organisation");
                break;
            case "table":
                Match("table:" + Identifier("logicalName"));
                break;
            case "field":
                if (component.ParentId is null || !component.ParentId.StartsWith("table:", StringComparison.Ordinal))
                    throw new InvalidDataException("Missing field parent.");
                Match("field:" + component.ParentId[6..] + ":" + Identifier("logicalName"));
                break;
            case "relationship":
                Match("relationship:" + Identifier("schemaName"));
                if (component.ParentId is null || !component.ParentId.StartsWith("table:", StringComparison.Ordinal))
                    throw new InvalidDataException("Missing relationship parent.");
                var owner = component.ParentId[6..];
                var type = Required("relationshipType");
                var left = type switch { "OneToMany" => Identifier("referencingEntity"), "ManyToMany" => Identifier("entity1LogicalName"), _ => throw new InvalidDataException("Invalid relationship type.") };
                var right = Identifier(type == "OneToMany" ? "referencedEntity" : "entity2LogicalName");
                if (left != owner && right != owner || Required("relatedEntity") != (left == owner ? right : left))
                    throw new InvalidDataException("Relationship endpoints do not match parent and related table.");
                break;
            case "application-user":
                Match("application-user:" + Identifier(String(data, "systemUserId") is not null ? "systemUserId" : "applicationId"));
                if (data.TryGetProperty("roles", out var roles) && roles.ValueKind != JsonValueKind.Null
                    && (roles.ValueKind != JsonValueKind.Array || roles.EnumerateArray().Any(r => r.ValueKind != JsonValueKind.String)))
                    throw new InvalidDataException("Invalid application user roles.");
                break;
            default: throw new InvalidDataException("Unknown evidence component kind.");
        }
        if (component.Kind is not ("field" or "relationship") && component.ParentId is not null)
            throw new InvalidDataException("Unexpected component parent.");
        var allowed = AllowedProperties(component.Kind);
        foreach (var property in data.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal)) throw new InvalidDataException("Unexpected evidence data property.");
            if (property.Name == "roles") continue;
            if (property.Name == "baseLanguageCode")
            {
                if (property.Value.ValueKind != JsonValueKind.Null && (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out _)))
                    throw new InvalidDataException("Invalid language code.");
            }
            else if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new InvalidDataException("Invalid metadata value type.");
        }
    }

    internal static string? String(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;

    internal static string[] AllowedProperties(string kind) => kind switch
    {
        "organisation" => ["environmentName", "environmentUrl", "version", "baseLanguageCode", "baseLanguageName"],
        "table" => ["logicalName", "schemaName", "displayName", "description", "solutionName"],
        "field" => ["logicalName", "displayName", "attributeType", "requiredLevel", "description"],
        "relationship" => ["schemaName", "relationshipType", "relatedEntity", "cascadeDelete", "referencingEntity", "referencedEntity", "referencingAttribute", "entity1LogicalName", "entity2LogicalName"],
        "application-user" => ["systemUserId", "applicationId", "displayName", "email", "roles"],
        _ => []
    };
}
