using ApartmentHunter.Core.Models;
using ApartmentHunter.Core.Scrapers;
using ApartmentHunter.Infrastructure.Sms;
using ApartmentHunter.Infrastructure.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApartmentHunter.Functions.Functions;

public class ApartmentCheckFunction(
    IEnumerable<IListingScraper> scrapers,
    ISeenListingsRepository seenListings,
    ISmsService smsService,
    IOptions<SearchCriteria> criteriaOptions,
    ILogger<ApartmentCheckFunction> logger)
{
    private readonly SearchCriteria _criteria = criteriaOptions.Value;

    // Toutes les 15 minutes, 7h-23h
    [Function(nameof(ApartmentCheckFunction))]
    public async Task Run([TimerTrigger("0 */15 7-23 * * *")] TimerInfo timer, CancellationToken ct)
    {
        logger.LogInformation("Vérification des annonces — {Time}", DateTimeOffset.Now);

        var enabledScrapers = scrapers.Where(s => s.IsEnabled).ToList();
        if (enabledScrapers.Count == 0)
        {
            logger.LogWarning("Aucun scraper activé. PAP/LeBonCoin/SeLoger sont actifs par défaut. Jinka/GDC nécessitent un cookie de session.");
            return;
        }

        var tasks = enabledScrapers.Select(s => s.ScrapeAsync(_criteria, ct));
        var results = await Task.WhenAll(tasks);
        var allListings = results.SelectMany(x => x).ToList();

        logger.LogInformation("{Count} annonce(s) trouvée(s) au total sur {Scrapers} source(s)",
            allListings.Count, enabledScrapers.Count);

        foreach (var listing in allListings)
        {
            if (await seenListings.HasBeenSeenAsync(listing.Id, ct)) continue;

            await seenListings.MarkAsSeenAsync(listing.Id, listing.Source, listing.Url, ct);

            var message = FormatSms(listing);
            logger.LogInformation("Nouvelle annonce: [{Source}] {Title} — {Price}€", listing.Source, listing.Title, listing.Price);

            await smsService.SendAsync(message, ct);
        }
    }

    private static string FormatSms(Listing listing)
    {
        var parts = new List<string>();

        // Ligne 1 : source + arrondissement + surface + pièces
        var surface = listing.Surface.HasValue ? $"{listing.Surface}m²" : null;
        var rooms = listing.Rooms > 0 ? $"{listing.Rooms}P" : null;
        var header = $"[{listing.Source}] {listing.Arrondissement}";
        if (surface != null || rooms != null)
            header += $" · {string.Join(" ", new[] { surface, rooms }.OfType<string>())}";
        parts.Add(header);

        // Ligne 2 : adresse (si disponible)
        if (!string.IsNullOrWhiteSpace(listing.Address))
            parts.Add(listing.Address);

        // Ligne 3 : prix
        parts.Add($"{listing.Price:N0}€/mois".Replace(",", " ").Replace(".", " "));

        // Ligne 4 : URL
        parts.Add(listing.Url);

        return string.Join("\n", parts);
    }
}
