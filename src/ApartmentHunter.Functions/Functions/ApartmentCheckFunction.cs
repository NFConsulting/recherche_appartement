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
            logger.LogWarning("Aucun scraper activé. Vérifiez la configuration des URLs.");
            return;
        }

        var tasks = enabledScrapers.Select(s => s.ScrapeAsync(_criteria, ct));
        var results = await Task.WhenAll(tasks);
        var allListings = results.SelectMany(x => x).ToList();

        logger.LogInformation("{Count} annonces trouvées au total", allListings.Count);

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
        var surface = listing.Surface.HasValue ? $" {listing.Surface}m²" : "";
        var arrond = listing.Arrondissement != "Paris" ? $" ({listing.Arrondissement})" : "";
        // SMS limité à 160 caractères
        var message = $"[{listing.Source}] {listing.Rooms}P{surface}{arrond} - {listing.Price}€\n{listing.Url}";
        return message.Length > 160 ? message[..157] + "..." : message;
    }
}
