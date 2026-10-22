namespace Cinema.UseCases.DTOs;

/// <summary>
/// DTO chua thong tin 1 tin nhan trong lich su hoi thoai giua User va AI
/// </summary>
public class AiChatMessageDto
{
    public bool IsUser { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// DTO chua phan hoi tro chuyen tu nhien va danh sach goi y phim tu Tro ly AI
/// </summary>
public class AiChatResponseDto
{
    /// <summary>
    /// Loi thoai tro chuyen tu nhien, tu van thong minh tu AI (nhu ChatGPT)
    /// </summary>
    public string ReplyMessage { get; set; } = string.Empty;

    /// <summary>
    /// Danh sach cac bo phim duoc AI goi y kem suat chieu thuc te (rong neu chi chao hoi hoac khong co phim phu hop)
    /// </summary>
    public List<AiMovieRecommendationDto> Recommendations { get; set; } = new();
}
