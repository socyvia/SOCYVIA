using System.Globalization;
using System.Text;

namespace SOCYVIA.Services;

/// <summary>
/// Normalizes product-authored Arabic interface copy only. Research content must
/// never be passed through this service. It intentionally performs no heuristic
/// byte-repair: persisted research text must be decoded correctly at its source.
/// </summary>
public static class UiTextService
{
    private static readonly char[] TerminalArabicUiPunctuation = ['.', '،', '؛', ':'];

    public static string Localized(string arabic, string english) =>
        LocalizationService.IsArabic ? Arabic(arabic) : english;

    public static string Arabic(string value)
    {
        value = value.Normalize(NormalizationForm.FormC);
        var clean = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark or
                UnicodeCategory.EnclosingMark)
            {
                continue;
            }
            clean.Append(character);
        }

        return clean.ToString()
            .TrimEnd()
            .TrimEnd(TerminalArabicUiPunctuation)
            .TrimEnd();
    }

}
