using Cinema.UseCases.DTOs;

namespace Cinema.UseCases.AI;

/// <summary>
/// UseCase tim kiem phim theo ngu nghia tu nhien (Semantic Search) va Tro ly hoi thoai dien anh AI
/// </summary>
public interface ISemanticMovieSearchUseCase
{
    /// <summary>
    /// Hoi thoai truc tiep voi AI kem goi y phim va suat chieu thuc te
    /// </summary>
    Task<AiChatResponseDto> ChatAsync(string naturalLanguageQuery, DateTime? preferredDate = null);

    /// <summary>
    /// Tim kiem phim theo ngu nghia (tuong thich nguoc)
    /// </summary>
    Task<List<AiMovieRecommendationDto>> ExecuteAsync(string naturalLanguageQuery, DateTime? preferredDate = null);
}
