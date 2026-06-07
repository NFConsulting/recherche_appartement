using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ApartmentHunter.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApartmentHunter.Core.Scrapers;

public class SeLogerScraper(
    HttpClient httpClient,
    IOptions<ScraperOptions> options,
    ILogger<SeLogerScraper> logger) : IListingScraper
{
    private readonly SeLogerOptions _options = options.Value.SeLoger;

    public string SourceName => "SeLoger";
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

            // SeLoger embeds listings in window.__REDIAL_PROPS__ ou __NEXT_DATA__
            var nextDataMatch = Regex.Match(html, @"<script id=""__NEXT_DATA__"" type=""application/json"">(.*?)</script>", RegexOptions.Singleline);
            if (!nextDataMatch.Success)
            {
                logger.LogWarning("SeLoger: __NEXT_DATA__ introuvable");
                return [];
            }

            var json = JsonNode.Parse(nextDataMatch.Groups[1].Value);
            // SeLoger structure: props.pageProps.initialData.cards ou listingsResult
            var cards = json?["props"]?["pageProps"]?["initialData"]?["cards"]?.AsArray()
                     ?? json?["props"]?["pageProps"]?["listingsResult"]?["listings"]?.AsArray();

            if (cards is null)
            {
                logger.LogWarning("SeLoger: aucune annonce trouvée dans le JSON");
                return [];
            }

            return cards
                .Select(ParseCard)
                .OfType<Listing>()
                .Where(criteria.Matches)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors du scraping SeLoger");
            return [];
        }
    }

    private Listing? ParseCard(JsonNode? card)
    {
        if (card is null) return null;

        var id = card["id"]?.GetValue<long>().ToString()
              ?? card["listingId"]?.GetValue<string>()
              ?? "";
        var title = card["title"]?.GetValue<string>() ?? "";
        var price = card["price"]?.GetValue<decimal?>()
                 ?? card["prices"]?["displayedPrice"]?.GetValue<decimal?>();
        var url = card["permalink"]?.GetValue<string>()
               ?? card["classifiedURL"]?.GetValue<string>() ?? "";
        var rooms = card["roomsQuantity"]?.GetValue<int?>()
                 ?? card["rooms"]?.GetValue<int?>();
        var surface = card["surface"]?.GetValue<decimal?>();
        var city = card["city"]?.GetValue<string>()
                ?? card["address"]?.GetValue<string>() ?? "Paris";

        if (price is null || price == 0) return null;

        return new Listing(
            Id: $"seloger_{id}",
            Source: SourceName,
            Title: title,
            Price: price.Value,
            Rooms: rooms ?? 0,
            Surface: surface,
            Arrondissement: city,
            Url: url.StartsWith("http") ? url : $"https://www.seloger.com{url}",
            PublishedAt: DateTime.UtcNow
        );
    }
}
