using System.Collections.Concurrent;
using Sahada.Infrastructure.Storage;

namespace Sahada.IntegrationTests.Support;

public sealed class MemoryFileStore : IFileStore
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public int Count => _files.Count;

    public Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct)
    {
        _files[key] = content.ToArray();
        return Task.CompletedTask;
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken ct) =>
        Task.FromResult(_files.TryGetValue(key, out var bytes) ? bytes.ToArray() : null);
}
