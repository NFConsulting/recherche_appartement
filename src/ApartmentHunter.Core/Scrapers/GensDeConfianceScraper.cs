using AngleSharp.Html.Parser;
using ApartmentHunter.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApartmentHunter.Core.Scrapers;

public class GensDeConfianceScraper(
    HttpClient httpClient,
    IOptions<ScraperOptions> options,
    ILogger<GensDeConfianceScraper> logger) : IListingScraper
{
    private readonly GensDeConfianceOptions _options = options.Value.GensDeConfiance;

    public string SourceName => "GensDeConfiance";
    public bool IsEnabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.SearchUrl);

    public async Task<IReadOnlyList<Listing>> ScrapeAsync(SearchCriteria criteria, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _options.SearchUrl);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            request.Headers.Add("Accept-Language", "fr-FR,fr;q=0.9");

            if (!string.IsNullOrWhiteSpace(_options.SessionCookie))
                request.Headers.Add("Cookie", _options.SessionCookie);

            var response = await httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(ct);

            var parser = new HtmlParser();
            using var document = await parser.ParseDocumentAsync(html, ct);

            // GensDeConfiance : annonces dans des articles ou divs avec data-id
            var cards = document.QuerySelectorAll("article, [data-id], .property-card, .listing-item");

            var listings = new List<Listing>();
            foreach (var card in cards)
            {
                var id = card.GetAttribute("data-id") ?? card.GetAttribute("id") ?? "";
                var titleEl = card.QuerySelector("h2, h3, .property-title, [class*='title']");
                var priceEl = card.QuerySelector("[class*='price'], .rent");
                var urlEl = card.QuerySelector("a[href]");

                var title = titleEl?.TextContent?.Trim() ?? "";
                var price = ListingParserHelpers.ExtractPrice(priceEl?.TextContent ?? "")
                          ?? ListingParserHelpers.ExtractPrice(title);
                var href = urlEl?.GetAttribute("href") ?? "";
                var url = href.StartsWith("http") ? href : $"https://www.gensdeconfiance.fr{href}";

                if (price is null) continue;

                if (string.IsNullOrEmpty(id))
                    id = url.GetHashCode().ToString();

                var listing = new Listing(
                    Id: $"gdc_{id}",
                    Source: SourceName,
                    Title: title,
                    Price: price.Value,
                    Rooms: ListingParserHelpers.ExtractRooms(title) ?? 0,
                    Surface: ListingParserHelpers.ExtractSurface(title),
                    Arrondissement: ListingParserHelpers.ExtractArrondissement(title),
                    Url: url,
                    PublishedAt: DateTime.UtcNow
                );

                if (criteria.Matches(listing))
                    listings.Add(listing);
            }

            return listings;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors du scraping GensDeConfiance");
            return [];
        }
    }
}
