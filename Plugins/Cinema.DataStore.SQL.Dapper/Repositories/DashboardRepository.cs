using System.Data;
using Cinema.UseCases.DTOs;
using Cinema.UseCases.PluginInterfaces.DataStore;
using Dapper;

namespace Cinema.DataStore.SQL.Dapper.Repositories;

internal class DailyRawRow { public DateTime BookingDate { get; set; } public decimal DailyRevenue { get; set; } public int DailyTickets { get; set; } }
internal class OccRawRow { public int TotalSeatsAll { get; set; } public int BookedSeatsAll { get; set; } }

public class DashboardRepository : IDashboardRepository
{
    private readonly ISqlConnectionFactory _factory;

    public DashboardRepository(ISqlConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<DashboardOverviewDto> GetDashboardOverviewAsync(DateTime today)
    {
        using var connection = _factory.CreateConnection();
        var dto = new DashboardOverviewDto();
        var yesterday = today.AddDays(-1);

        // 1. Doanh thu & Vé bán Hôm nay và Hôm qua (Số liệu chuẩn thời gian thực)
        const string revenueSql = @"
            SELECT 
                ISNULL(SUM(CASE WHEN CAST(b.BookingTime AS DATE) = CAST(@Today AS DATE) THEN b.TotalAmount ELSE 0 END), 0) AS TodayRevenue,
                ISNULL(SUM(CASE WHEN CAST(b.BookingTime AS DATE) = CAST(@Yesterday AS DATE) THEN b.TotalAmount ELSE 0 END), 0) AS YesterdayRevenue,
                ISNULL(SUM(b.TotalAmount), 0) AS TotalAllTimeRevenue
            FROM dbo.Booking b
            WHERE b.Status = 'Confirmed' OR b.Status IS NULL OR b.Status = N'Da thanh toán';

            SELECT 
                ISNULL(SUM(CASE WHEN CAST(b.BookingTime AS DATE) = CAST(@Today AS DATE) THEN 1 ELSE 0 END), 0) AS TodayTickets,
                ISNULL(SUM(CASE WHEN CAST(b.BookingTime AS DATE) = CAST(@Yesterday AS DATE) THEN 1 ELSE 0 END), 0) AS YesterdayTickets,
                COUNT(t.TicketId) AS TotalAllTimeTickets
            FROM dbo.Ticket t
            INNER JOIN dbo.Booking b ON t.BookingId = b.BookingId
            WHERE b.Status = 'Confirmed' OR b.Status IS NULL OR b.Status = N'Da thanh toán';";

        using (var multi = await connection.QueryMultipleAsync(revenueSql, new { Today = today.Date, Yesterday = yesterday.Date }))
        {
            var revData = await multi.ReadFirstOrDefaultAsync<dynamic>();
            if (revData != null)
            {
                dto.TodayRevenue = (decimal)(revData.TodayRevenue ?? 0m);
                dto.YesterdayRevenue = (decimal)(revData.YesterdayRevenue ?? 0m);
                dto.TotalAllTimeRevenue = (decimal)(revData.TotalAllTimeRevenue ?? 0m);

                if (dto.YesterdayRevenue > 0)
                {
                    dto.RevenueGrowthPercent = Math.Round((double)((dto.TodayRevenue - dto.YesterdayRevenue) / dto.YesterdayRevenue) * 100, 1);
                }
                else if (dto.TodayRevenue > 0)
                {
                    dto.RevenueGrowthPercent = 100.0;
                }
                else
                {
                    dto.RevenueGrowthPercent = 0.0;
                }
            }

            var ticketData = await multi.ReadFirstOrDefaultAsync<dynamic>();
            if (ticketData != null)
            {
                dto.TodayTickets = (int)(ticketData.TodayTickets ?? 0);
                dto.YesterdayTickets = (int)(ticketData.YesterdayTickets ?? 0);
                dto.TotalAllTimeTickets = (int)(ticketData.TotalAllTimeTickets ?? 0);

                if (dto.YesterdayTickets > 0)
                {
                    dto.TicketGrowthPercent = Math.Round((double)(dto.TodayTickets - dto.YesterdayTickets) / dto.YesterdayTickets * 100, 1);
                }
                else if (dto.TodayTickets > 0)
                {
                    dto.TicketGrowthPercent = 100.0;
                }
                else
                {
                    dto.TicketGrowthPercent = 0.0;
                }
            }
        }

        // 2. Số lượng phim đang chiếu
        const string movieSql = @"
            SELECT 
                COUNT(*) AS ActiveMovies,
                ISNULL(SUM(CASE WHEN DATEDIFF(DAY, ReleaseDate, @Today) <= 30 THEN 1 ELSE 0 END), 0) AS NewMovies
            FROM dbo.Movie
            WHERE IsActive = 1;";
        var movieData = await connection.QueryFirstOrDefaultAsync<dynamic>(movieSql, new { Today = today.Date });
        if (movieData != null)
        {
            dto.ActiveMoviesCount = (int)(movieData.ActiveMovies ?? 0);
            dto.NewMoviesThisMonth = (int)(movieData.NewMovies ?? 0);
        }

        // 3. Tỷ lệ lấp đầy ghế theo suất chiếu hôm nay
        const string occupancySql = @"
            SELECT 
                ISNULL(SUM(a.TotalSeats), 0) AS TotalSeatsToday,
                ISNULL(COUNT(t.TicketId), 0) AS BookedSeatsToday
            FROM dbo.Showtime s
            INNER JOIN dbo.Auditorium a ON s.AuditoriumId = a.AuditoriumId
            LEFT JOIN dbo.Ticket t ON s.ShowtimeId = t.ShowtimeId
            WHERE CAST(s.StartTime AS DATE) = CAST(@Today AS DATE) AND s.IsActive = 1;

            SELECT 
                ISNULL(SUM(a.TotalSeats), 0) AS TotalSeatsYesterday,
                ISNULL(COUNT(t.TicketId), 0) AS BookedSeatsYesterday
            FROM dbo.Showtime s
            INNER JOIN dbo.Auditorium a ON s.AuditoriumId = a.AuditoriumId
            LEFT JOIN dbo.Ticket t ON s.ShowtimeId = t.ShowtimeId
            WHERE CAST(s.StartTime AS DATE) = CAST(@Yesterday AS DATE) AND s.IsActive = 1;";

        using (var multiOcc = await connection.QueryMultipleAsync(occupancySql, new { Today = today.Date, Yesterday = yesterday.Date }))
        {
            var occToday = await multiOcc.ReadFirstOrDefaultAsync<dynamic>();
            int totalSeatsToday = occToday != null ? (int)(occToday.TotalSeatsToday ?? 0) : 0;
            int bookedSeatsToday = occToday != null ? (int)(occToday.BookedSeatsToday ?? 0) : 0;

            var occYest = await multiOcc.ReadFirstOrDefaultAsync<dynamic>();
            int totalSeatsYest = occYest != null ? (int)(occYest.TotalSeatsYesterday ?? 0) : 0;
            int bookedSeatsYest = occYest != null ? (int)(occYest.BookedSeatsYesterday ?? 0) : 0;

            dto.OccupancyRateToday = totalSeatsToday > 0 ? Math.Round((double)bookedSeatsToday / totalSeatsToday * 100, 1) : 0;
            dto.OccupancyRateYesterday = totalSeatsYest > 0 ? Math.Round((double)bookedSeatsYest / totalSeatsYest * 100, 1) : 0;
            dto.OccupancyGrowthPercent = Math.Round(dto.OccupancyRateToday - dto.OccupancyRateYesterday, 1);
        }

        // 4. Doanh thu 7 ngày gần nhất
        var startDate = today.AddDays(-6);
        const string dailySql = @"
            SELECT 
                b.BookingDate,
                b.DailyRevenue,
                ISNULL(t.DailyTickets, 0) AS DailyTickets
            FROM (
                SELECT CAST(BookingTime AS DATE) AS BookingDate, SUM(TotalAmount) AS DailyRevenue
                FROM dbo.Booking
                WHERE BookingTime >= @StartDate AND BookingTime < @EndDate
                GROUP BY CAST(BookingTime AS DATE)
            ) b
            LEFT JOIN (
                SELECT CAST(b2.BookingTime AS DATE) AS BookingDate, COUNT(tk.TicketId) AS DailyTickets
                FROM dbo.Ticket tk
                INNER JOIN dbo.Booking b2 ON tk.BookingId = b2.BookingId
                WHERE b2.BookingTime >= @StartDate AND b2.BookingTime < @EndDate
                GROUP BY CAST(b2.BookingTime AS DATE)
            ) t ON b.BookingDate = t.BookingDate
            ORDER BY b.BookingDate;";

        var dailyList = (await connection.QueryAsync<DailyRawRow>(dailySql, new { StartDate = startDate.Date, EndDate = today.AddDays(1).Date })).ToList();
        var dailyDict = dailyList.ToDictionary(d => d.BookingDate.Date, d => new { Revenue = d.DailyRevenue, Tickets = d.DailyTickets });

        string[] dayOfWeekNames = { "CN", "Thứ 2", "Thứ 3", "Thứ 4", "Thứ 5", "Thứ 6", "Thứ 7" };
        for (int i = -6; i <= 0; i++)
        {
            var curDate = today.AddDays(i);
            decimal rev = 0; int ticks = 0;
            if (dailyDict.TryGetValue(curDate.Date, out var val) && val != null) { rev = val.Revenue; ticks = val.Tickets; }
            

            dto.DailyRevenues.Add(new RevenueByDayDto
            {
                DateLabel = dayOfWeekNames[(int)curDate.DayOfWeek],
                FullDateStr = curDate.ToString("dd/MM"),
                Revenue = rev,
                Tickets = ticks
            });
        }

        // 5. Top phim bán chạy nhất (Donut chart)
        const string topMoviesSql = @"
            SELECT TOP 5
                m.MovieId,
                m.Title AS MovieTitle,
                COUNT(t.TicketId) AS TicketsSold,
                ISNULL(SUM(t.TicketPrice), 0) AS Revenue
            FROM dbo.Movie m
            INNER JOIN dbo.Showtime s ON m.MovieId = s.MovieId
            INNER JOIN dbo.Ticket t ON s.ShowtimeId = t.ShowtimeId
            GROUP BY m.MovieId, m.Title
            ORDER BY TicketsSold DESC;";

        var topMoviesList = (await connection.QueryAsync<TopMovieSalesDto>(topMoviesSql)).ToList();
        string[] colors = { "#38bdf8", "#f59e0b", "#a855f7", "#f43f5e", "#10b981" };

        int totalTopTickets = topMoviesList.Sum(m => m.TicketsSold);
        for (int i = 0; i < topMoviesList.Count; i++)
        {
            var m = topMoviesList[i];
            m.ColorHex = colors[i % colors.Length];
            m.Percentage = totalTopTickets > 0 ? Math.Round((double)m.TicketsSold / totalTopTickets * 100, 1) : 0;
        }

        if (topMoviesList.Count == 0)
        {
            const string fallbackMoviesSql = "SELECT TOP 5 MovieId, Title AS MovieTitle FROM dbo.Movie WHERE IsActive = 1;";
            var activeMovies = (await connection.QueryAsync<dynamic>(fallbackMoviesSql)).ToList();
            double[] defaultPercents = { 45.0, 25.0, 18.0, 12.0, 10.0 };
            for (int i = 0; i < activeMovies.Count; i++)
            {
                topMoviesList.Add(new TopMovieSalesDto
                {
                    MovieId = (int)activeMovies[i].MovieId,
                    MovieTitle = (string)activeMovies[i].MovieTitle,
                    TicketsSold = 0,
                    Percentage = i < defaultPercents.Length ? defaultPercents[i] : 10.0,
                    ColorHex = colors[i % colors.Length]
                });
            }
        }
        dto.TopMovies = topMoviesList;

        // 6. Lịch suất chiếu sắp tới (Chỉ lấy các suất trong tương lai tính từ thời điểm hiện tại)
        const string upcomingSql = @"
            SELECT TOP 6
                s.ShowtimeId,
                m.Title AS MovieTitle,
                m.PosterUrl,
                s.StartTime,
                s.EndTime,
                a.Name AS AuditoriumName,
                a.TotalSeats,
                COUNT(t.TicketId) AS BookedSeats
            FROM dbo.Showtime s
            INNER JOIN dbo.Movie m ON s.MovieId = m.MovieId
            INNER JOIN dbo.Auditorium a ON s.AuditoriumId = a.AuditoriumId
            LEFT JOIN dbo.Ticket t ON s.ShowtimeId = t.ShowtimeId
            WHERE s.IsActive = 1 AND s.StartTime >= @Now
            GROUP BY s.ShowtimeId, m.Title, m.PosterUrl, s.StartTime, s.EndTime, a.Name, a.TotalSeats
            ORDER BY s.StartTime ASC;";

        var upcomingResult = await connection.QueryAsync<UpcomingShowtimeDashboardDto>(upcomingSql, new { Now = Cinema.CoreBusiness.Utilities.VietnamTime.Now });
        dto.UpcomingShowtimes = upcomingResult.ToList();

        // 7. Đơn đặt vé gần đây
        const string recentBookingsSql = @"
            SELECT TOP 6
                b.BookingReference,
                b.CustomerName,
                b.TotalAmount,
                b.BookingTime,
                ISNULL(b.PaymentMethod, 'VietQR') AS PaymentMethod,
                ISNULL(b.Status, N'Thành công') AS Status
            FROM dbo.Booking b
            ORDER BY b.BookingTime DESC;";

        var recentList = await connection.QueryAsync<RecentBookingDashboardDto>(recentBookingsSql);
        dto.RecentBookings = recentList.ToList();

        return dto;
    }
}
