using Cinema.CoreBusiness.Utilities;
using Cinema.CoreBusiness.Models;
using Cinema.CoreBusiness.Rules;
using Cinema.UseCases.PluginInterfaces.DataStore;
using Cinema.UseCases.PluginInterfaces.Services;
using Cinema.UseCases.PluginInterfaces.StateStore;

namespace Cinema.UseCases.Bookings;

/// <summary>
/// Đặt vé xem phim - Thực hiện LUẬT 1 (Tranh chấp ghế, Giới hạn vé & Chống ghế trống đơn) và tạo Booking/Ticket theo Header-Line với Transaction
/// </summary>
public class CreateBookingUseCase : ICreateBookingUseCase
{
    private readonly IBookingRepository _bookingRepo;
    private readonly ISeatRepository _seatRepo;
    private readonly IShowtimeRepository _showtimeRepo;
    private readonly ISeatHoldingStateStore _holdingStore;
    private readonly IEmailService? _emailService;

    public CreateBookingUseCase(
        IBookingRepository bookingRepo,
        ISeatRepository seatRepo,
        IShowtimeRepository showtimeRepo,
        ISeatHoldingStateStore holdingStore,
        IEmailService? emailService = null)
    {
        _bookingRepo = bookingRepo;
        _seatRepo = seatRepo;
        _showtimeRepo = showtimeRepo;
        _holdingStore = holdingStore;
        _emailService = emailService;
    }

    public async Task<string> ExecuteAsync(Booking booking, IEnumerable<int> selectedSeatIds, string sessionId)
    {
        var seatIdsList = selectedSeatIds.Distinct().ToList();

        // 1. Áp dụng LUẬT 1: Giới hạn số lượng ghế đặt (1 - 8 ghế)
        SeatBookingRule.ValidateSeatCount(seatIdsList);

        // 2. Kiểm tra sự tồn tại của suất chiếu
        var showtime = await _showtimeRepo.GetShowtimeByIdAsync(booking.ShowtimeId);
        if (showtime == null)
            throw new InvalidOperationException("Suất chiếu không tồn tại.");

        // Chặn đặt vé cho suất chiếu trong quá khứ / đã bắt đầu
        if (showtime.StartTime <= VietnamTime.Now)
            throw new InvalidOperationException($"Suất chiếu này đã bắt đầu ({showtime.StartTime:HH:mm dd/MM/yyyy}) hoặc đã kết thúc, không thể thực hiện đặt vé.");

        // 3. Áp dụng LUẬT 1: Tranh chấp ghế (Ghế đã bán hoặc đang bị người khác giữ)
        var bookedSeats = await _seatRepo.GetBookedSeatIdsByShowtimeIdAsync(booking.ShowtimeId);
        var heldByOthers = _holdingStore.GetHeldSeatIds(booking.ShowtimeId, excludeSessionId: sessionId);

        SeatBookingRule.ValidateSeatsAvailability(seatIdsList, bookedSeats, heldByOthers);

        // 4. Áp dụng LUẬT 1: Chống để lại ghế trống đơn lẻ (Single Orphan Seat Rule)
        var allAuditoriumSeatsList = (await _seatRepo.GetSeatsByAuditoriumIdAsync(showtime.AuditoriumId)).ToList();
        var allAuditoriumSeats = allAuditoriumSeatsList.ToDictionary(s => s.SeatId);

        SeatBookingRule.ValidateNoSingleOrphanSeat(allAuditoriumSeatsList, seatIdsList, bookedSeats.Concat(heldByOthers));

        // 5. Chuẩn bị line items (Ticket) và tính tổng tiền
        var tickets = new List<Ticket>();
        decimal totalAmount = 0;
        string datePrefix = VietnamTime.Now.ToString("yyyyMMdd");
        string randomSuffix = Guid.NewGuid().ToString("N")[..6].ToUpper();
        booking.BookingReference = $"BK-{datePrefix}-{randomSuffix}";

        foreach (var seatId in seatIdsList)
        {
            if (!allAuditoriumSeats.TryGetValue(seatId, out var seat))
                throw new InvalidOperationException($"Ghế ID {seatId} không hợp lệ trong phòng chiếu này.");

            decimal ticketPrice = showtime.BasePrice + seat.PriceModifier;
            totalAmount += ticketPrice;

            tickets.Add(new Ticket
            {
                ShowtimeId = booking.ShowtimeId,
                SeatId = seatId,
                TicketPrice = ticketPrice,
                TicketCode = $"TK-{booking.ShowtimeId}-{seat.SeatDisplayName}-{Guid.NewGuid().ToString("N")[..5].ToUpper()}"
            });
        }

        booking.TotalAmount = totalAmount;

        // 6. Lưu Booking & Tickets vào CSDL trong một Transaction
        string createdReference = await _bookingRepo.CreateBookingWithTicketsAsync(booking, tickets);

        // 7. Giải phóng trạng thái giữ ghế
        _holdingStore.ReleaseSeats(booking.ShowtimeId, seatIdsList, sessionId);

        // 8. Tự động gửi Email vé điện tử (E-Ticket) kèm Mã Tra Cứu tới Gmail của khách hàng
        if (_emailService != null && !string.IsNullOrWhiteSpace(booking.CustomerEmail))
        {
            var seatNames = tickets.Select(t => allAuditoriumSeats.TryGetValue(t.SeatId, out var s) ? s.SeatDisplayName : $"Ghế #{t.SeatId}").ToList();
            var recipientEmail = booking.CustomerEmail;
            var recipientName = string.IsNullOrWhiteSpace(booking.CustomerName) ? "Quý khách" : booking.CustomerName;
            var movieTitle = showtime.MovieTitle ?? "Phim chiếu rạp";
            var showtimeStart = showtime.StartTime;
            var auditoriumName = showtime.AuditoriumName ?? "Phòng chiếu";
            var screenType = showtime.ScreenType ?? "2D Digital";
            var bookingTotal = booking.TotalAmount;
            var paymentMethod = booking.PaymentMethod;

            _ = Task.Run(async () =>
            {
                try
                {
                    await _emailService.SendTicketEmailAsync(
                        recipientEmail,
                        recipientName,
                        createdReference,
                        movieTitle,
                        showtimeStart,
                        auditoriumName,
                        screenType,
                        seatNames,
                        bookingTotal,
                        paymentMethod);
                }
                catch
                {
                    // Lỗi đã được ghi log an toàn bên trong IEmailService
                }
            });
        }

        return createdReference;
    }
}
