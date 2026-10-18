using System.Globalization;
using System.Windows.Media;

namespace GridSketch;

public sealed record FontName(string Source, string DisplayName, string[] Aliases)
{
    public override string ToString() => DisplayName;
    public bool Matches(string name) => string.Equals(Source, name, StringComparison.OrdinalIgnoreCase)
        || Aliases.Any(alias => string.Equals(alias, name, StringComparison.OrdinalIgnoreCase));
}

public static class FontNames
{
    private static readonly Lazy<IReadOnlyList<FontName>> installed = new(() => Fonts.SystemFontFamilies.Select(family =>
    {
        var japanese = family.FamilyNames.FirstOrDefault(pair => pair.Key.IetfLanguageTag.Equals("ja-JP", StringComparison.OrdinalIgnoreCase)).Value
            ?? family.FamilyNames.FirstOrDefault(pair => pair.Key.IetfLanguageTag.Equals("ja", StringComparison.OrdinalIgnoreCase)
                || pair.Key.IetfLanguageTag.StartsWith("ja-", StringComparison.OrdinalIgnoreCase)).Value;
        return new FontName(family.Source, japanese ?? family.Source, family.FamilyNames.Values.ToArray());
    }).OrderBy(font => font.DisplayName, StringComparer.Create(CultureInfo.GetCultureInfo("ja-JP"), true)).ToList());
    public static IReadOnlyList<FontName> Installed => installed.Value;
}
