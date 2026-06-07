using System.Text.Json.Nodes;
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
    private const string CacheKey = "jinka-session";

    public string SourceName => "Jinka";
    public bool IsEnabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.Username);

    public async Task<IReadOnlyList<Listing>> ScrapeAsync(SearchCriteria criteria, CancellationToken ct = default)
    {
        var cookie = await GetSessionAsync(ct);
        if (cookie is null)
        {
            logger.LogWarning("Jinka: impossible d'obtenir une session valide");
            return [];
        }

        var url = string.IsNullOrWhiteSpace(_options.SearchUrl) ? BuildSearchUrl(criteria) : _options.SearchUrl;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            request.Headers.Add("Cookie", cookie);

            var response = await httpClient.SendAsync(request, ct);

            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                // Session expirée → forcer un nouveau login
                SessionCache.Invalidate(CacheKey);
                cookie = await GetSessionAsync(ct);
                if (cookie is null) return [];

                request.Headers.Remove("Cookie");
                request.Headers.Add("Cookie", cookie);
                response = await httpClient.SendAsync(request, ct);
            }

            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(ct);
            return await ParseListingsAsync(html, criteria, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur Jinka ({Url})", url);
            return [];
        }
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
            // Étape 1 : récupérer le token CSRF (pattern Next.js/NextAuth)
            // ⚠️ Si l'endpoint change, le trouver via F12 → Network → chercher le POST de connexion
            var csrfResp = await httpClient.GetAsync("https://www.jinka.fr/api/auth/csrf", ct);
            string? csrfToken = null;
            if (csrfResp.IsSuccessStatusCode)
            {
                var json = JsonNode.Parse(await csrfResp.Content.ReadAsStringAsync(ct));
                csrfToken = json?["csrfToken"]?.GetValue<string>();
            }

            // Étape 2 : login avec credentials
            var loginContent = new FormUrlEncodedContent([
                new("email", _options.Username),
                new("password", _options.Password),
                new("redirect", "false"),
                new("json", "true"),
                .. (csrfToken is not null ? new[] { new KeyValuePair<string, string>("csrfToken", csrfToken) } : [])
            ]);

            using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "https://www.jinka.fr/api/auth/callback/credentials");
            loginRequest.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            loginRequest.Headers.Add("Referer", "https://www.jinka.fr/connexion");
            loginRequest.Content = loginContent;

            var loginResp = await httpClient.SendAsync(loginRequest, ct);

            if (!loginResp.IsSuccessStatusCode)
            {
                logger.LogWarning("Jinka: login échoué ({StatusCode}) — vérifier les credentials", loginResp.StatusCode);
                return null;
            }

            // Extraire le cookie de session
            var cookie = ExtractSessionCookie(loginResp);
            if (cookie is null)
            {
                logger.LogWarning("Jinka: login OK mais aucun cookie de session trouvé");
                return null;
            }

            SessionCache.Set(CacheKey, cookie, TimeSpan.FromHours(23));
            logger.LogInformation("Jinka: session renouvelée");
            return cookie;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Jinka: erreur lors du login");
            return null;
        }
    }

    private static string? ExtractSessionCookie(HttpResponseMessage response)
    {
        var cookies = response.Headers.TryGetValues("Set-Cookie", out var vals) ? vals : [];
        var sessionCookies = cookies
            .Where(c => c.Contains("next-auth") || c.Contains("session") || c.Contains("token"))
            .Select(c => c.Split(';')[0])
            .ToList();

        return sessionCookies.Count > 0 ? string.Join("; ", sessionCookies) : null;
    }

    private static string BuildSearchUrl(SearchCriteria criteria) =>
        $"https://www.jinka.fr/search?transaction=rent&minBudget={criteria.PriceMin}&maxBudget={criteria.PriceMax}&minRooms={criteria.RoomsMin}&maxRooms={criteria.RoomsMax}";

    private async Task<IReadOnlyList<Listing>> ParseListingsAsync(string html, SearchCriteria criteria, CancellationToken ct)
    {
        var parser = new HtmlParser();
        using var document = await parser.ParseDocumentAsync(html, ct);

        var cards = document.QuerySelectorAll("[data-ad-id], .ad-card, article[class*='ad'], .listing-card");
        var listings = new List<Listing>();

        foreach (var card in cards)
        {
            var id = card.GetAttribute("data-ad-id") ?? card.GetAttribute("data-id") ?? "";
            var titleEl = card.QuerySelector("h2, h3, [class*='title']");
            var priceEl = card.QuerySelector("[class*='price']");
            var urlEl = card.QuerySelector("a[href]");
            var surfaceEl = card.QuerySelector("[class*='surface'], [class*='area']");
            var cityEl = card.QuerySelector("[class*='city'], [class*='location']");
            var addressEl = card.QuerySelector("[class*='address'], [class*='adresse']");

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
                Address: addressEl?.TextContent?.Trim(),
                Arrondissement: cityEl?.TextContent?.Trim() ?? ListingParserHelpers.ExtractArrondissement(title),
                Url: url,
                PublishedAt: DateTime.UtcNow
            );

            if (criteria.Matches(listing))
                listings.Add(listing);
        }

        return listings;
    }
}
