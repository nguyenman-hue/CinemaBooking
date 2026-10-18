using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cinema.CoreBusiness.Models;
using Cinema.UseCases.DTOs;
using Cinema.UseCases.PluginInterfaces.DataStore;

namespace Cinema.UseCases.AI;

public class SemanticMovieSearchUseCase : ISemanticMovieSearchUseCase
{
    private readonly IMovieRepository _movieRepository;
    private readonly IShowtimeRepository _showtimeRepository;
    private readonly HttpClient? _httpClient;
    private readonly string _geminiModel;
    private readonly string _effectiveApiKey;

    public SemanticMovieSearchUseCase(
        IMovieRepository movieRepository,
        IShowtimeRepository showtimeRepository,
        HttpClient? httpClient = null,
        string? geminiApiKey = null,
        string? geminiModel = null)
    {
        _movieRepository = movieRepository;
        _showtimeRepository = showtimeRepository;
        _httpClient = httpClient;
        _geminiModel = string.IsNullOrWhiteSpace(geminiModel) ? "gemini-2.5-flash" : geminiModel;
        _effectiveApiKey = (!string.IsNullOrWhiteSpace(geminiApiKey) && geminiApiKey != "YOUR_GEMINI_API_KEY") ? geminiApiKey : (Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "");
    }

    public async Task<List<AiMovieRecommendationDto>> ExecuteAsync(string naturalLanguageQuery, DateTime? preferredDate = null)
    {
        if (string.IsNullOrWhiteSpace(naturalLanguageQuery))
        {
            return new List<AiMovieRecommendationDto>();
        }

        var normalizedQuery = NormalizeText(naturalLanguageQuery);
        var activeMovies = (await _movieRepository.GetAllMoviesAsync(activeOnly: true)).ToList();
        var allShowtimes = (await _showtimeRepository.GetAllUpcomingShowtimesAsync()).ToList();

        var (timeFilterStart, timeFilterEnd, timeReason) = DetectTimePreference(normalizedQuery);
        var targetDate = preferredDate ?? DetectTargetDate(normalizedQuery);

        // Uu tien goi Google Gemini 2.5 Flash de doc hieu tu nhien moi cau hoi (nhu ChatGPT)
        if (!string.IsNullOrWhiteSpace(_effectiveApiKey) && _effectiveApiKey != "YOUR_GEMINI_API_KEY" && _httpClient != null)
        {
            try
            {
                var geminiResults = await TryCallGeminiAsync(naturalLanguageQuery, normalizedQuery, activeMovies, allShowtimes, targetDate, timeFilterStart, timeFilterEnd, timeReason);
                if (geminiResults != null && geminiResults.Any())
                {
                    return geminiResults;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gemini Fallback] Loi API: {ex.Message}. Chuyen sang Smart Local Semantic Engine.");
            }
        }

        return RunSmartSemanticSearch(naturalLanguageQuery, normalizedQuery, activeMovies, allShowtimes, targetDate, timeFilterStart, timeFilterEnd, timeReason);
    }

    private async Task<List<AiMovieRecommendationDto>?> TryCallGeminiAsync(
        string userQuery, 
        string normalizedQuery,
        List<Movie> movies, 
        List<Showtime> allShowtimes, 
        DateTime targetDate, 
        TimeSpan timeStart, 
        TimeSpan timeEnd, 
        string timeReason)
    {
        var moviesSummary = string.Join("\n", movies.Select(m => 
        {
            var mShowtimes = allShowtimes
                .Where(st => st.MovieId == m.MovieId)
                .OrderBy(st => st.StartTime)
                .Select(st => $"{st.StartTime:HH:mm} (ngay {st.StartTime:dd/MM})")
                .Distinct()
                .ToList();
            var showtimesStr = mShowtimes.Any() ? string.Join(", ", mShowtimes) : "Hien chua co lich chieu";
            return $"- ID {m.MovieId}: \"{m.Title}\" (Dao dien: {m.Director}, Dien vien: {m.Cast}, The loai: {m.Genre}, Do tuoi: {m.AgeRating}, Thoi luong: {m.DurationMinutes} phut). Lich chieu: [{showtimesStr}]. Mo ta: {m.Description}";
        }));

        var systemPrompt = 
            "Ban la Tro ly AI dien anh thong minh cua rap Starlight Cinema Hue (tuong tu nhu ChatGPT chuyen ve dien anh).\n" +
            "Duoi day la toan bo danh sach cac bo phim va lich chieu thuc te tai rap:\n" + moviesSummary + "\n\n" +
            "Khach hang hoi / tro chuyen: \"" + userQuery + "\"\n\n" +
            "Nhiem vu cua ban:\n" +
            "1. Doc hieu toan dien ngu nghia, thoi gian (chieu muon nhat, chieu som nhat, buoi sang, trua, toi, khuya...), cam xuc (xa stress, khoc, cuoi, that tinh...), dien vien, dao dien, hoac bat ky phong cach dien dat tu nhien nao cua khach hang (ke ca tieng long, vi von, tu ngu la).\n" +
            "2. Tu danh sach phim va lich chieu o tren, chon ra tu 1 den 4 bo phim phu hop nhat voi mong muon cua khach hang.\n" +
            "3. Voi moi phim duoc chon, cham diem phu hop (score tu 70 den 99) va viet 1 cau giai thich ly do ngan gon (reason), tu nhien, than thien bang tieng Viet xung 'minh' hoac 'rap'. Neu khach hoi ve gio chieu (vi du: chieu muon nhat, chieu sau 21h...), ban BAT BUOC phai chi ro gio chieu cu the cua phim do trong cau giai thich (vi du: 'Suat 23:55 la suat muon nhat rap hom nay...').\n" +
            "4. BAT BUOC chi tra ve dung dinh dang JSON thuan tuy (khong kem chu thua mo dau hay ket thuc), theo mau:\n" +
            "[{\"movieId\": 1, \"score\": 98, \"reason\": \"Cau giai thich than thien cho khach hang...\"}]";

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = systemPrompt }
                    }
                }
            }
        };

        var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_geminiModel}:generateContent?key={_effectiveApiKey}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = await _httpClient!.PostAsync(url, jsonContent, cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var responseString = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseString);
        
        var textResult = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(textResult)) return null;

        var jsonClean = CleanJson(textResult);
        using var jsonParsed = JsonDocument.Parse(jsonClean);

        var recommendations = new List<AiMovieRecommendationDto>();
        bool isLateQuery = normalizedQuery.Contains("muon") || normalizedQuery.Contains("khuya") || normalizedQuery.Contains("toi muon");

        foreach (var item in jsonParsed.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("movieId", out var movieIdProp))
            {
                int movieId = movieIdProp.GetInt32();
                var movie = movies.FirstOrDefault(m => m.MovieId == movieId);
                if (movie != null)
                {
                    double score = item.TryGetProperty("score", out var s) ? s.GetDouble() : 90;
                    string reason = item.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";

                    var movieShowtimes = allShowtimes
                        .Where(st => st.MovieId == movie.MovieId)
                        .Where(st => st.StartTime.Date == targetDate.Date)
                        .Where(st => st.StartTime.TimeOfDay >= timeStart && st.StartTime.TimeOfDay <= timeEnd);

                    var showtimesList = isLateQuery
                        ? movieShowtimes.OrderByDescending(st => st.StartTime).ToList()
                        : movieShowtimes.OrderBy(st => st.StartTime).ToList();

                    if (!showtimesList.Any())
                    {
                        var fallbackShowtimes = allShowtimes.Where(st => st.MovieId == movie.MovieId);
                        showtimesList = isLateQuery
                            ? fallbackShowtimes.OrderByDescending(st => st.StartTime).Take(4).ToList()
                            : fallbackShowtimes.OrderBy(st => st.StartTime).Take(4).ToList();
                    }

                    recommendations.Add(new AiMovieRecommendationDto
                    {
                        Movie = movie,
                        RelevanceScore = score,
                        MatchReason = string.IsNullOrWhiteSpace(timeReason) ? reason : $"{reason}. {timeReason}",
                        SuggestedShowtimes = showtimesList
                    });
                }
            }
        }

        return recommendations.OrderByDescending(r => r.RelevanceScore).ToList();
    }

    private string CleanJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "[]";
        text = text.Trim();
        int startIndex = text.IndexOf('[');
        int endIndex = text.LastIndexOf(']');
        if (startIndex >= 0 && endIndex > startIndex)
        {
            return text.Substring(startIndex, endIndex - startIndex + 1);
        }
        return text;
    }

    private List<AiMovieRecommendationDto> RunSmartSemanticSearch(
        string originalQuery,
        string normalizedQuery, 
        List<Movie> activeMovies, 
        List<Showtime> allShowtimes, 
        DateTime targetDate, 
        TimeSpan timeFilterStart, 
        TimeSpan timeFilterEnd, 
        string timeReason)
    {
        var recommendations = new List<AiMovieRecommendationDto>();

        // Kiểm tra câu hỏi tổng quan (chào hỏi, hỏi danh sách phim, phim hot...)
        bool isGeneralBrowseQuery = IsGeneralBrowseQuery(normalizedQuery);

        foreach (var movie in activeMovies)
        {
            var (score, matchReasons) = CalculateSemanticScore(movie, normalizedQuery, isGeneralBrowseQuery);

            if (score > 15.0)
            {
                var movieShowtimes = allShowtimes
                    .Where(s => s.MovieId == movie.MovieId)
                    .Where(s => s.StartTime.Date == targetDate.Date)
                    .Where(s => s.StartTime.TimeOfDay >= timeFilterStart && s.StartTime.TimeOfDay <= timeFilterEnd)
                    .OrderBy(s => s.StartTime)
                    .ToList();

                if (!movieShowtimes.Any())
                {
                    movieShowtimes = allShowtimes
                        .Where(s => s.MovieId == movie.MovieId)
                        .OrderBy(s => s.StartTime)
                        .Take(4)
                        .ToList();
                }

                var reasonBuilder = new StringBuilder();
                if (matchReasons.Any())
                {
                    reasonBuilder.Append(string.Join(". ", matchReasons));
                }
                else
                {
                    reasonBuilder.Append($"Nội dung phù hợp với mô tả: \"{originalQuery}\"");
                }

                if (!string.IsNullOrEmpty(timeReason) && movieShowtimes.Any())
                {
                    reasonBuilder.Append($". {timeReason}");
                }

                recommendations.Add(new AiMovieRecommendationDto
                {
                    Movie = movie,
                    RelevanceScore = Math.Min(99.0, Math.Round(score, 1)),
                    MatchReason = reasonBuilder.ToString(),
                    SuggestedShowtimes = movieShowtimes
                });
            }
        }

        // Nếu không khớp phim nào (VD người dùng gõ từ khóa không có thật như 'phim ma', 'avatar', 'xyz'...)
        // Trợ lý AI tự động gợi ý các phim bom tấn đang chiếu để khách hàng không bị cụt hứng!
        if (!recommendations.Any() && activeMovies.Any())
        {
            foreach (var movie in activeMovies.Take(3))
            {
                var fallbackShowtimes = allShowtimes
                    .Where(s => s.MovieId == movie.MovieId)
                    .OrderBy(s => s.StartTime)
                    .Take(4)
                    .ToList();

                recommendations.Add(new AiMovieRecommendationDto
                {
                    Movie = movie,
                    RelevanceScore = 75.0,
                    MatchReason = $"Rạp Starlight Cinema Huế hiện đang chiếu bom tấn nổi bật này, rất đáng để bạn trải nghiệm!",
                    SuggestedShowtimes = fallbackShowtimes
                });
            }
        }

        return recommendations.OrderByDescending(r => r.RelevanceScore).ToList();
    }

    private bool IsGeneralBrowseQuery(string query)
    {
        return query.Contains("co phim gi") || query.Contains("phim gi") || query.Contains("danh sach phim") ||
               query.Contains("phim dang chieu") || query.Contains("phim hay") || query.Contains("phim hot") ||
               query.Contains("tat ca phim") || query.Contains("chao") || query.Contains("hello") ||
               query.Contains("hi") || query.Contains("tu van") || query.Contains("goi y") ||
               query.Contains("hom nay co gi") || query.Contains("rap co gi");
    }

    private (double Score, List<string> Reasons) CalculateSemanticScore(Movie movie, string query, bool isGeneralBrowse)
    {
        double score = 0;
        var reasons = new List<string>();

        var titleNorm = NormalizeText(movie.Title);
        var genreNorm = NormalizeText(movie.Genre);
        var descNorm = NormalizeText(movie.Description ?? "");
        var directorNorm = NormalizeText(movie.Director ?? "");
        var castNorm = NormalizeText(movie.Cast ?? "");
        var rating = (movie.AgeRating ?? "").ToUpperInvariant();

        // 1. Nếu là câu hỏi chung (danh sách phim, phim hot...) -> Gợi ý toàn bộ phim với điểm số cân bằng
        if (isGeneralBrowse)
        {
            score += 70.0;
            reasons.Add("Phim bom tấn đang chiếu và được nhiều khán giả quan tâm tại rạp");
            return (score, reasons);
        }

        // 2. Khớp trực tiếp Tên phim
        if (query.Contains(titleNorm) || (!string.IsNullOrEmpty(titleNorm) && titleNorm.Length > 2 && query.Contains(titleNorm)))
        {
            score += 90;
            reasons.Add($"Khớp chính xác tên phim: \"{movie.Title}\"");
        }

        // Đặc biệt tên phim ngắn (Mai, Po, Dune, Kong...)
        if (titleNorm == "mai" && (query.Contains("phim mai") || query.Contains("xem mai") || query == "mai"))
        {
            score += 95;
            reasons.Add("Khớp chính xác bộ phim điện ảnh \"Mai\"");
        }
        if (titleNorm.Contains("godzilla") && (query.Contains("godzilla") || query.Contains("kong") || query.Contains("titan") || query.Contains("quai thu")))
        {
            score += 85;
            reasons.Add("Siêu bom tấn quái thú Godzilla x Kong");
        }
        if (titleNorm.Contains("kung fu panda") && (query.Contains("panda") || query.Contains("po") || query.Contains("gau truc") || query.Contains("kungfu") || query.Contains("kung fu")))
        {
            score += 85;
            reasons.Add("Hành trình hài hước của Gấu Po trong Kung Fu Panda 4");
        }
        if (titleNorm.Contains("dune") && (query.Contains("dune") || query.Contains("hanh tinh cat") || query.Contains("sau cat") || query.Contains("paul")))
        {
            score += 85;
            reasons.Add("Kiệt tác sử thi viễn tưởng Dune: Hành Tinh Cát");
        }
        if (titleNorm.Contains("lat mat") && (query.Contains("lat mat") || query.Contains("dieu uoc") || query.Contains("me gia")))
        {
            score += 85;
            reasons.Add("Tác phẩm gia đình xúc động Lật Mặt 7");
        }

        // 3. Khớp Đạo diễn & Diễn viên
        if (!string.IsNullOrEmpty(directorNorm) && query.Contains(directorNorm))
        {
            score += 75;
            reasons.Add($"Phim của đạo diễn {movie.Director}");
        }
        else if (query.Contains("tran thanh") && (directorNorm.Contains("tran thanh") || castNorm.Contains("tran thanh")))
        {
            score += 75;
            reasons.Add("Phim có sự tham gia của Trấn Thành");
        }
        else if (query.Contains("ly hai") && (directorNorm.Contains("ly hai") || castNorm.Contains("ly hai")))
        {
            score += 75;
            reasons.Add("Phim của đạo diễn Lý Hải");
        }
        else if (query.Contains("zendaya") && castNorm.Contains("zendaya"))
        {
            score += 70;
            reasons.Add("Phim có sự tham gia của ngôi sao Zendaya");
        }
        else if (query.Contains("jack black") && castNorm.Contains("jack black"))
        {
            score += 70;
            reasons.Add("Phim do Jack Black lồng tiếng");
        }
        else if (query.Contains("phuong anh dao") && castNorm.Contains("phuong anh dao"))
        {
            score += 70;
            reasons.Add("Phim có Phương Anh Đào đóng chính");
        }

        // 4. Nhóm Gia đình, Trẻ em, Thiếu nhi, Hài hước
        bool isFamilyQuery = query.Contains("gia dinh") || query.Contains("tre em") || 
                             query.Contains("nhe nhang") || query.Contains("hoat hinh") ||
                             query.Contains("thieu nhi") || query.Contains("vui ve") ||
                             query.Contains("hai huoc") || query.Contains("cuoi") ||
                             query.Contains("xa stress") || query.Contains("giai tri");

        if (isFamilyQuery)
        {
            if (rating == "P" || rating == "K")
            {
                score += 45;
                reasons.Add($"Phim có nhãn {rating} (phù hợp mọi lứa tuổi, gia đình và trẻ em)");
            }
            else if (rating == "T18")
            {
                score -= 40;
            }

            if (genreNorm.Contains("hoat hinh") || genreNorm.Contains("gia dinh") || genreNorm.Contains("hai"))
            {
                score += 40;
                reasons.Add("Thể loại hoạt hình, gia đình vui vẻ, giàu cảm xúc");
            }

            if (descNorm.Contains("gia dinh") || descNorm.Contains("me") || descNorm.Contains("con") || descNorm.Contains("tre em"))
            {
                score += 25;
            }
        }

        // 5. Nhóm Hành động, Bom tấn, Viễn tưởng, Kịch tính
        bool isActionQuery = query.Contains("hanh dong") || query.Contains("kich tinh") || 
                             query.Contains("bom tan") || query.Contains("chien dau") ||
                             query.Contains("vien tuong") || query.Contains("khoa hoc") ||
                             query.Contains("danh nhau") || query.Contains("quai vat") ||
                             query.Contains("gay can") || query.Contains("man nhan") ||
                             query.Contains("ky xao");

        if (isActionQuery)
        {
            if (genreNorm.Contains("hanh dong") || genreNorm.Contains("vien tuong") || genreNorm.Contains("phieu luu"))
            {
                score += 45;
                reasons.Add("Thể loại hành động, viễn tưởng mãn nhãn kỹ xảo");
            }
            if (descNorm.Contains("danh nhau") || descNorm.Contains("doi dau") || descNorm.Contains("huy diet") || descNorm.Contains("titan"))
            {
                score += 30;
                reasons.Add("Các pha giao tranh nghẹt thở và kịch tính");
            }
        }

        // 6. Nhóm Tình cảm, Tâm lý, Lãng mạn, Hẹn hò
        bool isRomanceQuery = query.Contains("tinh cam") || query.Contains("tam ly") || 
                              query.Contains("lang man") || query.Contains("hen ho") ||
                              query.Contains("sau lang") || query.Contains("tinh yeu") ||
                              query.Contains("nguoi yeu") || query.Contains("cap doi") ||
                              query.Contains("nguoi lon") || query.Contains("18+");

        if (isRomanceQuery)
        {
            if (genreNorm.Contains("tam ly") || genreNorm.Contains("tinh cam"))
            {
                score += 50;
                reasons.Add("Nội dung tâm lý tình cảm sâu sắc, chạm đến cảm xúc");
            }
            if (rating == "T18")
            {
                score += 20;
                reasons.Add("Phim nhãn T18 phù hợp cho các cặp đôi trưởng thành");
            }
        }

        // 7. Nhóm Cảm động, Nước mắt, Ý nghĩa gia đình
        bool isEmotionalQuery = query.Contains("cam dong") || query.Contains("khoc") || 
                                query.Contains("sau lang") || query.Contains("y nghia") ||
                                query.Contains("me") || query.Contains("nuoc mat");

        if (isEmotionalQuery)
        {
            if (descNorm.Contains("me") || descNorm.Contains("tinh cam") || genreNorm.Contains("chinh kich"))
            {
                score += 45;
                reasons.Add("Câu chuyện nhân văn cảm động, lấy đi nhiều nước mắt khán giả");
            }
        }

        // 8. Quét từng từ đơn lẻ (Token Matching)
        var queryWords = query.Split(new[] { ' ', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
                              .Where(w => w.Length >= 2)
                              .ToList();

        int matchedWords = 0;
        foreach (var word in queryWords)
        {
            if (titleNorm.Contains(word))
            {
                score += 25;
                matchedWords++;
            }
            else if (genreNorm.Contains(word))
            {
                score += 15;
                matchedWords++;
            }
            else if (descNorm.Contains(word))
            {
                score += 10;
                matchedWords++;
            }
            else if (directorNorm.Contains(word) || castNorm.Contains(word))
            {
                score += 15;
                matchedWords++;
            }
        }

        if (matchedWords > 0 && !reasons.Any())
        {
            reasons.Add($"Khớp {matchedWords} đặc trưng từ khóa trong thông tin phim");
        }

        return (score, reasons);
    }

    private (TimeSpan Start, TimeSpan End, string Reason) DetectTimePreference(string query)
    {
        if (query.Contains("toi nay") || query.Contains("buoi toi") || query.Contains("toi"))
        {
            return (new TimeSpan(18, 0, 0), new TimeSpan(21, 30, 0), "Ưu tiên các suất chiếu buổi tối (18h00 - 21h30)");
        }
        if (query.Contains("dem") || query.Contains("muon") || query.Contains("khuya"))
        {
            return (new TimeSpan(21, 30, 0), new TimeSpan(23, 59, 59), "Ưu tiên các suất chiếu muộn sau 21h30");
        }
        if (query.Contains("chieu") || query.Contains("buoi chieu"))
        {
            return (new TimeSpan(13, 0, 0), new TimeSpan(18, 0, 0), "Ưu tiên các suất chiếu buổi chiều (13h00 - 18h00)");
        }
        if (query.Contains("sang") || query.Contains("buoi sang"))
        {
            return (new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0), "Ưu tiên các suất chiếu buổi sáng (08h00 - 12h00)");
        }

        return (TimeSpan.Zero, new TimeSpan(23, 59, 59), string.Empty);
    }

    private DateTime DetectTargetDate(string query)
    {
        if (query.Contains("ngay mai") || query.Contains("hom sau"))
        {
            return DateTime.Today.AddDays(1);
        }
        return DateTime.Today;
    }

    private string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        text = text.ToLowerInvariant().Trim();
        string normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();

        foreach (var c in normalized)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        string result = sb.ToString().Normalize(NormalizationForm.FormC);
        result = result.Replace('đ', 'd').Replace('Đ', 'd');
        return Regex.Replace(result, @"\s+", " ");
    }
}
