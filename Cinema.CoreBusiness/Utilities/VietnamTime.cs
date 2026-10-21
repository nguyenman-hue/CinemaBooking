namespace Cinema.CoreBusiness.Utilities;

/// <summary>
/// Quản lý thời gian chuẩn theo múi giờ Việt Nam (UTC+7 / "SE Asia Standard Time").
/// Giúp hệ thống đồng bộ chính xác dù triển khai trên máy chủ đám mây ở bất kỳ quốc gia nào (như SmarterASP ở Mỹ).
/// </summary>
public static class VietnamTime
{
    private static readonly TimeZoneInfo? _vietnamTimeZone;

    static VietnamTime()
    {
        try
        {
            _vietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch
        {
            try
            {
                _vietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
            }
            catch
            {
                _vietnamTimeZone = null;
            }
        }
    }

    /// <summary>
    /// Thời điểm hiện tại theo múi giờ Việt Nam (UTC+7).
    /// </summary>
    public static DateTime Now => _vietnamTimeZone != null
        ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _vietnamTimeZone)
        : DateTime.UtcNow.AddHours(7);

    /// <summary>
    /// Ngày hôm nay (00:00:00) theo múi giờ Việt Nam.
    /// </summary>
    public static DateTime Today => Now.Date;
}
