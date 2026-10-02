using Tsdt.Api.Documents;

namespace Tsdt.Tests.Documents;

internal sealed class TestDocumentStorage : IDocumentStorage
{
    private readonly Dictionary<(Guid OrganizationId, Guid StorageKey), byte[]> files = [];
    public bool FailAfterSave { get; set; }
    public int SavedCount => files.Count;

    public async Task SaveAsync(Guid organizationId, Guid storageKey, Stream content, CancellationToken cancellationToken)
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        files[(organizationId, storageKey)] = copy.ToArray();
        if (FailAfterSave)
        {
            files.Remove((organizationId, storageKey));
            throw new IOException("Simulated storage failure.");
        }
    }

    public Task<Stream> OpenReadAsync(Guid organizationId, Guid storageKey, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(new MemoryStream(files[(organizationId, storageKey)]));

    public Task DeleteAsync(Guid organizationId, Guid storageKey, CancellationToken cancellationToken)
    {
        files.Remove((organizationId, storageKey));
        return Task.CompletedTask;
    }
}
