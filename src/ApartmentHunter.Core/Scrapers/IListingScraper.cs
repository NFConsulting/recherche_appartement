using ApartmentHunter.Core.Models;

namespace ApartmentHunter.Core.Scrapers;

public interface IListingScraper
{
    string SourceName { get; }
    bool IsEnabled { get; }
    Task<IReadOnlyList<Listing>> ScrapeAsync(SearchCriteria criteria, CancellationToken ct = default);
}
