using Cinema.CoreBusiness.Utilities;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cinema.CoreBusiness.Models;
using Cinema.UseCases.DTOs;
using Cinema.UseCases.PluginInterfaces.DataStore;

namespace Cinema.UseCases.AI;

/// <summary>
/// Trien khai UseCase tim kiem phim thong minh (Semantic Search) va Tro ly AI dien anh.
/// Ho tro tich hop truc tiep Google Gemini API voi co che tu dong chuyen doi Model khi cham han muc (Quota),
/// va co che Fallback thong minh cuc bo (Offline).
/// </summary>
public class SemanticMovieSearchUseCase : ISemanticMovieSearchUseCase
{
    private readonly IMovieRepository _movieRepository;
    private readonly IShowtimeRepository _showtimeRepository;
    private readonly HttpClient? _httpClient;
    private readonly string _primaryModel;
    private readonly string _effectiveApiKey;

    // Danh sach cac Model Gemini duoc ho tro de du phong khi model chinh het quota (429)
    private static readonly string[] FallbackModels = new[]
    {
        "gemini-2.5-flash-lite",
        "gemini-2.5-flash",
        "gemini-flash-lite-latest",
        "gemini-3-flash-preview"
    };

    public SemanticMovieSearchUseCase(
        IMovieRepository movieRepository,
        IShowtimeRepository showtimeRepository,
        HttpClient? httpClient = null,
        string? geminiApiKey = null,
        string? geminiModel = null)
    {
        _movieRepository = movieRepository;
        _showtimeRepository = showtimeRepository;
        _httpClient = httpClient ?? new HttpClient();
        _primaryModel = string.IsNullOrWhiteSpace(geminiModel) ? "gemini-2.5-flash-lite" : geminiModel;
        _effectiveApiKey = ResolveApiKey(geminiApiKey);
    }

    private static string ResolveApiKey(string? apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey) &&
            !apiKey.Contains("YOUR_GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase))
        {
            return apiKey;
        }

        var processKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.Process);
        if (!string.IsNullOrWhiteSpace(processKey)) return processKey;

        var userKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(userKey)) return userKey;

        var machineKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.Machine);
        if (!string.IsNullOrWhiteSpace(machineKey)) return machineKey;

        return string.Empty;
    }

    /// <summary>
    /// Hoi thoai thuc te voi Tro ly AI (tuong thich 100% ChatGPT/Gemini)
    /// </summary>
    public async Task<AiChatResponseDto> ChatAsync(string naturalLanguageQuery, DateTime? preferredDate = null)
    {
        if (string.IsNullOrWhiteSpace(naturalLanguageQuery))
        {
            return new AiChatResponseDto
            {
                ReplyMessage = "Chào bạn! Mình là Trợ lý AI Starlight Cinema. Bạn muốn tìm phim theo thể loại nào, diễn viên nào hay suất chiếu mấy giờ hôm nay nè?",
                Recommendations = new List<AiMovieRecommendationDto>()
            };
        }

        string originalQuery = naturalLanguageQuery.Trim();
        string normalizedQuery = NormalizeText(originalQuery);

        var targetDate = preferredDate?.Date ?? DetectTargetDate(normalizedQuery);
        var (timeStart, timeEnd, timeReason) = DetectTimePreference(normalizedQuery);

        var activeMovies = (await _movieRepository.GetAllMoviesAsync(activeOnly: true)).ToList();
        var allShowtimes = (await _showtimeRepository.GetAllUpcomingShowtimesAsync()).ToList();

        // Loc suat chieu: NEU LA HOM NAY thi chi lay cac suat chieu TRONG TUONG LAI (chua chieu)
        var upcomingShowtimes = allShowtimes
            .Where(s => s.StartTime.Date == targetDate.Date)
            .Where(s => targetDate.Date > DateTime.Today || s.StartTime > DateTime.Now)
            .ToList();

        if (!string.IsNullOrWhiteSpace(_effectiveApiKey))
        {
            // Thu lan luot model chinh, neu gap 429 quota thi tu dong chuyen sang model du phong
            var modelsToTry = new List<string> { _primaryModel };
            foreach (var m in FallbackModels)
            {
                if (!modelsToTry.Contains(m)) modelsToTry.Add(m);
            }

            foreach (var model in modelsToTry)
            {
                try
                {
                    var geminiResult = await CallGeminiChatAsync(
                        originalQuery,
                        normalizedQuery,
                        activeMovies,
                        upcomingShowtimes,
                        targetDate,
                        timeStart,
                        timeEnd,
                        _effectiveApiKey,
                        model);

                    if (geminiResult != null)
                    {
                        return geminiResult;
                    }
                }
                catch
                {
                    // Tiep tuc thu model tiep theo
                }
            }
        }

        // Che do Offline / Smart Local Chat khi khong the ket noi den bat ky model Gemini nao
        return RunSmartLocalChat(
            originalQuery,
            normalizedQuery,
            activeMovies,
            upcomingShowtimes,
            targetDate,
            timeStart,
            timeEnd,
            timeReason);
    }

    /// <summary>
    /// Tim kiem phim (tuong thich nguoc cho cac noi chi can danh sach phim)
    /// </summary>
    public async Task<List<AiMovieRecommendationDto>> ExecuteAsync(string naturalLanguageQuery, DateTime? preferredDate = null)
    {
        var chatResult = await ChatAsync(naturalLanguageQuery, preferredDate);
        return chatResult.Recommendations;
    }

    private async Task<AiChatResponseDto?> CallGeminiChatAsync(
        string userQuery,
        string normalizedQuery,
        List<Movie> movies,
        List<Showtime> upcomingShowtimes,
        DateTime targetDate,
        TimeSpan timeStart,
        TimeSpan timeEnd,
        string apiKey,
        string modelName)
    {
        var moviesSummary = string.Join("\n", movies.Select(m =>
        {
            var movieShowtimes = upcomingShowtimes
                .Where(st => st.MovieId == m.MovieId)
                .OrderBy(st => st.StartTime)
                .GroupBy(st => st.StartTime)
                .Select(g => g.First())
                .ToList();

            string showtimeListStr = movieShowtimes.Any()
                ? string.Join(", ", movieShowtimes.Select(st => $"{st.StartTime:HH:mm} ({st.AuditoriumName ?? "Rạp"})"))
                : "Hôm nay đã hết suất chiếu";

            return $"[{m.MovieId}] {m.Title} | Thể loại: {m.Genre} | Đạo diễn: {m.Director} | Diễn viên: {m.Cast} | Độ tuổi: {m.AgeRating} | Suất chiếu thực tế hôm nay: {showtimeListStr}";
        }));

        var systemPrompt =
            "Bạn là Trợ lý AI điện ảnh thông minh của rạp Starlight Cinema Huế (tương tự như ChatGPT phiên bản chuyên gia rạp phim).\n" +
            "Dưới đây là danh sách phim và suất chiếu THỰC TẾ hôm nay tại rạp:\n" + moviesSummary + "\n\n" +
            "Khách hàng hỏi: \"" + userQuery + "\"\n\n" +
            "Quy tắc nghiệp vụ THỰC TẾ bắt buộc:\n" +
            "1. Nếu khách hàng chỉ chào hỏi ('hello', 'chào', 'hi'), hỏi bạn là ai, hoặc trò chuyện chung/ngoài lề (thời tiết, ăn uống...):\n" +
            "   - Hãy trả lời chào hỏi tự nhiên, lịch sự, thân thiện và khéo léo giới thiệu bạn là trợ lý rạp Starlight Cinema.\n" +
            "   - Mục 'movies' PHẢI là mảng rỗng [] (TUYỆT ĐỐI KHÔNG tự tiện gắn phim vào khi khách chỉ chào hỏi!).\n" +
            "2. Nếu khách hàng hỏi tìm phim (theo thể loại, diễn viên, tâm trạng, thời gian, suất muộn, suất sớm...):\n" +
            "   - Chọn ĐÚNG các phim thực sự phù hợp từ danh sách trên.\n" +
            "   - Viết câu trả lời 'reply' tự nhiên, chi tiết như một nhân viên tư vấn nhiệt tình của rạp (nhắc đến tên phim, suất chiếu).\n" +
            "   - Mục 'movies' chứa danh sách các phim phù hợp với movieId, score (từ 60 đến 100), reason (lý do cụ thể vì sao phim này hợp với khách).\n" +
            "3. TUYỆT ĐỐI KHÔNG bịa đặt phim hoặc suất chiếu không có trong danh sách trên.\n" +
            "4. BẮT BUỘC chỉ trả về định dạng JSON thuần túy (không kèm Markdown code block thừa thãi), theo mẫu:\n" +
            "{\n" +
            "  \"reply\": \"Câu trả lời trò chuyện tự nhiên của AI...\",\n" +
            "  \"movies\": [\n" +
            "    { \"movieId\": 1, \"score\": 95, \"reason\": \"Lý do phù hợp...\" }\n" +
            "  ]\n" +
            "}";

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
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = await _httpClient!.PostAsync(url, jsonContent, cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            return null; // Model nay bi loi (vd 429 quota), de vong lap thu model tiep theo
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

        var jsonClean = CleanJsonObject(textResult);
        using var jsonParsed = JsonDocument.Parse(jsonClean);

        string replyMessage = "Chào bạn! Tôi có thể hỗ trợ gì cho bạn về các bộ phim tại Starlight Cinema?";
        if (jsonParsed.RootElement.TryGetProperty("reply", out var replyProp))
        {
            replyMessage = replyProp.GetString() ?? replyMessage;
        }

        var recommendations = new List<AiMovieRecommendationDto>();
        bool isLateQuery = normalizedQuery.Contains("muon") || normalizedQuery.Contains("khuya") || normalizedQuery.Contains("toi muon");

        if (jsonParsed.RootElement.TryGetProperty("movies", out var moviesArray) && moviesArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in moviesArray.EnumerateArray())
            {
                if (item.TryGetProperty("movieId", out var movieIdProp))
                {
                    int movieId = movieIdProp.GetInt32();
                    var movie = movies.FirstOrDefault(m => m.MovieId == movieId);
                    if (movie != null)
                    {
                        double score = item.TryGetProperty("score", out var s) ? s.GetDouble() : 90;
                        string reason = item.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";

                        var movieShowtimes = upcomingShowtimes
                            .Where(st => st.MovieId == movie.MovieId)
                            .Where(st => st.StartTime.Date == targetDate.Date);

                        if (timeStart > TimeSpan.Zero || timeEnd < new TimeSpan(23, 59, 59))
                        {
                            var filteredInWindow = movieShowtimes.Where(st => st.StartTime.TimeOfDay >= timeStart && st.StartTime.TimeOfDay <= timeEnd).ToList();
                            if (filteredInWindow.Any())
                            {
                                movieShowtimes = filteredInWindow;
                            }
                        }

                        var showtimesList = (isLateQuery
                            ? movieShowtimes.OrderByDescending(st => st.StartTime)
                            : movieShowtimes.OrderBy(st => st.StartTime))
                            .GroupBy(st => st.StartTime)
                            .Select(g => g.First())
                            .ToList();

                        recommendations.Add(new AiMovieRecommendationDto
                        {
                            Movie = movie,
                            RelevanceScore = score,
                            MatchReason = reason,
                            SuggestedShowtimes = showtimesList
                        });
                    }
                }
            }
        }

        return new AiChatResponseDto
        {
            ReplyMessage = replyMessage,
            Recommendations = recommendations.OrderByDescending(r => r.RelevanceScore).ToList()
        };
    }

    private string CleanJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "{}";
        text = text.Trim();
        int startIndex = text.IndexOf('{');
        int endIndex = text.LastIndexOf('}');
        if (startIndex >= 0 && endIndex > startIndex)
        {
            return text.Substring(startIndex, endIndex - startIndex + 1);
        }
        return "{}";
    }

    private AiChatResponseDto RunSmartLocalChat(
        string originalQuery,
        string normalizedQuery,
        List<Movie> activeMovies,
        List<Showtime> upcomingShowtimes,
        DateTime targetDate,
        TimeSpan timeFilterStart,
        TimeSpan timeFilterEnd,
        string timeReason)
    {
        // 1. Kiem tra chao hoi hoac tro chuyen chung chung
        bool isGreeting = normalizedQuery == "chao" || normalizedQuery.StartsWith("chao ") ||
                          normalizedQuery == "hello" || normalizedQuery == "hi" || normalizedQuery == "alo" ||
                          normalizedQuery.Contains("ban la ai") || normalizedQuery.Contains("ai day") ||
                          normalizedQuery.Contains("xin chao") || normalizedQuery.Contains("tro ly");

        if (isGreeting)
        {
            return new AiChatResponseDto
            {
                ReplyMessage = "Chào bạn! Tôi là Starlight AI - trợ lý điện ảnh rạp Starlight Cinema. Hôm nay rạp có nhiều suất chiếu phim đặc sắc (hành động, tình cảm, hài hước, hoạt hình). Bạn muốn tìm phim theo thể loại nào hoặc đi xem vào khung giờ mấy giờ nè?",
                Recommendations = new List<AiMovieRecommendationDto>()
            };
        }

        // 2. Kiem tra hoi khung gio cu the (chi khi khong phai hoi the loai)
        bool hasSpecificTimeFilter = (timeFilterStart > TimeSpan.Zero || timeFilterEnd < new TimeSpan(23, 59, 59));
        bool hasGenreKeyword = normalizedQuery.Contains("hanh dong") || normalizedQuery.Contains("tinh cam") ||
                               normalizedQuery.Contains("hoat hinh") || normalizedQuery.Contains("gia dinh") ||
                               normalizedQuery.Contains("hai");

        if (hasSpecificTimeFilter && !hasGenreKeyword)
        {
            var moviesWithTimeShowtimes = activeMovies
                .Select(m => new
                {
                    Movie = m,
                    Showtimes = upcomingShowtimes
                        .Where(s => s.MovieId == m.MovieId &&
                                    s.StartTime.Date == targetDate.Date &&
                                    s.StartTime.TimeOfDay >= timeFilterStart &&
                                    s.StartTime.TimeOfDay <= timeFilterEnd)
                        .OrderBy(s => s.StartTime)
                        .GroupBy(s => s.StartTime)
                        .Select(g => g.First())
                        .ToList()
                })
                .Where(x => x.Showtimes.Any())
                .ToList();

            if (moviesWithTimeShowtimes.Any())
            {
                var recs = new List<AiMovieRecommendationDto>();
                double score = 95.0;
                foreach (var item in moviesWithTimeShowtimes)
                {
                    var showtimeStr = string.Join(", ", item.Showtimes.Select(s => s.StartTime.ToString("HH:mm")));
                    recs.Add(new AiMovieRecommendationDto
                    {
                        Movie = item.Movie,
                        RelevanceScore = score,
                        MatchReason = $"Phim có suất chiếu trong khung giờ bạn tìm: lúc {showtimeStr}.",
                        SuggestedShowtimes = item.Showtimes
                    });
                    score -= 5.0;
                }

                return new AiChatResponseDto
                {
                    ReplyMessage = $"Tôi tìm thấy {recs.Count} bộ phim có suất chiếu phù hợp trong khung giờ bạn yêu cầu:",
                    Recommendations = recs
                };
            }
            else
            {
                return new AiChatResponseDto
                {
                    ReplyMessage = $"Rất tiếc hiện tại rạp không còn suất chiếu nào trong khung giờ này hôm nay. Bạn có thể tham khảo các suất chiếu khác trong ngày nhé!",
                    Recommendations = new List<AiMovieRecommendationDto>()
                };
            }
        }

        // 3. Cham diem theo the loai, dien vien, dao dien, tu khoa
        var scoredList = new List<AiMovieRecommendationDto>();
        foreach (var movie in activeMovies)
        {
            var (score, matchReasons) = CalculateLocalMovieMatch(movie, normalizedQuery);
            if (score >= 50.0)
            {
                var movieShowtimes = upcomingShowtimes
                    .Where(s => s.MovieId == movie.MovieId && s.StartTime.Date == targetDate.Date)
                    .OrderBy(s => s.StartTime)
                    .GroupBy(s => s.StartTime)
                    .Select(g => g.First())
                    .ToList();

                scoredList.Add(new AiMovieRecommendationDto
                {
                    Movie = movie,
                    RelevanceScore = Math.Min(99.0, Math.Round(score, 1)),
                    MatchReason = matchReasons.Any() ? string.Join("; ", matchReasons) : "Phim phù hợp với tìm kiếm của bạn",
                    SuggestedShowtimes = movieShowtimes
                });
            }
        }

        if (scoredList.Any())
        {
            var ordered = scoredList.OrderByDescending(r => r.RelevanceScore).Take(4).ToList();
            return new AiChatResponseDto
            {
                ReplyMessage = $"Dạ, tôi tìm thấy {ordered.Count} bộ phim phù hợp nhất với yêu cầu của bạn:",
                Recommendations = ordered
            };
        }

        // 4. Neu khong tim thay phim nao phu hop
        return new AiChatResponseDto
        {
            ReplyMessage = $"Rất tiếc hiện tại rạp chưa có suất chiếu phù hợp với yêu cầu '{originalQuery}'. Bạn có thể tham khảo các phim bom tấn đang chiếu tối nay tại rạp như Godzilla x Kong, Dune 2, Lật Mặt 7 hoặc Mai nhé!",
            Recommendations = new List<AiMovieRecommendationDto>()
        };
    }

    private (double Score, List<string> Reasons) CalculateLocalMovieMatch(Movie movie, string query)
    {
        double score = 20.0;
        var reasons = new List<string>();

        string titleNorm = NormalizeText(movie.Title);
        string genreNorm = NormalizeText(movie.Genre);
        string descNorm = NormalizeText(movie.Description ?? string.Empty);
        string directorNorm = NormalizeText(movie.Director ?? string.Empty);
        string castNorm = NormalizeText(movie.Cast ?? string.Empty);

        // 1. Khop ten phim
        if (titleNorm.Contains(query) || query.Contains(titleNorm))
        {
            score += 65;
            reasons.Add($"Khớp tên phim: {movie.Title}");
        }

        // 2. Nhan vat / Thuong hieu
        if (query.Contains("gau po") && (titleNorm.Contains("panda") || descNorm.Contains("po")))
        {
            score += 75;
            reasons.Add("Phim chú gấu Po (Kung Fu Panda)");
        }
        else if ((query.Contains("kong") || query.Contains("godzilla")) && (titleNorm.Contains("godzilla") || titleNorm.Contains("kong") || descNorm.Contains("titan")))
        {
            score += 75;
            reasons.Add("Đại chiến quái thú Godzilla x Kong");
        }
        else if (query.Contains("sau cat") && (titleNorm.Contains("dune") || descNorm.Contains("cat")))
        {
            score += 75;
            reasons.Add("Siêu phẩm viễn tưởng Xứ Cát (Dune 2)");
        }

        // 3. Dao dien / Dien vien
        if (query.Contains("tran thanh") && (directorNorm.Contains("tran thanh") || castNorm.Contains("tran thanh")))
        {
            score += 70;
            reasons.Add("Tác phẩm của đạo diễn Trấn Thành");
        }
        else if (query.Contains("ly hai") && (directorNorm.Contains("ly hai") || castNorm.Contains("ly hai")))
        {
            score += 70;
            reasons.Add("Phim của đạo diễn Lý Hải");
        }

        // 4. The loai: Hanh dong / Vien tuong
        bool isActionQuery = query.Contains("hanh dong") || query.Contains("bom tan") ||
                             query.Contains("chien dau") || query.Contains("vien tuong") ||
                             query.Contains("danh nhau") || query.Contains("quai vat");

        if (isActionQuery)
        {
            if (genreNorm.Contains("hanh dong") || genreNorm.Contains("vien tuong"))
            {
                score += 55;
                reasons.Add("Thể loại hành động, viễn tưởng đỉnh cao");
            }
        }

        // 5. The loai: Gia dinh / Hoat hinh / Hai huoc
        bool isFamilyQuery = query.Contains("gia dinh") || query.Contains("tre em") ||
                             query.Contains("hoat hinh") || query.Contains("hai huoc") ||
                             query.Contains("cuoi") || query.Contains("xa stress");

        if (isFamilyQuery)
        {
            if (genreNorm.Contains("hoat hinh") || genreNorm.Contains("hai") || genreNorm.Contains("gia dinh"))
            {
                score += 50;
                reasons.Add("Thể loại gia đình, hoạt hình vui vẻ");
            }
        }

        // 6. The loai: Tinh cam / Tam ly
        bool isRomanceQuery = query.Contains("tinh cam") || query.Contains("tam ly") ||
                              query.Contains("lang man") || query.Contains("hen ho") || query.Contains("tinh yeu");

        if (isRomanceQuery)
        {
            if (genreNorm.Contains("tam ly") || genreNorm.Contains("tinh cam"))
            {
                score += 55;
                reasons.Add("Nội dung tâm lý tình cảm sâu sắc");
            }
        }

        return (score, reasons);
    }

    private (TimeSpan Start, TimeSpan End, string Reason) DetectTimePreference(string query)
    {
        if (query.Contains("8 gio dem") || query.Contains("8h dem") || query.Contains("20h") || query.Contains("sau 8"))
        {
            return (new TimeSpan(20, 0, 0), new TimeSpan(23, 59, 59), "Ưu tiên các suất chiếu sau 20h00");
        }
        if (query.Contains("9 gio dem") || query.Contains("9h dem") || query.Contains("21h") || query.Contains("sau 9"))
        {
            return (new TimeSpan(21, 0, 0), new TimeSpan(23, 59, 59), "Ưu tiên các suất chiếu sau 21h00");
        }
        if (query.Contains("dem") || query.Contains("muon") || query.Contains("khuya"))
        {
            return (new TimeSpan(21, 0, 0), new TimeSpan(23, 59, 59), "Ưu tiên các suất chiếu muộn sau 21h00");
        }
        if (query.Contains("toi nay") || query.Contains("buoi toi") || (query.Contains("toi") && !query.Contains("toi thay") && !query.Contains("toi nghi")))
        {
            return (new TimeSpan(18, 0, 0), new TimeSpan(23, 59, 59), "Ưu tiên các suất chiếu buổi tối (sau 18h00)");
        }
        if (query.Contains("buoi chieu") || query.Contains("chieu nay") || query.Contains("chieu mai") || query.Contains("dau chieu"))
        {
            return (new TimeSpan(13, 0, 0), new TimeSpan(18, 0, 0), "Ưu tiên các suất chiếu buổi chiều (13h00 - 18h00)");
        }
        if (query.Contains("buoi sang") || query.Contains("sang nay") || query.Contains("sang mai"))
        {
            return (new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0), "Ưu tiên các suất chiếu buổi sáng (08h00 - 12h00)");
        }

        return (TimeSpan.Zero, new TimeSpan(23, 59, 59), string.Empty);
    }

    private DateTime DetectTargetDate(string query)
    {
        if (query.Contains("ngay mai") || query.Contains("hom sau"))
        {
            return VietnamTime.Today.AddDays(1);
        }
        return VietnamTime.Today;
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
        result = result.Replace('\u0111', 'd').Replace('\u0110', 'd');
        return Regex.Replace(result, @"\s+", " ");
    }
}