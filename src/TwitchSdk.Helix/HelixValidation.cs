using System.Text;

namespace TwitchSdk.Helix;

internal static class HelixValidation
{
    public static void Text(string value, int maximum, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (value.EnumerateRunes().Count() > maximum) throw new ArgumentException($"Text may contain at most {maximum} Unicode code points.", parameter);
    }
}
