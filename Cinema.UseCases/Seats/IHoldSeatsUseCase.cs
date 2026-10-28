namespace Cinema.UseCases.Seats;

public interface IHoldSeatsUseCase
{
    event Action<int>? OnSeatsChanged;
    bool TryHold(int showtimeId, IEnumerable<int> seatIds, string sessionId);
    bool TryHold(int showtimeId, IEnumerable<int> seatIds, string sessionId, TimeSpan duration);
    void Release(int showtimeId, IEnumerable<int> seatIds, string sessionId);
    void ReleaseAllBySession(string sessionId);
    DateTime? GetHoldExpiry(int showtimeId, string sessionId);
    void CleanExpiredHolds();
    void CancelScheduledSessionRelease(string sessionId);
}
