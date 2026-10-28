using Cinema.CoreBusiness.Exceptions;
using Cinema.CoreBusiness.Models;

namespace Cinema.CoreBusiness.Rules;

/// <summary>
/// LUẬT 1: Tranh chấp & Quy tắc đặt ghế (Seat Booking & Conflict Rule)
/// Bao gồm 3 tầng nghiệp vụ:
/// 1. Giới hạn số lượng ghế: Tối thiểu 1 ghế, tối đa 8 ghế/giao dịch (chống phe vé, đầu cơ).
/// 2. Tính khả dụng: Ghế đã mua hoặc đang được người khác giữ chỗ không được phép đặt.
/// 3. Quy tắc không để lại ghế trống đơn (Single Orphan Seat Rule): Không được để lại duy nhất
///    1 ghế trống đơn độc ở giữa các ghế đã đặt hoặc ở đầu/cuối hàng ghế (chuẩn vận hành rạp chiếu phim).
/// </summary>
public static class SeatBookingRule
{
    public const int MinSeatsPerBooking = 1;
    public const int MaxSeatsPerBooking = 8;
    public const int HoldDurationMinutes = 10;

    /// <summary>
    /// Kiểm tra số lượng ghế yêu cầu trong 1 lần đặt vé
    /// </summary>
    public static void ValidateSeatCount(IEnumerable<int> requestedSeatIds)
    {
        var count = requestedSeatIds?.Count() ?? 0;
        if (count < MinSeatsPerBooking)
        {
            throw new SeatConflictException("Vui lòng chọn ít nhất 1 ghế để thực hiện đặt vé!");
        }

        if (count > MaxSeatsPerBooking)
        {
            throw new SeatConflictException(
                $"Mỗi giao dịch chỉ được đặt tối đa {MaxSeatsPerBooking} ghế để đảm bảo công bằng cho khán giả!");
        }
    }

    /// <summary>
    /// Kiểm tra ghế có bị trùng với ghế đã bán hoặc ghế đang được giữ bởi phiên khác không
    /// </summary>
    public static void ValidateSeatsAvailability(
        IEnumerable<int> requestedSeatIds, 
        IEnumerable<int> alreadyBookedSeatIds,
        IEnumerable<int>? currentlyHeldByOthersSeatIds = null)
    {
        var bookedSet = new HashSet<int>(alreadyBookedSeatIds);
        var heldSet = currentlyHeldByOthersSeatIds != null 
            ? new HashSet<int>(currentlyHeldByOthersSeatIds) 
            : new HashSet<int>();

        foreach (var seatId in requestedSeatIds)
        {
            if (bookedSet.Contains(seatId))
            {
                throw new SeatConflictException($"Ghế mã #{seatId} đã được mua vé thành công bởi khách hàng khác. Vui lòng chọn ghế khác!");
            }

            if (heldSet.Contains(seatId))
            {
                throw new SeatConflictException($"Ghế mã #{seatId} đang được giữ chỗ trong phiên đặt vé của người khác. Vui lòng thử lại sau!");
            }
        }
    }

    /// <summary>
    /// LUẬT CHỐNG KHOẢNG TRỐNG ĐƠN (Single Orphan Seat Rule)
    /// Kiểm tra xem việc chọn ghế của khách có để lại đúng 1 ghế trống đơn lẻ bị cô lập ở hàng ghế hay không.
    /// </summary>
    public static void ValidateNoSingleOrphanSeat(
        IEnumerable<Seat> allSeatsInAuditorium,
        IEnumerable<int> requestedSeatIds,
        IEnumerable<int> alreadyUnavailableSeatIds)
    {
        var requestedSet = new HashSet<int>(requestedSeatIds);
        var unavailableSet = new HashSet<int>(alreadyUnavailableSeatIds);

        // Gom các ghế trong phòng chiếu theo hàng ghế (RowCode)
        var seatsByRow = allSeatsInAuditorium
            .GroupBy(s => s.RowCode)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.SeatNumber).ToList());

        foreach (var (rowCode, rowSeats) in seatsByRow)
        {
            // Chỉ kiểm tra những hàng ghế mà khách có chọn ít nhất 1 ghế
            bool hasSelectedInRow = rowSeats.Any(s => requestedSet.Contains(s.SeatId));
            if (!hasSelectedInRow) continue;

            // Trạng thái ghế sau khi khách chọn:
            // True: Đã bị chiếm (đã mua, đang giữ hoặc khách mới chọn)
            // False: Còn trống
            var isOccupiedAfter = rowSeats.Select(s => 
                unavailableSet.Contains(s.SeatId) || requestedSet.Contains(s.SeatId)
            ).ToList();

            var isOccupiedBefore = rowSeats.Select(s => 
                unavailableSet.Contains(s.SeatId)
            ).ToList();

            int n = rowSeats.Count;
            int i = 0;
            while (i < n)
            {
                if (!isOccupiedAfter[i])
                {
                    // Đếm độ dài khoảng trống liên tiếp
                    int start = i;
                    while (i < n && !isOccupiedAfter[i])
                    {
                        i++;
                    }
                    int gapLength = i - start;

                    // Nếu khoảng trống đúng bằng 1 ghế đơn lẻ
                    if (gapLength == 1)
                    {
                        var orphanSeat = rowSeats[start];

                        // Kiểm tra: Khoảng trống 1 ghế này có phải do chính khách hàng vừa tạo ra không?
                        // (Nếu trước khi khách đặt, ghế này đã bị cô lập sẵn thì không bắt lỗi khách)
                        int prevStart = start;
                        while (prevStart > 0 && !isOccupiedBefore[prevStart - 1]) prevStart--;
                        int prevEnd = start;
                        while (prevEnd < n - 1 && !isOccupiedBefore[prevEnd + 1]) prevEnd++;
                        int gapBefore = prevEnd - prevStart + 1;

                        if (gapBefore > 1)
                        {
                            throw new SeatConflictException(
                                $"Việc chọn ghế của bạn không được để trống 1 ghế ở bên trái, giữa hoặc bên phải trên cùng một hàng ghế mà bạn vừa chọn (Để lại ghế trống đơn lẻ {orphanSeat.SeatDisplayName})!");
                        }
                    }
                }
                else
                {
                    i++;
                }
            }
        }
    }

    /// <summary>
    /// LUẬT CHỐNG GHẾ TRỐNG TRONG LÚC CHỌN TRỰC TIẾP TRÊN SƠ ĐỒ (Interactive Seat Selection Rule)
    /// 1. Chặn ngay lập tức hành vi nhảy cóc tạo ra 1 ghế trống đơn lẻ bị kẹp ở giữa (giữa 2 ghế đã mua/đã chọn).
    ///    Ví dụ: F1-F6 đã có, nếu chưa chọn F7 mà chọn F8 thì chặn F8 vì bỏ trống F7 ở giữa.
    /// 2. Cho phép người dùng chọn ghế liền kề (như F7 nối tiếp F6) để có thể click tiếp F8 và chọn trọn vẹn cả dãy.
    /// </summary>
    public static void ValidateInteractiveSeatSelection(
        IEnumerable<Seat> allSeatsInAuditorium,
        IEnumerable<int> candidateSeatIds,
        IEnumerable<int> alreadyUnavailableSeatIds)
    {
        var candidateSet = new HashSet<int>(candidateSeatIds);
        var unavailableSet = new HashSet<int>(alreadyUnavailableSeatIds);

        var seatsByRow = allSeatsInAuditorium
            .GroupBy(s => s.RowCode)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.SeatNumber).ToList());

        foreach (var (rowCode, rowSeats) in seatsByRow)
        {
            bool hasSelectedInRow = rowSeats.Any(s => candidateSet.Contains(s.SeatId));
            if (!hasSelectedInRow) continue;

            var isOccupiedAfter = rowSeats.Select(s => 
                unavailableSet.Contains(s.SeatId) || candidateSet.Contains(s.SeatId)
            ).ToList();

            var isOccupiedBefore = rowSeats.Select(s => 
                unavailableSet.Contains(s.SeatId)
            ).ToList();

            int n = rowSeats.Count;
            int i = 0;
            while (i < n)
            {
                if (!isOccupiedAfter[i])
                {
                    int start = i;
                    while (i < n && !isOccupiedAfter[i])
                    {
                        i++;
                    }
                    int gapLength = i - start;

                    if (gapLength == 1)
                    {
                        var orphanSeat = rowSeats[start];
                        bool hasOccupiedLeft = start > 0 && isOccupiedAfter[start - 1];
                        bool hasOccupiedRight = start < n - 1 && isOccupiedAfter[start + 1];

                        if (hasOccupiedLeft && hasOccupiedRight)
                        {
                            bool wasTrappedBefore = (start > 0 && isOccupiedBefore[start - 1]) && 
                                                    (start < n - 1 && isOccupiedBefore[start + 1]);

                            if (!wasTrappedBefore)
                            {
                                throw new SeatConflictException(
                                    $"Không được để trống 1 ghế ở giữa ({orphanSeat.SeatDisplayName}). Vui lòng chọn các ghế liền kề nhau!");
                            }
                        }
                    }
                }
                else
                {
                    i++;
                }
            }
        }
    }
}

