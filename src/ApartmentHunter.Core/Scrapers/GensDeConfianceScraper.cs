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
    private const string BaseUrl = "https://gensdeconfiance.com";

    public string SourceName => "GensDeConfiance";
    // Nécessite un cookie de session valide
    public bool IsEnabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.SessionCookie);

    public async Task<IReadOnlyList<Listing>> ScrapeAsync(SearchCriteria criteria, CancellationToken ct = default)
    {
        var urls = BuildUrls(criteria).ToList();
        var results = new List<Listing>();

        foreach (var url in urls)
        {
            var listings = await ScrapeOneAsync(url, criteria, ct);
            results.AddRange(listings);
        }

        return results.DistinctBy(l => l.Id).ToList();
    }

    private IEnumerable<string> BuildUrls(SearchCriteria criteria)
    {
        if (!string.IsNullOrWhiteSpace(_options.SearchUrl) && !_options.SearchUrl.StartsWith("CONFIGURER"))
        {
            yield return _options.SearchUrl;
            yield break;
        }

        // Pattern GDC confirmé : /fr/sc/paris-{code_postal}/immobilier/locations-immobilieres/appartement
        foreach (var arr in criteria.Arrondissements)
            yield return $"{BaseUrl}/fr/sc/paris-{75000 + arr}/immobilier/locations-immobilieres/appartement";
    }

    private async Task<IReadOnlyList<Listing>> ScrapeOneAsync(string url, SearchCriteria criteria, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            request.Headers.Add("Accept-Language", "fr-FR,fr;q=0.9");
            request.Headers.Add("Cookie", _options.SessionCookie);

            var response = await httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(ct);
            var parser = new HtmlParser();
            using var document = await parser.ParseDocumentAsync(html, ct);

            var cards = document.QuerySelectorAll("article, [data-id], .property-card, .listing-item, [class*='ad-card']");
            var listings = new List<Listing>();

            foreach (var card in cards)
            {
                var id = card.GetAttribute("data-id") ?? card.GetAttribute("id") ?? "";
                var titleEl = card.QuerySelector("h2, h3, [class*='title']");
                var priceEl = card.QuerySelector("[class*='price'], .rent");
                var urlEl = card.QuerySelector("a[href]");
                var surfaceEl = card.QuerySelector("[class*='surface']");
                var addressEl = card.QuerySelector("[class*='address'], [class*='adresse'], [class*='location']");

                var title = titleEl?.TextContent?.Trim() ?? "";
                var price = ListingParserHelpers.ExtractPrice(priceEl?.TextContent ?? "")
                          ?? ListingParserHelpers.ExtractPrice(title);
                var href = urlEl?.GetAttribute("href") ?? "";
                var urlFull = href.StartsWith("http") ? href : $"{BaseUrl}{href}";

                if (price is null) continue;
                if (string.IsNullOrEmpty(id)) id = urlFull.GetHashCode().ToString();

                // Extraire l'arrondissement depuis l'URL de scraping courante
                var arrFromUrl = ListingParserHelpers.ExtractArrondissement(url);
                var arrFromContent = ListingParserHelpers.ExtractArrondissement(title + " " + (addressEl?.TextContent ?? ""));
                var arrondissement = arrFromContent != "Paris" ? arrFromContent : arrFromUrl;

                var listing = new Listing(
                    Id: $"gdc_{id}",
                    Source: SourceName,
                    Title: title,
                    Price: price.Value,
                    Rooms: ListingParserHelpers.ExtractRooms(title) ?? 0,
                    Surface: ListingParserHelpers.ExtractSurface(surfaceEl?.TextContent ?? title),
                    Address: addressEl?.TextContent?.Trim(),
                    Arrondissement: arrondissement,
                    Url: urlFull,
                    PublishedAt: DateTime.UtcNow
                );

                if (criteria.Matches(listing))
                    listings.Add(listing);
            }

            return listings;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur GensDeConfiance ({Url})", url);
            return [];
        }
    }
}
