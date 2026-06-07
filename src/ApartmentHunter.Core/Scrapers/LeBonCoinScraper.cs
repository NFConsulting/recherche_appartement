using System.Text.Json;
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
    public bool IsEnabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.SearchUrl);

    public async Task<IReadOnlyList<Listing>> ScrapeAsync(SearchCriteria criteria, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _options.SearchUrl);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            request.Headers.Add("Accept-Language", "fr-FR,fr;q=0.9");

            var response = await httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(ct);

            // LeBonCoin embeds listings in __NEXT_DATA__ JSON
            var nextDataMatch = Regex.Match(html, @"<script id=""__NEXT_DATA__"" type=""application/json"">(.*?)</script>", RegexOptions.Singleline);
            if (!nextDataMatch.Success)
            {
                logger.LogWarning("LeBonCoin: __NEXT_DATA__ introuvable dans la page");
                return [];
            }

            var json = JsonNode.Parse(nextDataMatch.Groups[1].Value);
            var ads = json?["props"]?["pageProps"]?["searchData"]?["ads"]?.AsArray();

            if (ads is null)
            {
                logger.LogWarning("LeBonCoin: structure JSON inattendue");
                return [];
            }

            return ads
                .Select(ad => ParseAd(ad))
                .OfType<Listing>()
                .Where(criteria.Matches)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors du scraping LeBonCoin");
            return [];
        }
    }

    private Listing? ParseAd(JsonNode? ad)
    {
        if (ad is null) return null;

        var id = ad["list_id"]?.GetValue<long>().ToString() ?? "";
        var title = ad["subject"]?.GetValue<string>() ?? "";
        var url = ad["url"]?.GetValue<string>() ?? $"https://www.leboncoin.fr/annonce/{id}";
        var priceRaw = ad["price"]?.AsArray().FirstOrDefault()?.GetValue<decimal>();
        var publishedAt = ad["first_publication_date"]?.GetValue<string>();

        if (priceRaw is null) return null;

        var attributes = ad["attributes"]?.AsArray();
        var rooms = attributes?.FirstOrDefault(a => a?["key"]?.GetValue<string>() == "rooms")
            ?["value"]?.GetValue<string>();
        var surface = attributes?.FirstOrDefault(a => a?["key"]?.GetValue<string>() == "square")
            ?["value"]?.GetValue<string>();
        var location = ad["location"]?["city"]?.GetValue<string>() ?? "";

        return new Listing(
            Id: $"lbc_{id}",
            Source: SourceName,
            Title: title,
            Price: priceRaw.Value,
            Rooms: int.TryParse(rooms, out var r) ? r : 0,
            Surface: decimal.TryParse(surface, out var s) ? s : null,
            Arrondissement: location,
            Url: url.StartsWith("http") ? url : $"https://www.leboncoin.fr{url}",
            PublishedAt: DateTime.TryParse(publishedAt, out var dt) ? dt.ToUniversalTime() : DateTime.UtcNow
        );
    }
}
