using Cinema.CoreBusiness.Utilities;
using System.Data;
using Cinema.CoreBusiness.Models;
using Cinema.UseCases.PluginInterfaces.DataStore;
using Dapper;

namespace Cinema.DataStore.SQL.Dapper.Repositories;

public class ShowtimeRepository : IShowtimeRepository
{
    private readonly ISqlConnectionFactory _factory;

    public ShowtimeRepository(ISqlConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IEnumerable<Showtime>> GetAllUpcomingShowtimesAsync()
    {
        using var connection = _factory.CreateConnection();
        const string sql = @"
            SELECT s.ShowtimeId, s.MovieId, s.AuditoriumId, s.StartTime, s.EndTime, s.BasePrice, s.IsActive,
                   m.Title AS MovieTitle, m.PosterUrl AS MoviePosterUrl, m.AgeRating AS MovieAgeRating, m.DurationMinutes AS MovieDurationMinutes,
                   a.Name AS AuditoriumName, a.ScreenType AS ScreenType
            FROM dbo.Showtime s
            INNER JOIN dbo.Movie m ON s.MovieId = m.MovieId
            INNER JOIN dbo.Auditorium a ON s.AuditoriumId = a.AuditoriumId
            WHERE s.IsActive = 1
            ORDER BY s.StartTime";

        var result = await connection.QueryAsync<Showtime>(sql);
        return result.ToList();
    }

    public async Task<IEnumerable<Showtime>> GetShowtimesByDateAsync(DateTime date)
    {
        using var connection = _factory.CreateConnection();
        const string sql = @"
            SELECT s.ShowtimeId, s.MovieId, s.AuditoriumId, s.StartTime, s.EndTime, s.BasePrice, s.IsActive,
                   m.Title AS MovieTitle, m.PosterUrl AS MoviePosterUrl, m.AgeRating AS MovieAgeRating, m.DurationMinutes AS MovieDurationMinutes,
                   a.Name AS AuditoriumName, a.ScreenType AS ScreenType
            FROM dbo.Showtime s
            INNER JOIN dbo.Movie m ON s.MovieId = m.MovieId
            INNER JOIN dbo.Auditorium a ON s.AuditoriumId = a.AuditoriumId
            WHERE CAST(s.StartTime AS DATE) = CAST(@Date AS DATE) AND s.IsActive = 1
            ORDER BY s.StartTime";

        var result = await connection.QueryAsync<Showtime>(sql, new { Date = date.Date });
        return result.ToList();
    }

    public async Task<IEnumerable<Showtime>> GetShowtimesByMovieIdAsync(int movieId)
    {
        using var connection = _factory.CreateConnection();
        const string sql = @"
            SELECT s.ShowtimeId, s.MovieId, s.AuditoriumId, s.StartTime, s.EndTime, s.BasePrice, s.IsActive,
                   m.Title AS MovieTitle, m.PosterUrl AS MoviePosterUrl, m.AgeRating AS MovieAgeRating, m.DurationMinutes AS MovieDurationMinutes,
                   a.Name AS AuditoriumName, a.ScreenType AS ScreenType
            FROM dbo.Showtime s
            INNER JOIN dbo.Movie m ON s.MovieId = m.MovieId
            INNER JOIN dbo.Auditorium a ON s.AuditoriumId = a.AuditoriumId
            WHERE s.MovieId = @MovieId AND s.StartTime >= DATEADD(MINUTE, -30, @Now) AND s.IsActive = 1
            ORDER BY s.StartTime";

        var result = await connection.QueryAsync<Showtime>(sql, new { MovieId = movieId, Now = VietnamTime.Now });
        return result.ToList();
    }

    public async Task<Showtime?> GetShowtimeByIdAsync(int showtimeId)
    {
        using var connection = _factory.CreateConnection();
        const string sql = @"
            SELECT s.ShowtimeId, s.MovieId, s.AuditoriumId, s.StartTime, s.EndTime, s.BasePrice, s.IsActive,
                   m.Title AS MovieTitle, m.PosterUrl AS MoviePosterUrl, m.AgeRating AS MovieAgeRating, m.DurationMinutes AS MovieDurationMinutes,
                   a.Name AS AuditoriumName, a.ScreenType AS ScreenType
            FROM dbo.Showtime s
            INNER JOIN dbo.Movie m ON s.MovieId = m.MovieId
            INNER JOIN dbo.Auditorium a ON s.AuditoriumId = a.AuditoriumId
            WHERE s.ShowtimeId = @ShowtimeId";

        return await connection.QueryFirstOrDefaultAsync<Showtime>(sql, new { ShowtimeId = showtimeId });
    }

    public async Task<IEnumerable<Showtime>> GetShowtimesByAuditoriumAndDateRangeAsync(int auditoriumId, DateTime start, DateTime end)
    {
        using var connection = _factory.CreateConnection();
        const string sql = @"
            SELECT s.ShowtimeId, s.MovieId, s.AuditoriumId, s.StartTime, s.EndTime, s.BasePrice, s.IsActive
            FROM dbo.Showtime s
            WHERE s.AuditoriumId = @AuditoriumId AND s.StartTime >= @Start AND s.StartTime < @End AND s.IsActive = 1";

        var result = await connection.QueryAsync<Showtime>(sql, new { AuditoriumId = auditoriumId, Start = start, End = end });
        return result.ToList();
    }

    public async Task<int> AddShowtimeAsync(Showtime showtime)
    {
        using var connection = _factory.CreateConnection();
        const string sql = @"
            INSERT INTO dbo.Showtime (MovieId, AuditoriumId, StartTime, EndTime, BasePrice, IsActive)
            VALUES (@MovieId, @AuditoriumId, @StartTime, @EndTime, @BasePrice, @IsActive);
            SELECT CAST(SCOPE_IDENTITY() as int);";

        return await connection.ExecuteScalarAsync<int>(sql, showtime);
    }

    public async Task<bool> DeleteShowtimeAsync(int showtimeId)
    {
        using var connection = _factory.CreateConnection();
        const string sql = "DELETE FROM dbo.Showtime WHERE ShowtimeId = @ShowtimeId";
        var rows = await connection.ExecuteAsync(sql, new { ShowtimeId = showtimeId });
        return rows > 0;
    }
}
