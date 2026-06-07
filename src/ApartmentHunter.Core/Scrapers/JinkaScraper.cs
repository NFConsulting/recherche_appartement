using AngleSharp;
using AngleSharp.Html.Parser;
using ApartmentHunter.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApartmentHunter.Core.Scrapers;

public class JinkaScraper(
    HttpClient httpClient,
    IOptions<ScraperOptions> options,
    ILogger<JinkaScraper> logger) : IListingScraper
{
    private readonly JinkaOptions _options = options.Value.Jinka;

    public string SourceName => "Jinka";
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

            // Jinka affiche les annonces dans des cards avec data-ad-id
            var cards = document.QuerySelectorAll("[data-ad-id], .ad-card, .listing-card, article[class*='ad']");

            var listings = new List<Listing>();
            foreach (var card in cards)
            {
                var id = card.GetAttribute("data-ad-id") ?? card.GetAttribute("data-id") ?? "";
                var titleEl = card.QuerySelector("h2, h3, .title, [class*='title']");
                var priceEl = card.QuerySelector("[class*='price'], .price");
                var urlEl = card.QuerySelector("a[href]");
                var surfaceEl = card.QuerySelector("[class*='surface'], [class*='area']");
                var cityEl = card.QuerySelector("[class*='city'], [class*='location']");

                var title = titleEl?.TextContent?.Trim() ?? "";
                var price = ListingParserHelpers.ExtractPrice(priceEl?.TextContent ?? title);
                var href = urlEl?.GetAttribute("href") ?? "";
                var url = href.StartsWith("http") ? href : $"https://www.jinka.fr{href}";

                if (price is null || string.IsNullOrEmpty(id)) continue;

                var listing = new Listing(
                    Id: $"jinka_{id}",
                    Source: SourceName,
                    Title: title,
                    Price: price.Value,
                    Rooms: ListingParserHelpers.ExtractRooms(title) ?? 0,
                    Surface: ListingParserHelpers.ExtractSurface(surfaceEl?.TextContent ?? title),
                    Arrondissement: cityEl?.TextContent?.Trim() ?? ListingParserHelpers.ExtractArrondissement(title),
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
            logger.LogError(ex, "Erreur lors du scraping Jinka");
            return [];
        }
    }
}
