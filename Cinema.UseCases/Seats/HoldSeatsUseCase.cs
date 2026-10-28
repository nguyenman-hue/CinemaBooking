using Cinema.CoreBusiness.Rules;
using Cinema.UseCases.PluginInterfaces.StateStore;

namespace Cinema.UseCases.Seats;

public class HoldSeatsUseCase : IHoldSeatsUseCase
{
    private readonly ISeatHoldingStateStore _holdingStore;

    public event Action<int>? OnSeatsChanged
    {
        add => _holdingStore.OnSeatsChanged += value;
        remove => _holdingStore.OnSeatsChanged -= value;
    }

    public HoldSeatsUseCase(ISeatHoldingStateStore holdingStore)
    {
        _holdingStore = holdingStore;
    }

    public bool TryHold(int showtimeId, IEnumerable<int> seatIds, string sessionId)
    {
        return _holdingStore.TryHoldSeats(showtimeId, seatIds, sessionId, TimeSpan.FromMinutes(SeatBookingRule.HoldDurationMinutes));
    }

    public bool TryHold(int showtimeId, IEnumerable<int> seatIds, string sessionId, TimeSpan duration)
    {
        return _holdingStore.TryHoldSeats(showtimeId, seatIds, sessionId, duration);
    }

    public void Release(int showtimeId, IEnumerable<int> seatIds, string sessionId)
    {
        _holdingStore.ReleaseSeats(showtimeId, seatIds, sessionId);
    }

    public void ReleaseAllBySession(string sessionId)
    {
        _holdingStore.ReleaseAllBySession(sessionId);
    }

    public DateTime? GetHoldExpiry(int showtimeId, string sessionId)
    {
        return _holdingStore.GetHoldExpiryTime(showtimeId, sessionId);
    }

    public void CleanExpiredHolds()
    {
        _holdingStore.CleanExpiredHolds();
    }

    public void CancelScheduledSessionRelease(string sessionId)
    {
        _holdingStore.CancelScheduledSessionRelease(sessionId);
    }
}
