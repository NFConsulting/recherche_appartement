namespace ApartmentHunter.Infrastructure.Storage;

public interface ISeenListingsRepository
{
    Task<bool> HasBeenSeenAsync(string listingId, CancellationToken ct = default);
    Task MarkAsSeenAsync(string listingId, string source, string url, CancellationToken ct = default);
}
