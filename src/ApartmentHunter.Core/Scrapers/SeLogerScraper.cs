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
    public bool IsEnabled => _options.Enabled;

    public async Task<IReadOnlyList<Listing>> ScrapeAsync(SearchCriteria criteria, CancellationToken ct = default)
    {
        var urls = BuildUrls(criteria).ToList();
        var results = new List<Listing>();

        foreach (var url in urls)
        {
            var listings = await ScrapeOneAsync(url, criteria, ct);
            results.AddRange(listings);
        }

        // Déduplication par ID (même annonce peut apparaître dans plusieurs arrondissements)
        return results.DistinctBy(l => l.Id).ToList();
    }

    private IEnumerable<string> BuildUrls(SearchCriteria criteria)
    {
        if (!string.IsNullOrWhiteSpace(_options.SearchUrl) && !_options.SearchUrl.StartsWith("CONFIGURER"))
        {
            yield return _options.SearchUrl;
            yield break;
        }

        // Pattern SeLoger confirmé : ad09fr{25 + numéro arrondissement}
        // Ex : 10e → ad09fr35, 20e → ad09fr45, 1er → ad09fr26
        foreach (var arr in criteria.Arrondissements)
        {
            var ordinal = arr == 1 ? "1er" : $"{arr}eme";
            var code = 25 + arr;
            yield return $"https://www.seloger.com/recherche/location/appartement/paris-75000/paris-{ordinal}-arrondissement-{75000 + arr}/ad09fr{code}/?price={(int)criteria.PriceMin}%2F{(int)criteria.PriceMax}&rooms={criteria.RoomsMin}";
        }
    }

    private async Task<IReadOnlyList<Listing>> ScrapeOneAsync(string url, SearchCriteria criteria, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            request.Headers.Add("Accept-Language", "fr-FR,fr;q=0.9");

            var response = await httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(ct);
            var nextDataMatch = Regex.Match(html, @"<script id=""__NEXT_DATA__"" type=""application/json"">(.*?)</script>", RegexOptions.Singleline);
            if (!nextDataMatch.Success) return [];

            var json = JsonNode.Parse(nextDataMatch.Groups[1].Value);
            var cards = json?["props"]?["pageProps"]?["initialData"]?["cards"]?.AsArray()
                     ?? json?["props"]?["pageProps"]?["listingsResult"]?["listings"]?.AsArray();
            if (cards is null) return [];

            return cards
                .Select(ParseCard)
                .OfType<Listing>()
                .Where(criteria.Matches)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur SeLoger ({Url})", url);
            return [];
        }
    }

    private Listing? ParseCard(JsonNode? card)
    {
        if (card is null) return null;

        var id = card["id"]?.GetValue<long?>()?.ToString()
              ?? card["listingId"]?.GetValue<string>() ?? "";
        var title = card["title"]?.GetValue<string>() ?? "";
        var price = card["price"]?.GetValue<decimal?>()
                 ?? card["prices"]?["displayedPrice"]?.GetValue<decimal?>();
        if (price is null || price == 0) return null;

        var href = card["permalink"]?.GetValue<string>()
                ?? card["classifiedURL"]?.GetValue<string>() ?? "";
        var url = href.StartsWith("http") ? href : $"https://www.seloger.com{href}";

        var rooms = card["roomsQuantity"]?.GetValue<int?>() ?? card["rooms"]?.GetValue<int?>();
        var surface = card["surface"]?.GetValue<decimal?>();
        var city = card["city"]?.GetValue<string>() ?? card["address"]?.GetValue<string>() ?? "Paris";
        var zipcode = card["zipCode"]?.GetValue<string>() ?? card["postalCode"]?.GetValue<string>();
        var address = card["address"]?.GetValue<string>();

        var arrondissement = !string.IsNullOrEmpty(zipcode) && zipcode.StartsWith("75")
            ? $"Paris {int.Parse(zipcode[^2..])}"
            : ListingParserHelpers.ExtractArrondissement(city);

        return new Listing(
            Id: $"seloger_{id}",
            Source: SourceName,
            Title: title,
            Price: price.Value,
            Rooms: rooms ?? 0,
            Surface: surface,
            Address: string.IsNullOrWhiteSpace(address) ? null : address,
            Arrondissement: arrondissement,
            Url: url,
            PublishedAt: DateTime.UtcNow
        );
    }
}
