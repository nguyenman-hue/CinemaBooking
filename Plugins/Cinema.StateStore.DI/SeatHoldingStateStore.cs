using System.Collections.Concurrent;
using Cinema.UseCases.PluginInterfaces.StateStore;

namespace Cinema.StateStore.DI;

public class SeatHoldingStateStore : ISeatHoldingStateStore
{
    private class SeatHold
    {
        public int ShowtimeId { get; set; }
        public int SeatId { get; set; }
        public string SessionId { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }

    // Key: $"{showtimeId}_{seatId}"
    private readonly ConcurrentDictionary<string, SeatHold> _holds = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingReleases = new();

    public event Action<int>? OnSeatsChanged;

    public bool TryHoldSeats(int showtimeId, IEnumerable<int> seatIds, string sessionId, TimeSpan duration)
    {
        CancelScheduledSessionRelease(sessionId);
        CleanExpiredHolds();
        var now = DateTime.UtcNow;
        var expiresAt = now.Add(duration);
        var seatList = seatIds.ToList();

        // Kiểm tra xem có ghế nào đang bị giữ bởi phiên khác còn hạn không
        foreach (var seatId in seatList)
        {
            string key = $"{showtimeId}_{seatId}";
            if (_holds.TryGetValue(key, out var existingHold))
            {
                if (existingHold.ExpiresAt > now && existingHold.SessionId != sessionId)
                {
                    return false; // Ghế đã bị người khác giữ
                }
            }
        }

        // Đặt giữ chỗ cho các ghế
        foreach (var seatId in seatList)
        {
            string key = $"{showtimeId}_{seatId}";
            _holds[key] = new SeatHold
            {
                ShowtimeId = showtimeId,
                SeatId = seatId,
                SessionId = sessionId,
                ExpiresAt = expiresAt
            };
        }

        OnSeatsChanged?.Invoke(showtimeId);
        return true;
    }

    public void ReleaseSeats(int showtimeId, IEnumerable<int> seatIds, string sessionId)
    {
        bool anyRemoved = false;
        foreach (var seatId in seatIds)
        {
            string key = $"{showtimeId}_{seatId}";
            if (_holds.TryGetValue(key, out var hold) && hold.SessionId == sessionId)
            {
                if (_holds.TryRemove(key, out _))
                {
                    anyRemoved = true;
                }
            }
        }

        if (anyRemoved)
        {
            OnSeatsChanged?.Invoke(showtimeId);
        }
    }

    public void ReleaseAllBySession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        CancelScheduledSessionRelease(sessionId);

        var keysToRemove = _holds
            .Where(kvp => kvp.Value.SessionId == sessionId)
            .Select(kvp => kvp.Key)
            .ToList();

        bool anyRemoved = false;
        foreach (var key in keysToRemove)
        {
            if (_holds.TryRemove(key, out _))
            {
                anyRemoved = true;
            }
        }

        if (anyRemoved)
        {
            OnSeatsChanged?.Invoke(0);
        }
    }

    public void ScheduleSessionRelease(string sessionId, TimeSpan delay)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;

        CancelScheduledSessionRelease(sessionId);

        var cts = new CancellationTokenSource();
        _pendingReleases[sessionId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token);
                if (!cts.Token.IsCancellationRequested)
                {
                    _pendingReleases.TryRemove(sessionId, out _);
                    ReleaseAllBySession(sessionId);
                }
            }
            catch (TaskCanceledException) { }
            catch { }
        }, cts.Token);
    }

    public void CancelScheduledSessionRelease(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;

        if (_pendingReleases.TryRemove(sessionId, out var cts))
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch { }
        }
    }

    public void ClearAllHolds()
    {
        _holds.Clear();
        OnSeatsChanged?.Invoke(0);
    }

    public IEnumerable<int> GetHeldSeatIds(int showtimeId, string excludeSessionId = "")
    {
        CleanExpiredHolds();
        var now = DateTime.UtcNow;
        return _holds.Values
            .Where(h => h.ShowtimeId == showtimeId && h.ExpiresAt > now && (string.IsNullOrEmpty(excludeSessionId) || h.SessionId != excludeSessionId))
            .Select(h => h.SeatId)
            .Distinct();
    }

    public IEnumerable<int> GetMyHeldSeatIds(int showtimeId, string sessionId)
    {
        CleanExpiredHolds();
        var now = DateTime.UtcNow;
        return _holds.Values
            .Where(h => h.ShowtimeId == showtimeId && h.ExpiresAt > now && h.SessionId == sessionId)
            .Select(h => h.SeatId)
            .Distinct();
    }

    public DateTime? GetHoldExpiryTime(int showtimeId, string sessionId)
    {
        CleanExpiredHolds();
        var now = DateTime.UtcNow;
        var myHolds = _holds.Values
            .Where(h => h.ShowtimeId == showtimeId && h.SessionId == sessionId && h.ExpiresAt > now)
            .ToList();
        if (!myHolds.Any()) return null;
        return myHolds.Min(h => h.ExpiresAt);
    }

    public void CleanExpiredHolds()
    {
        var now = DateTime.UtcNow;
        var expiredKeys = _holds
            .Where(kvp => kvp.Value.ExpiresAt <= now)
            .Select(kvp => kvp.Key)
            .ToList();

        if (expiredKeys.Any())
        {
            foreach (var key in expiredKeys)
            {
                _holds.TryRemove(key, out _);
            }
            OnSeatsChanged?.Invoke(0);
        }
    }
}
