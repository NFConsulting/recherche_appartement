using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ApartmentHunter.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApartmentHunter.Core.Scrapers;

public class LeBonCoinScraper(
    HttpClient httpClient,
    IOptions<ScraperOptions> options,
    ILogger<LeBonCoinScraper> logger) : IListingScraper
{
    private readonly LeBonCoinOptions _options = options.Value.LeBonCoin;

    public string SourceName => "LeBonCoin";
    public bool IsEnabled => _options.Enabled;

    public async Task<IReadOnlyList<Listing>> ScrapeAsync(SearchCriteria criteria, CancellationToken ct = default)
    {
        var url = BuildUrl(criteria);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            request.Headers.Add("Accept", "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");
            request.Headers.Add("Accept-Language", "fr-FR,fr;q=0.9");

            var response = await httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(ct);

            var nextDataMatch = Regex.Match(html, @"<script id=""__NEXT_DATA__"" type=""application/json"">(.*?)</script>", RegexOptions.Singleline);
            if (!nextDataMatch.Success)
            {
                logger.LogWarning("LeBonCoin: __NEXT_DATA__ introuvable");
                return [];
            }

            var json = JsonNode.Parse(nextDataMatch.Groups[1].Value);
            var ads = json?["props"]?["pageProps"]?["searchData"]?["ads"]?.AsArray()
                   ?? json?["props"]?["pageProps"]?["data"]?["ads"]?.AsArray();

            if (ads is null)
            {
                logger.LogWarning("LeBonCoin: structure JSON inattendue");
                return [];
            }

            return ads
                .Select(ParseAd)
                .OfType<Listing>()
                .Where(criteria.Matches)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur LeBonCoin ({Url})", url);
            return [];
        }
    }

    private string BuildUrl(SearchCriteria criteria)
    {
        if (!string.IsNullOrWhiteSpace(_options.SearchUrl) && !_options.SearchUrl.StartsWith("CONFIGURER"))
            return _options.SearchUrl;

        // Format de recherche LeBonCoin multi-arrondissements
        var locs = string.Join(",", criteria.Arrondissements.Select(a => $"Paris_{75000 + a}"));
        return $"https://www.leboncoin.fr/recherche?category=10&locations={Uri.EscapeDataString(locs)}&real_estate_type=2&price={(int)criteria.PriceMin}-{(int)criteria.PriceMax}&rooms={criteria.RoomsMin}-{criteria.RoomsMax}";
    }

    private Listing? ParseAd(JsonNode? ad)
    {
        if (ad is null) return null;

        var id = ad["list_id"]?.GetValue<long?>()?.ToString() ?? ad["id"]?.GetValue<string>() ?? "";
        var title = ad["subject"]?.GetValue<string>() ?? "";
        var priceRaw = ad["price"]?.AsArray().FirstOrDefault()?.GetValue<decimal?>()
                    ?? ad["price_cents"]?.GetValue<long?>() / 100m;
        if (priceRaw is null) return null;

        var href = ad["url"]?.GetValue<string>() ?? $"/locations/{id}.htm";
        var url = href.StartsWith("http") ? href : $"https://www.leboncoin.fr{href}";
        var publishedAt = ad["first_publication_date"]?.GetValue<string>();

        var attrs = ad["attributes"]?.AsArray();
        var rooms = attrs?.FirstOrDefault(a => a?["key"]?.GetValue<string>() == "rooms")?["value"]?.GetValue<string>();
        var surface = attrs?.FirstOrDefault(a => a?["key"]?.GetValue<string>() == "square")?["value"]?.GetValue<string>();

        var location = ad["location"];
        var city = location?["city_label"]?.GetValue<string>()
                ?? location?["city"]?.GetValue<string>() ?? "";
        var zipcode = location?["zipcode"]?.GetValue<string>() ?? "";
        var address = location?["address"]?.GetValue<string>();

        var arrondissement = !string.IsNullOrEmpty(zipcode)
            ? $"Paris {int.Parse(zipcode[^2..])}"
            : ListingParserHelpers.ExtractArrondissement(city);

        return new Listing(
            Id: $"lbc_{id}",
            Source: SourceName,
            Title: title,
            Price: priceRaw.Value,
            Rooms: int.TryParse(rooms, out var r) ? r : ListingParserHelpers.ExtractRooms(title) ?? 0,
            Surface: decimal.TryParse(surface, out var s) ? s : null,
            Address: string.IsNullOrWhiteSpace(address) ? null : address,
            Arrondissement: arrondissement,
            Url: url,
            PublishedAt: DateTime.TryParse(publishedAt, out var dt) ? dt.ToUniversalTime() : DateTime.UtcNow
        );
    }
}
