using System.Runtime.CompilerServices;

namespace TwitchSdk.Core;

public sealed class Pagination
{
    public string? Cursor { get; init; }
}

public sealed class HelixPage<T>
{
    public IReadOnlyList<T> Data { get; init => field = value ?? []; } = [];
    public Pagination? Pagination { get; init; }
    public int? Total { get; init; }
}

public static class HelixPagination
{
    public static async IAsyncEnumerable<T> EnumerateAsync<T>(
        Func<string?, CancellationToken, Task<HelixPage<T>>> fetchPage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fetchPage);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await fetchPage(cursor, cancellationToken).ConfigureAwait(false);
            foreach (var item in page.Data) { cancellationToken.ThrowIfCancellationRequested(); yield return item; }
            cursor = page.Pagination?.Cursor;
            if (!string.IsNullOrEmpty(cursor) && !seen.Add(cursor)) throw new InvalidOperationException("Twitch returned a repeated pagination cursor.");
        } while (!string.IsNullOrEmpty(cursor));
    }
}
