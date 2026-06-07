using System.ServiceModel.Syndication;
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
    public bool IsEnabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.RssUrl);

    public async Task<IReadOnlyList<Listing>> ScrapeAsync(SearchCriteria criteria, CancellationToken ct = default)
    {
        try
        {
            var stream = await httpClient.GetStreamAsync(_options.RssUrl, ct);
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
            logger.LogError(ex, "Erreur lors du scraping PAP");
            return [];
        }
    }

    private Listing? ParseItem(SyndicationItem item)
    {
        var title = item.Title?.Text ?? "";
        var url = item.Links.FirstOrDefault()?.Uri?.ToString() ?? "";
        var id = $"pap_{item.Id ?? url}";

        var price = ListingParserHelpers.ExtractPrice(title)
            ?? ListingParserHelpers.ExtractPrice(item.Summary?.Text ?? "");

        if (price is null) return null;

        return new Listing(
            Id: id,
            Source: SourceName,
            Title: title,
            Price: price.Value,
            Rooms: ListingParserHelpers.ExtractRooms(title) ?? 0,
            Surface: ListingParserHelpers.ExtractSurface(title),
            Arrondissement: ListingParserHelpers.ExtractArrondissement(title + " " + (item.Summary?.Text ?? "")),
            Url: url,
            PublishedAt: item.PublishDate.UtcDateTime
        );
    }
}
