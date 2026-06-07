using System.Text.RegularExpressions;

namespace ApartmentHunter.Core.Scrapers;

internal static partial class ListingParserHelpers
{
    [GeneratedRegex(@"(\d[\d\s ]*)\s*€", RegexOptions.IgnoreCase)]
    private static partial Regex PriceRegex();

    [GeneratedRegex(@"(\d+)\s*pi[eè]ces?", RegexOptions.IgnoreCase)]
    private static partial Regex RoomsRegex();

    [GeneratedRegex(@"(\d+(?:[,.]\d+)?)\s*m[²2]", RegexOptions.IgnoreCase)]
    private static partial Regex SurfaceRegex();

    [GeneratedRegex(@"Paris\s+(\d{1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex ArrondissementRegex();

    public static decimal? ExtractPrice(string text)
    {
        var match = PriceRegex().Match(text);
        if (!match.Success) return null;
        var cleaned = match.Groups[1].Value.Replace(" ", "").Replace(" ", "");
        return decimal.TryParse(cleaned, out var price) ? price : null;
    }

    public static int? ExtractRooms(string text)
    {
        var match = RoomsRegex().Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value, out var r) ? r : null;
    }

    public static decimal? ExtractSurface(string text)
    {
        var match = SurfaceRegex().Match(text);
        if (!match.Success) return null;
        var value = match.Groups[1].Value.Replace(',', '.');
        return decimal.TryParse(value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : null;
    }

    public static string ExtractArrondissement(string text)
    {
        var match = ArrondissementRegex().Match(text);
        return match.Success ? $"Paris {match.Groups[1].Value}e" : "Paris";
    }
}
