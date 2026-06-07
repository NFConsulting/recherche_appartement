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
    private const string CacheKey = "gdc-session";

    public string SourceName => "GensDeConfiance";
    public bool IsEnabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.Username);

    public async Task<IReadOnlyList<Listing>> ScrapeAsync(SearchCriteria criteria, CancellationToken ct = default)
    {
        var cookie = await GetSessionAsync(ct);
        if (cookie is null)
        {
            logger.LogWarning("GensDeConfiance: impossible d'obtenir une session valide");
            return [];
        }

        var urls = BuildUrls(criteria).ToList();
        var results = new List<Listing>();

        foreach (var url in urls)
        {
            var listings = await ScrapeOneAsync(url, cookie, criteria, ct);

            // Session expirée en cours de scraping → re-login une fois
            if (listings is null)
            {
                SessionCache.Invalidate(CacheKey);
                cookie = await GetSessionAsync(ct);
                if (cookie is null) break;
                listings = await ScrapeOneAsync(url, cookie, criteria, ct) ?? [];
            }

            results.AddRange(listings);
        }

        return results.DistinctBy(l => l.Id).ToList();
    }

    // ──────────────────────────────────────────────
    // Login automatique — session cachée 23h
    // ──────────────────────────────────────────────

    private async Task<string?> GetSessionAsync(CancellationToken ct)
    {
        var cached = SessionCache.Get(CacheKey);
        if (cached is not null) return cached;

        return await LoginAsync(ct);
    }

    private async Task<string?> LoginAsync(CancellationToken ct)
    {
        try
        {
            // Étape 1 : GET la page de login pour récupérer le token CSRF
            // ⚠️ Si l'endpoint change, le trouver via F12 → Network → chercher le POST de connexion sur gensdeconfiance.com
            using var getReq = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/fr/login");
            getReq.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            var getResp = await httpClient.SendAsync(getReq, ct);
            var getHtml = await getResp.Content.ReadAsStringAsync(ct);

            // Extraire le token CSRF depuis le formulaire
            var csrfToken = ExtractCsrfToken(getHtml);
            var initialCookies = ExtractCookies(getResp);

            // Étape 2 : POST login
            var formData = new List<KeyValuePair<string, string>>
            {
                new("_username", _options.Username),
                new("_password", _options.Password),
                new("_remember_me", "on"),
            };
            if (csrfToken is not null)
                formData.Add(new("_csrf_token", csrfToken));

            using var loginReq = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/fr/login");
            loginReq.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            loginReq.Headers.Add("Referer", $"{BaseUrl}/fr/login");
            if (initialCookies is not null) loginReq.Headers.Add("Cookie", initialCookies);
            loginReq.Content = new FormUrlEncodedContent(formData);

            var loginResp = await httpClient.SendAsync(loginReq, ct);

            var cookie = ExtractCookies(loginResp);
            if (string.IsNullOrWhiteSpace(cookie))
            {
                logger.LogWarning("GensDeConfiance: login échoué — vérifier les credentials ou l'URL de login");
                return null;
            }

            // Combine cookies initiaux et de session
            var fullCookie = string.Join("; ", new[] { initialCookies, cookie }.Where(c => !string.IsNullOrEmpty(c)));
            SessionCache.Set(CacheKey, fullCookie, TimeSpan.FromHours(23));
            logger.LogInformation("GensDeConfiance: session renouvelée");
            return fullCookie;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GensDeConfiance: erreur lors du login");
            return null;
        }
    }

    private static string? ExtractCsrfToken(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            html,
            @"<input[^>]*name=""_csrf_token""[^>]*value=""([^""]+)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractCookies(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        var parts = cookies
            .Select(c => c.Split(';')[0])
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToList();
        return parts.Count > 0 ? string.Join("; ", parts) : null;
    }

    // ──────────────────────────────────────────────
    // Scraping des annonces
    // ──────────────────────────────────────────────

    private IEnumerable<string> BuildUrls(SearchCriteria criteria)
    {
        if (!string.IsNullOrWhiteSpace(_options.SearchUrl) && !_options.SearchUrl.StartsWith("CONFIGURER"))
        {
            yield return _options.SearchUrl;
            yield break;
        }
        foreach (var arr in criteria.Arrondissements)
            yield return $"{BaseUrl}/fr/sc/paris-{75000 + arr}/immobilier/locations-immobilieres/appartement";
    }

    // Retourne null si la session est expirée (401/403), liste vide si aucune annonce
    private async Task<IReadOnlyList<Listing>?> ScrapeOneAsync(string url, string cookie, SearchCriteria criteria, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            request.Headers.Add("Cookie", cookie);

            var response = await httpClient.SendAsync(request, ct);

            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                return null;

            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(ct);

            var parser = new HtmlParser();
            using var document = await parser.ParseDocumentAsync(html, ct);

            var cards = document.QuerySelectorAll("article, [data-id], .property-card, [class*='ad-card']");
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
                var fullUrl = href.StartsWith("http") ? href : $"{BaseUrl}{href}";

                if (price is null) continue;
                if (string.IsNullOrEmpty(id)) id = fullUrl.GetHashCode().ToString();

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
                    Url: fullUrl,
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
