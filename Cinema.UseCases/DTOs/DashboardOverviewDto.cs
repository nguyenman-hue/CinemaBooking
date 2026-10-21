namespace Cinema.UseCases.DTOs;

public class DashboardOverviewDto
{
    // 4 KPI Cards
    public decimal TodayRevenue { get; set; }
    public decimal YesterdayRevenue { get; set; }
    public double RevenueGrowthPercent { get; set; }
    public decimal TotalAllTimeRevenue { get; set; }

    public int TodayTickets { get; set; }
    public int YesterdayTickets { get; set; }
    public double TicketGrowthPercent { get; set; }
    public int TotalAllTimeTickets { get; set; }

    public int ActiveMoviesCount { get; set; }
    public int NewMoviesThisMonth { get; set; }

    public double OccupancyRateToday { get; set; }
    public double OccupancyRateYesterday { get; set; }
    public double OccupancyGrowthPercent { get; set; }

    // Chart 1: Doanh thu theo ngày (7 ngày gần nhất)
    public List<RevenueByDayDto> DailyRevenues { get; set; } = new();

    // Chart 2: Top phim bán chạy nhất (Donut chart)
    public List<TopMovieSalesDto> TopMovies { get; set; } = new();

    // Table 1: Suất chiếu sắp tới
    public List<UpcomingShowtimeDashboardDto> UpcomingShowtimes { get; set; } = new();

    // Table 2: Đơn đặt vé gần đây
    public List<RecentBookingDashboardDto> RecentBookings { get; set; } = new();
}

public class RevenueByDayDto
{
    public string DateLabel { get; set; } = string.Empty; // "Thứ Hai" hoặc "21/10"
    public string FullDateStr { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public int Tickets { get; set; }
}

public class TopMovieSalesDto
{
    public int MovieId { get; set; }
    public string MovieTitle { get; set; } = string.Empty;
    public int TicketsSold { get; set; }
    public decimal Revenue { get; set; }
    public double Percentage { get; set; }
    public string ColorHex { get; set; } = "#38bdf8";
}

public class UpcomingShowtimeDashboardDto
{
    public int ShowtimeId { get; set; }
    public string MovieTitle { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string AuditoriumName { get; set; } = string.Empty;
    public int TotalSeats { get; set; }
    public int BookedSeats { get; set; }
    public int AvailableSeats => Math.Max(0, TotalSeats - BookedSeats);
    public double OccupancyPercent => TotalSeats > 0 ? Math.Round((double)BookedSeats / TotalSeats * 100, 1) : 0;
}

public class RecentBookingDashboardDto
{
    public string BookingReference { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public DateTime BookingTime { get; set; }
    public string PaymentMethod { get; set; } = "VietQR";
    public string Status { get; set; } = "Thành công";
}
