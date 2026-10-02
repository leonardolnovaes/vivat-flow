using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Tsdt.Api.Documents;

namespace Tsdt.Tests.Documents;

[Trait("Category", "Unit")]
public sealed class LocalDocumentStorageTests
{
    [Fact]
    public async Task Private_storage_uses_generated_identifiers_and_supports_read_and_cleanup()
    {
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        var root = Path.GetFullPath(Path.Combine(temporaryRoot, "vivat-documents-unit-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Throws<InvalidOperationException>(() => new LocalDocumentStorage(
                Options.Create(new DocumentStorageOptions { RootPath = "public" }), new TestEnvironment(root)));
            var storage = new LocalDocumentStorage(Options.Create(new DocumentStorageOptions { RootPath = "private" }), new TestEnvironment(root));
            var organizationId = Guid.NewGuid(); var storageKey = Guid.NewGuid();
            var bytes = "%PDF-test"u8.ToArray();
            await storage.SaveAsync(organizationId, storageKey, new MemoryStream(bytes), CancellationToken.None);
            var saved = Assert.Single(Directory.GetFiles(Path.Combine(root, "private"), "*", SearchOption.AllDirectories));
            Assert.Equal(storageKey.ToString("N"), Path.GetFileName(saved));
            Assert.Equal(organizationId.ToString("N"), Path.GetFileName(Path.GetDirectoryName(saved)));
            await using (var content = await storage.OpenReadAsync(organizationId, storageKey, CancellationToken.None))
            {
                using var copy = new MemoryStream();
                await content.CopyToAsync(copy);
                Assert.Equal(bytes, copy.ToArray());
            }
            await storage.DeleteAsync(organizationId, storageKey, CancellationToken.None);
            Assert.False(File.Exists(saved));
        }
        finally
        {
            if (!root.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected test cleanup path.");
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Tsdt.Tests";
        public string WebRootPath { get; set; } = Path.Combine(root, "public");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
