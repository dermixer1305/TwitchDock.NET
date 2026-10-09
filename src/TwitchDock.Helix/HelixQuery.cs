using System.Globalization;

namespace TwitchDock.Helix;

internal sealed class HelixQuery : List<KeyValuePair<string, string?>>
{
    public HelixQuery AddValue(string name, string? value) { if (value is not null) Add(new(name, value)); return this; }
    public HelixQuery AddValue(string name, int? value) => AddValue(name, value?.ToString(CultureInfo.InvariantCulture));
    public HelixQuery AddValue(string name, bool? value) => AddValue(name, value?.ToString().ToLowerInvariant());
    public HelixQuery AddValue(string name, decimal? value) => AddValue(name, value?.ToString(CultureInfo.InvariantCulture));
    public HelixQuery AddValue(string name, DateTimeOffset? value) => AddValue(name, value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
    public HelixQuery AddValues(string name, IReadOnlyList<string> values, int maximum = 100)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count > maximum) throw new ArgumentException($"At most {maximum} values are allowed for {name}.", nameof(values));
        foreach (var value in values) { ArgumentException.ThrowIfNullOrWhiteSpace(value); Add(new(name, value)); }
        return this;
    }
    public HelixQuery AddPage(int? first, string? after, string? before = null)
    {
        if (first is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(first), "Page size must be between 1 and 100.");
        if (after is not null && before is not null) throw new ArgumentException("Use either before or after.");
        return AddValue("first", first).AddValue("after", after).AddValue("before", before);
    }
}
