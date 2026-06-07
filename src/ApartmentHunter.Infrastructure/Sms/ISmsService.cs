namespace ApartmentHunter.Infrastructure.Sms;

public interface ISmsService
{
    Task SendAsync(string message, CancellationToken ct = default);
}
