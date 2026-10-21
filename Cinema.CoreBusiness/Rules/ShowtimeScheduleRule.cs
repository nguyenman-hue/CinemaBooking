using Cinema.CoreBusiness.Utilities;
using Cinema.CoreBusiness.Exceptions;
using Cinema.CoreBusiness.Models;

namespace Cinema.CoreBusiness.Rules;

/// <summary>
/// LUẬT 2: Quy tắc xếp lịch chiếu & Chống chồng giờ (Showtime Schedule & Non-overlapping Rule)
/// Bao gồm 4 tầng nghiệp vụ:
/// 1. Không xếp lịch trong quá khứ: Thời gian bắt đầu phải lớn hơn thời điểm hiện tại.
/// 2. Khung giờ hoạt động rạp: Chỉ xếp suất chiếu trong khung giờ mở cửa rạp (08:00 - 23:30).
/// 3. Phân biệt rõ ràng 2 khái niệm thời gian:
///    - MovieEndTime: Thời gian hết phim (StartTime + MovieDuration) -> In lên vé cho khán giả.
///    - AuditoriumAvailableTime: Thời gian phòng chiếu sẵn sàng (MovieEndTime + 15 phút dọn rạp) -> Dùng để xếp suất tiếp theo.
/// 4. Không chồng giờ trong cùng phòng chiếu: Giữa 2 suất chiếu bắt buộc có tối thiểu 15 phút dọn phòng vệ sinh.
/// </summary>
public static class ShowtimeScheduleRule
{
    public const int CleaningBufferMinutes = 15;
    public static readonly TimeSpan EarliestOpeningTime = new(8, 0, 0);   // 08:00 sáng
    public static readonly TimeSpan LatestOpeningTime = new(23, 30, 0);   // 23:30 đêm

    /// <summary>
    /// Thời gian kết thúc phim thực tế (dùng để in lên vé cho khán giả)
    /// </summary>
    public static DateTime CalculateMovieEndTime(DateTime startTime, int movieDurationMinutes)
    {
        return startTime.AddMinutes(movieDurationMinutes);
    }

    /// <summary>
    /// Thời gian phòng chiếu sẵn sàng sau khi dọn rạp 15 phút (cho hệ thống quản lý lịch chiếu)
    /// </summary>
    public static DateTime CalculateAuditoriumAvailableTime(DateTime startTime, int movieDurationMinutes)
    {
        return startTime.AddMinutes(movieDurationMinutes + CleaningBufferMinutes);
    }

    /// <summary>
    /// Tính EndTime của suất chiếu (bao gồm 15 phút dọn phòng chiếu)
    /// </summary>
    public static DateTime CalculateEndTime(DateTime startTime, int movieDurationMinutes)
    {
        return CalculateAuditoriumAvailableTime(startTime, movieDurationMinutes);
    }

    /// <summary>
    /// Kiểm tra không được xếp suất chiếu trong quá khứ
    /// </summary>
    public static void ValidateNotInPast(DateTime startTime, DateTime? referenceTime = null)
    {
        var now = referenceTime ?? VietnamTime.Now;
        if (startTime < now)
        {
            throw new ShowtimeOverlapException(
                $"Không thể tạo suất chiếu trong quá khứ ({startTime:HH:mm dd/MM/yyyy})! " +
                $"Thời gian bắt đầu phải lớn hơn thời điểm hiện tại ({now:HH:mm dd/MM/yyyy}).");
        }
    }

    /// <summary>
    /// Kiểm tra suất chiếu có nằm trong khung giờ mở cửa của rạp (08:00 - 23:30)
    /// </summary>
    public static void ValidateOperatingHours(DateTime startTime)
    {
        var timeOfDay = startTime.TimeOfDay;
        if (timeOfDay < EarliestOpeningTime && timeOfDay >= TimeSpan.Zero)
        {
            throw new ShowtimeOverlapException(
                $"Suất chiếu lúc {startTime:HH:mm} nằm ngoài khung giờ mở cửa rạp (08:00 - 23:30)! " +
                "Vui lòng xếp lịch từ 08:00 sáng trở đi.");
        }
    }

    /// <summary>
    /// Kiểm tra tính hợp lệ và không chồng giờ giữa các suất chiếu trong cùng một phòng chiếu
    /// </summary>
    public static void ValidateNoOverlap(
        Showtime newShowtime, 
        IEnumerable<Showtime> existingShowtimesInSameAuditorium)
    {
        if (newShowtime.EndTime <= newShowtime.StartTime)
        {
            throw new ShowtimeOverlapException("Thời gian kết thúc suất chiếu phải sau thời gian bắt đầu!");
        }

        foreach (var existing in existingShowtimesInSameAuditorium)
        {
            if (existing.ShowtimeId == newShowtime.ShowtimeId) continue;

            // Kiểm tra giao khoảng thời gian: [StartTime, EndTime]
            // Chú ý EndTime đã bao gồm 15 phút dọn rạp
            bool isOverlap = (newShowtime.StartTime < existing.EndTime) && (newShowtime.EndTime > existing.StartTime);
            if (isOverlap)
            {
                throw new ShowtimeOverlapException(
                    $"Suất chiếu bị trùng lịch với suất chiếu ID #{existing.ShowtimeId} " +
                    $"({existing.StartTime:HH:mm dd/MM/yyyy} - {existing.EndTime:HH:mm dd/MM/yyyy}) " +
                    $"tại phòng chiếu này! Giữa các suất chiếu bắt buộc có tối thiểu {CleaningBufferMinutes} phút dọn phòng vệ sinh.");
            }
        }
    }
}
