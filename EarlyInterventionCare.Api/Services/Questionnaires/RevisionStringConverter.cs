using System.Globalization;

namespace EarlyInterventionCare.Api.Services.Questionnaires;

/// <summary>
/// Standalone provisional HTTP revision codec: ASCII "0" or a positive decimal
/// integer without leading zeros, within UInt64 range. Not wired into any API.
/// </summary>
public static class RevisionStringConverter
{
    /// <summary>Rejects noncanonical input without trimming or normalization.</summary>
    public static bool TryParse(string? text, out ulong revision)
    {
        revision = 0;
        if (string.IsNullOrEmpty(text) || text.Length > 20 ||
            (text.Length > 1 && text[0] == '0'))
            return false;

        foreach (var digit in text)
            if (digit is < '0' or > '9')
                return false;

        return ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out revision);
    }

    /// <summary>Formats losslessly as canonical decimal ASCII, regardless of culture.</summary>
    public static string Format(ulong revision) => revision.ToString(CultureInfo.InvariantCulture);
}
