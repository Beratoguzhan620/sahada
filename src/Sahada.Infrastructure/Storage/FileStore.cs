using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Sahada.Infrastructure.Storage;

/// <summary>Where photos are kept. The API is the only way in and out; the bucket is never public.</summary>
public interface IFileStore
{
    Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct);

    /// <summary>The bytes, or null when nothing is stored under the key.</summary>
    Task<byte[]?> GetAsync(string key, CancellationToken ct);
}

public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>
    /// "S3" (the default) for an S3-compatible bucket; "Postgres" for a table in the application's database, where
    /// there is no object storage to run next to it.
    /// </summary>
    public string Provider { get; set; } = "S3";

    public bool UsesDatabase => string.Equals(Provider, "Postgres", StringComparison.OrdinalIgnoreCase);

    /// <summary>An S3-compatible endpoint, such as MinIO's.</summary>
    public string ServiceUrl { get; set; } = string.Empty;

    public string Bucket { get; set; } = "sahada";

    public string Region { get; set; } = "us-east-1";

    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }

    /// <summary>Create the bucket at start-up when it is missing; handy locally and in Compose.</summary>
    public bool CreateBucket { get; set; }
}

internal sealed class S3FileStore(IAmazonS3 s3, IOptions<StorageOptions> options) : IFileStore
{
    private readonly string _bucket = options.Value.Bucket;

    public async Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct)
    {
        using var body = new MemoryStream(content, writable: false);
        await s3.PutObjectAsync(new PutObjectRequest { BucketName = _bucket, Key = key, InputStream = body, ContentType = contentType, AutoCloseStream = false }, ct);
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken ct)
    {
        try
        {
            using var response = await s3.GetObjectAsync(_bucket, key, ct);
            using var copy = new MemoryStream();
            await response.ResponseStream.CopyToAsync(copy, ct);
            return copy.ToArray();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task EnsureBucketAsync(CancellationToken ct)
    {
        if (!await AmazonS3Util.DoesS3BucketExistV2Async(s3, _bucket))
        {
            await s3.PutBucketAsync(new PutBucketRequest { BucketName = _bucket }, ct);
        }
    }
}

public static class StorageSetup
{
    /// <param name="connectionString">The application's database, used when Storage:Provider is "Postgres".</param>
    public static IServiceCollection AddSahadaStorage(this IServiceCollection services, IConfiguration configuration, string? connectionString = null)
    {
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.Section))
            .Validate(o => o.UsesDatabase || Uri.TryCreate(o.ServiceUrl, UriKind.Absolute, out _), "Storage:ServiceUrl must be an absolute URL.")
            .Validate(o => o.UsesDatabase || (!string.IsNullOrWhiteSpace(o.AccessKey) && !string.IsNullOrWhiteSpace(o.SecretKey)),
                "Storage:AccessKey and Storage:SecretKey are required. Use 'dotnet user-secrets' or Storage__AccessKey and Storage__SecretKey.")
            .ValidateOnStart();

        if (configuration.GetSection(StorageOptions.Section).Get<StorageOptions>() is { UsesDatabase: true })
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("Storage:Provider \"Postgres\" keeps photos in the application's database, so it needs its connection string.");
            }

            services.AddSingleton(_ => new PostgresFileStore(NpgsqlDataSource.Create(connectionString)));
            services.AddSingleton<IFileStore>(provider => provider.GetRequiredService<PostgresFileStore>());
            return services;
        }

        services.AddSingleton<IAmazonS3>(provider =>
        {
            var o = provider.GetRequiredService<IOptions<StorageOptions>>().Value;
            return new AmazonS3Client(new BasicAWSCredentials(o.AccessKey, o.SecretKey), new AmazonS3Config
            {
                ServiceURL = o.ServiceUrl,
                AuthenticationRegion = o.Region,
                // MinIO serves buckets under the path, not as subdomains.
                ForcePathStyle = true,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
        });
        services.AddSingleton<S3FileStore>();
        services.AddSingleton<IFileStore>(provider => provider.GetRequiredService<S3FileStore>());
        return services;
    }

    /// <summary>Creates the bucket when configured to; a store replaced in tests is left alone.</summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        if (services.GetRequiredService<IFileStore>() is PostgresFileStore table)
        {
            await table.EnsureTableAsync(ct);
            return;
        }

        if (!services.GetRequiredService<IOptions<StorageOptions>>().Value.CreateBucket || services.GetRequiredService<IFileStore>() is not S3FileStore s3)
        {
            return;
        }

        // Object storage may still be starting next to the API (as in Compose): give it a little while.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await s3.EnsureBucketAsync(ct);
                return;
            }
            catch (Exception) when (attempt < 15)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }
}
