using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Xml;
using ApartmentHunter.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApartmentHunter.Core.Scrapers;

public class PapScraper(
    HttpClient httpClient,
    IOptions<ScraperOptions> options,
    ILogger<PapScraper> logger) : IListingScraper
{
    private readonly PapOptions _options = options.Value.Pap;

    public string SourceName => "PAP";
    public bool IsEnabled => _options.Enabled;

    public async Task<IReadOnlyList<Listing>> ScrapeAsync(SearchCriteria criteria, CancellationToken ct = default)
    {
        var url = BuildUrl(criteria);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            var response = await httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = XmlReader.Create(stream);
            var feed = SyndicationFeed.Load(reader);

            return feed.Items
                .Select(ParseItem)
                .OfType<Listing>()
                .Where(criteria.Matches)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur PAP ({Url})", url);
            return [];
        }
    }

    private string BuildUrl(SearchCriteria criteria)
    {
        if (!string.IsNullOrWhiteSpace(_options.RssUrl) && !_options.RssUrl.StartsWith("CONFIGURER"))
            return _options.RssUrl;

        // URL RSS PAP : tout Paris filtré par prix/pièces, filtre arrondissement côté client
        var rooms = criteria.RoomsMin;
        return $"https://www.pap.fr/annonce/locations-appartement-paris-75-g439-a-{rooms}-pieces_{rooms}?loyer_min={(int)criteria.PriceMin}&loyer_max={(int)criteria.PriceMax}&rss=1";
    }

    private Listing? ParseItem(SyndicationItem item)
    {
        var title = item.Title?.Text ?? "";
        var description = item.Summary?.Text ?? "";
        var url = item.Links.FirstOrDefault()?.Uri?.ToString() ?? "";
        var id = $"pap_{item.Id ?? url}";

        var price = ListingParserHelpers.ExtractPrice(title)
                 ?? ListingParserHelpers.ExtractPrice(description);
        if (price is null) return null;

        return new Listing(
            Id: id,
            Source: SourceName,
            Title: title,
            Price: price.Value,
            Rooms: ListingParserHelpers.ExtractRooms(title) ?? 0,
            Surface: ListingParserHelpers.ExtractSurface(title) ?? ListingParserHelpers.ExtractSurface(description),
            Address: ExtractAddress(description),
            Arrondissement: ListingParserHelpers.ExtractArrondissement(title + " " + description),
            Url: url,
            PublishedAt: item.PublishDate.UtcDateTime
        );
    }

    // PAP met souvent l'adresse dans la description sous forme "Adresse : X" ou après le titre
    private static readonly Regex AddressRegex = new(@"(?:adresse\s*:\s*|situé[e]?\s+(?:rue|bd|avenue|allée|place|impasse|villa)\s+)([^\n<,]{5,60})", RegexOptions.IgnoreCase);
    private static readonly Regex StreetRegex = new(@"\b\d+[,\s]+(?:rue|boulevard|avenue|allée|place|impasse|villa|passage|square)\s+[A-ZÀ-Ü][a-zA-ZÀ-ü\s\-']{3,40}", RegexOptions.IgnoreCase);

    private static string? ExtractAddress(string text)
    {
        var m = AddressRegex.Match(text);
        if (m.Success) return m.Groups[1].Value.Trim();
        m = StreetRegex.Match(text);
        return m.Success ? m.Value.Trim() : null;
    }
}
