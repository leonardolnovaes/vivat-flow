using Microsoft.Extensions.Options;

namespace Tsdt.Api.Documents;

public sealed class DocumentStorageOptions
{
    public const string SectionName = "DocumentStorage";
    public const long MaximumSupportedFileSizeBytes = 20 * 1024 * 1024;
    public required string RootPath { get; set; }
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    public static long ValidateMaxFileSize(long value) => value is > 0 and <= MaximumSupportedFileSizeBytes
        ? value : throw new InvalidOperationException($"DocumentStorage:MaxFileSizeBytes must be between 1 and {MaximumSupportedFileSizeBytes} bytes.");
}

public interface IDocumentStorage
{
    // A failed save must leave no final object. Callers compensate only after a successful save.
    Task SaveAsync(Guid organizationId, Guid storageKey, Stream content, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(Guid organizationId, Guid storageKey, CancellationToken cancellationToken);
    Task DeleteAsync(Guid organizationId, Guid storageKey, CancellationToken cancellationToken);
}

public sealed class LocalDocumentStorage : IDocumentStorage
{
    private readonly string root;

    public LocalDocumentStorage(IOptions<DocumentStorageOptions> options, IWebHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(options.Value.RootPath)) throw new InvalidOperationException("DocumentStorage:RootPath is required.");
        root = Path.GetFullPath(Path.IsPathRooted(options.Value.RootPath)
            ? options.Value.RootPath : Path.Combine(environment.ContentRootPath, options.Value.RootPath));
        if (environment.WebRootPath is { } webRoot &&
            (root.Equals(Path.GetFullPath(webRoot), StringComparison.OrdinalIgnoreCase) ||
             root.StartsWith(Path.GetFullPath(webRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Document storage must be outside the public web root.");
    }

    private string Location(Guid organizationId, Guid storageKey) =>
        Path.Combine(root, organizationId.ToString("N"), storageKey.ToString("N"));

    public async Task SaveAsync(Guid organizationId, Guid storageKey, Stream content, CancellationToken cancellationToken)
    {
        var destination = Location(organizationId, storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
                await content.CopyToAsync(output, cancellationToken);
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public Task<Stream> OpenReadAsync(Guid organizationId, Guid storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(Location(organizationId, storageKey), FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(Guid organizationId, Guid storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(Location(organizationId, storageKey));
        return Task.CompletedTask;
    }
}
