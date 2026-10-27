namespace Cinema.UseCases.PluginInterfaces.StateStore;

public interface ISeatHoldingStateStore
{
    event Action<int>? OnSeatsChanged;
    bool TryHoldSeats(int showtimeId, IEnumerable<int> seatIds, string sessionId, TimeSpan duration);
    void ReleaseSeats(int showtimeId, IEnumerable<int> seatIds, string sessionId);
    void ReleaseAllBySession(string sessionId);
    void ScheduleSessionRelease(string sessionId, TimeSpan delay);
    void CancelScheduledSessionRelease(string sessionId);
    void ClearAllHolds();
    IEnumerable<int> GetHeldSeatIds(int showtimeId, string excludeSessionId = "");
    IEnumerable<int> GetMyHeldSeatIds(int showtimeId, string sessionId);
    DateTime? GetHoldExpiryTime(int showtimeId, string sessionId);
    void CleanExpiredHolds();
}
