using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sahada.Infrastructure.Storage;
using Testcontainers.Minio;

namespace Sahada.IntegrationTests;

/// <summary>The S3 store against a real MinIO, the way Compose runs it.</summary>
public sealed class StorageTests : IAsyncLifetime
{
    // MinIO no longer publishes images; this is a maintained build of it, pinned by digest.
    private const string Image = "cgr.dev/chainguard/minio@sha256:3c60a2a8c4d33409a9bc7a623a7fcbfb4605adc3d89422d2f53b3f673a6e5597";

    private readonly MinioContainer _minio = new MinioBuilder(Image).Build();
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _minio.StartAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:ServiceUrl"] = _minio.GetConnectionString(),
            ["Storage:AccessKey"] = _minio.GetAccessKey(),
            ["Storage:SecretKey"] = _minio.GetSecretKey(),
            ["Storage:Bucket"] = "sahada-tests",
            ["Storage:CreateBucket"] = "true",
        }).Build();
        _services = new ServiceCollection().AddSahadaStorage(configuration).BuildServiceProvider();
        await StorageSetup.InitializeAsync(_services);
        // A second start-up finds the bucket and leaves it be.
        await StorageSetup.InitializeAsync(_services);
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _minio.DisposeAsync();
    }

    [Fact]
    public async Task Files_go_in_and_come_back_byte_for_byte()
    {
        var store = _services.GetRequiredService<IFileStore>();
        var bytes = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7)).ToArray();

        await store.PutAsync("companies/a/work-orders/b/photo.jpg", bytes, "image/jpeg", CancellationToken.None);

        Assert.Equal(bytes, await store.GetAsync("companies/a/work-orders/b/photo.jpg", CancellationToken.None));
        Assert.Null(await store.GetAsync("companies/a/missing.jpg", CancellationToken.None));
    }
}
