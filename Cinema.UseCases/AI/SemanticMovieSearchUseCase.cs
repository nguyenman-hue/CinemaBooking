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
        _effectiveApiKey = (!string.IsNullOrWhiteSpace(geminiApiKey) && geminiApiKey != "YOUR_GEMINI_API_KEY") 
            ? geminiApiKey 
            : (Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "");
    }

    public async Task<List<AiMovieRecommendationDto>> ExecuteAsync(string naturalLanguageQuery, DateTime? preferredDate = null)
    {
        if (string.IsNullOrWhiteSpace(naturalLanguageQuery))
        {
            return new List<AiMovieRecommendationDto>();
        }

        var normalizedQuery = NormalizeText(naturalLanguageQuery);
        var activeMovies = (await _movieRepository.GetAllMoviesAsync(activeOnly: true)).ToList();
        
        // CHỈ LẤY CÁC SUẤT CHIẾU TRONG TƯƠNG LAI (LOẠI BỎ TRIỆT ĐỂ SUẤT ĐÃ CHIẾU VÀ DỮ LIỆU CŨ QUÁ KHỨ)
        var allShowtimes = (await _showtimeRepository.GetAllUpcomingShowtimesAsync())
            .Where(s => s.StartTime > DateTime.Now)
            .ToList();

        var (timeFilterStart, timeFilterEnd, timeReason) = DetectTimePreference(normalizedQuery);
        var targetDate = preferredDate ?? DetectTargetDate(normalizedQuery);

        // Ưu tiên gọi Google Gemini 2.5 Flash để đọc hiểu tự nhiên mọi câu hỏi (như ChatGPT)
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
                Console.WriteLine($"[Gemini Fallback] Lỗi API: {ex.Message}. Chuyển sang Smart Local Semantic Engine.");
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
                .Select(st => $"{st.StartTime:HH:mm} (ngày {st.StartTime:dd/MM})")
                .Distinct()
                .ToList();
            var showtimesStr = mShowtimes.Any() ? string.Join(", ", mShowtimes) : "Hiện tại không còn suất chiếu khả dụng";
            return $"- ID {m.MovieId}: \"{m.Title}\" (Đạo diễn: {m.Director}, Diễn viên: {m.Cast}, Thể loại: {m.Genre}, Độ tuổi: {m.AgeRating}, Thời lượng: {m.DurationMinutes} phút). Suất chiếu sắp tới: [{showtimesStr}]. Mô tả: {m.Description}";
        }));

        var systemPrompt = 
            "Bạn là Trợ lý AI điện ảnh thông minh của rạp Starlight Cinema Huế (tương tự như ChatGPT chuyên về điện ảnh).\n" +
            "Dưới đây là toàn bộ danh sách các bộ phim và các suất chiếu SẮP TỚI thực tế tại rạp:\n" + moviesSummary + "\n\n" +
            "Khách hàng hỏi: \"" + userQuery + "\"\n\n" +
            "Quy tắc nghiệp vụ THỰC TẾ bắt buộc (TUYỆT ĐỐI TUÂN THỦ):\n" +
            "1. Đọc hiểu toàn diện thời gian, suất chiếu (sớm nhất, muộn nhất, sau mấy giờ, trưa, tối, khuya...), cảm xúc, thể loại của khách hàng.\n" +
            "2. TUYỆT ĐỐI KHÔNG BỊA ĐẶT suất chiếu hoặc giờ chiếu không có trong danh sách trên.\n" +
            "3. Nếu khách hỏi về một khung giờ cụ thể (ví dụ: 'trên 8 giờ đêm', 'sau 20h', 'chiếu muộn nhất') mà rạp KHÔNG CÓ suất chiếu nào thỏa mãn:\n" +
            "   - BẮT BUỘC phải nói rõ sự thật trong câu trả lời (reason): Rạp không có suất nào trong khung giờ đó, và thông báo suất chiếu gần nhất/muộn nhất còn lại hôm nay (nếu có) hoặc báo hôm nay đã hết suất.\n" +
            "   - Chỉ gợi ý phim có suất chiếu gần nhất đó (hoặc trả về [] nếu toàn bộ rạp đã hết sạch suất).\n" +
            "4. BẮT BUỘC chỉ trả về định dạng JSON thuần túy (không kèm chữ thừa mở đầu hay kết thúc), theo mẫu:\n" +
            "[{\"movieId\": 5, \"score\": 85, \"reason\": \"Câu giải thích thân thiện và chính xác thực tế cho khách hàng...\"}]";

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
                        .Where(st => st.StartTime.Date == targetDate.Date);

                    if (timeStart > TimeSpan.Zero || timeEnd < new TimeSpan(23, 59, 59))
                    {
                        var filteredInWindow = movieShowtimes.Where(st => st.StartTime.TimeOfDay >= timeStart && st.StartTime.TimeOfDay <= timeEnd).ToList();
                        if (filteredInWindow.Any())
                        {
                            movieShowtimes = filteredInWindow;
                        }
                    }

                    var showtimesList = isLateQuery
                        ? movieShowtimes.OrderByDescending(st => st.StartTime).ToList()
                        : movieShowtimes.OrderBy(st => st.StartTime).ToList();

                    // TUYỆT ĐỐI KHÔNG BỐC SUẤT QUÁ KHỨ!
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

        // 1. Kiểm tra nếu người dùng hỏi theo khung giờ cụ thể (VD: "sau 8 gio dem", "suat muon", "suat sang"...)
        bool hasSpecificTimeFilter = (timeFilterStart > TimeSpan.Zero || timeFilterEnd < new TimeSpan(23, 59, 59));
        if (hasSpecificTimeFilter)
        {
            var moviesWithTimeShowtimes = activeMovies
                .Select(m => new
                {
                    Movie = m,
                    Showtimes = allShowtimes.Where(s => s.MovieId == m.MovieId && s.StartTime.Date == targetDate.Date && s.StartTime.TimeOfDay >= timeFilterStart && s.StartTime.TimeOfDay <= timeFilterEnd).OrderBy(s => s.StartTime).ToList()
                })
                .Where(x => x.Showtimes.Any())
                .ToList();

            if (moviesWithTimeShowtimes.Any())
            {
                double score = 95.0;
                foreach (var item in moviesWithTimeShowtimes)
                {
                    var showtimeStr = string.Join(", ", item.Showtimes.Select(s => s.StartTime.ToString("HH:mm")));
                    recommendations.Add(new AiMovieRecommendationDto
                    {
                        Movie = item.Movie,
                        RelevanceScore = score,
                        MatchReason = $"Phim có suất chiếu trong khung giờ bạn tìm: lúc {showtimeStr}.",
                        SuggestedShowtimes = item.Showtimes
                    });
                    score -= 5.0;
                }
                return recommendations;
            }
            else
            {
                // RẠP THỰC TẾ KHÔNG CÓ SUẤT NÀO TRONG KHUNG GIỜ NÀY -> CÓ SAO NÓI VẬY!
                var latestShowtimeToday = allShowtimes
                    .Where(s => s.StartTime.Date == targetDate.Date)
                    .OrderByDescending(s => s.StartTime)
                    .FirstOrDefault();

                if (latestShowtimeToday != null)
                {
                    var movie = activeMovies.FirstOrDefault(m => m.MovieId == latestShowtimeToday.MovieId);
                    if (movie != null)
                    {
                        var timeStr = latestShowtimeToday.StartTime.ToString("HH:mm");
                        recommendations.Add(new AiMovieRecommendationDto
                        {
                            Movie = movie,
                            RelevanceScore = 80.0,
                            MatchReason = $"Rất tiếc hiện tại rạp không có suất chiếu nào phù hợp với khung giờ này. Suất chiếu muộn nhất còn lại hôm nay là lúc {timeStr} (phim {movie.Title}) bạn nhé!",
                            SuggestedShowtimes = new List<Showtime> { latestShowtimeToday }
                        });
                        return recommendations;
                    }
                }

                // Nếu cả ngày hôm nay không còn suất nào nữa:
                return new List<AiMovieRecommendationDto>();
            }
        }

        // 2. Ý định: "chiếu muộn nhất", "suất muộn nhất"
        bool isLatestShowtimeQuery = normalizedQuery.Contains("muon nhat") || 
                                     normalizedQuery.Contains("chieu muon") || 
                                     normalizedQuery.Contains("xem muon") || 
                                     normalizedQuery.Contains("khuya nhat") ||
                                     (normalizedQuery.Contains("muon") && normalizedQuery.Contains("nhat"));

        if (isLatestShowtimeQuery && activeMovies.Any())
        {
            var moviesWithLatest = activeMovies
                .Select(m => new
                {
                    Movie = m,
                    Showtimes = allShowtimes.Where(s => s.MovieId == m.MovieId && s.StartTime.Date == targetDate.Date).OrderByDescending(s => s.StartTime).ToList(),
                    Latest = allShowtimes.Where(s => s.MovieId == m.MovieId && s.StartTime.Date == targetDate.Date).OrderByDescending(s => s.StartTime).FirstOrDefault()
                })
                .Where(x => x.Latest != null)
                .OrderByDescending(x => x.Latest!.StartTime.TimeOfDay)
                .ToList();

            if (moviesWithLatest.Any())
            {
                double topScore = 99.0;
                foreach (var item in moviesWithLatest)
                {
                    var latestTimeStr = item.Latest!.StartTime.ToString("HH:mm");
                    recommendations.Add(new AiMovieRecommendationDto
                    {
                        Movie = item.Movie,
                        RelevanceScore = topScore,
                        MatchReason = topScore >= 98.0
                            ? $"Bộ phim có suất chiếu muộn nhất rạp hôm nay (lúc {latestTimeStr}), rất lý tưởng cho bạn xem phim tối!"
                            : $"Phim có suất chiếu muộn lúc {latestTimeStr}.",
                        SuggestedShowtimes = item.Showtimes
                    });
                    topScore = Math.Max(70.0, topScore - 5.0);
                }
                return recommendations;
            }
            else
            {
                return new List<AiMovieRecommendationDto>();
            }
        }

        // 3. Ý định: "chiếu sớm nhất"
        bool isEarliestShowtimeQuery = normalizedQuery.Contains("som nhat") || 
                                       normalizedQuery.Contains("chieu som") || 
                                       normalizedQuery.Contains("xem som");

        if (isEarliestShowtimeQuery && activeMovies.Any())
        {
            var moviesWithEarliest = activeMovies
                .Select(m => new
                {
                    Movie = m,
                    Showtimes = allShowtimes.Where(s => s.MovieId == m.MovieId && s.StartTime.Date == targetDate.Date).OrderBy(s => s.StartTime).ToList(),
                    Earliest = allShowtimes.Where(s => s.MovieId == m.MovieId && s.StartTime.Date == targetDate.Date).OrderBy(s => s.StartTime).FirstOrDefault()
                })
                .Where(x => x.Earliest != null)
                .OrderBy(x => x.Earliest!.StartTime.TimeOfDay)
                .ToList();

            if (moviesWithEarliest.Any())
            {
                double topScore = 99.0;
                foreach (var item in moviesWithEarliest)
                {
                    var earliestTimeStr = item.Earliest!.StartTime.ToString("HH:mm");
                    recommendations.Add(new AiMovieRecommendationDto
                    {
                        Movie = item.Movie,
                        RelevanceScore = topScore,
                        MatchReason = $"Bộ phim có suất chiếu sớm nhất trong ngày lúc {earliestTimeStr}.",
                        SuggestedShowtimes = item.Showtimes
                    });
                    topScore = Math.Max(70.0, topScore - 5.0);
                }
                return recommendations;
            }
        }

        // 4. Tìm kiếm theo thể loại, tâm trạng, đạo diễn, tên phim
        bool isGeneralBrowseQuery = IsGeneralBrowseQuery(normalizedQuery);

        foreach (var movie in activeMovies)
        {
            var (score, matchReasons) = CalculateSemanticScore(movie, normalizedQuery, isGeneralBrowseQuery);

            if (score > 35.0)
            {
                var movieShowtimes = allShowtimes
                    .Where(st => st.MovieId == movie.MovieId && st.StartTime.Date == targetDate.Date)
                    .OrderBy(st => st.StartTime)
                    .ToList();

                var reasonBuilder = new StringBuilder();
                if (matchReasons.Any())
                {
                    reasonBuilder.Append(string.Join(", ", matchReasons));
                }
                else
                {
                    reasonBuilder.Append("Phù hợp với sở thích của bạn");
                }

                if (!movieShowtimes.Any())
                {
                    reasonBuilder.Append(" (Hôm nay phim tạm thời chưa có suất chiếu tiếp theo)");
                }

                recommendations.Add(new AiMovieRecommendationDto
                {
                    Movie = movie,
                    RelevanceScore = Math.Min(99.0, Math.Round(score, 1)),
                    MatchReason = reasonBuilder.ToString(),
                    SuggestedShowtimes = movieShowtimes // CHỈ LẤY SUẤT THỰC TẾ, KHÔNG BỐC ĐẠI QUÁ KHỨ!
                });
            }
        }

        return recommendations.OrderByDescending(r => r.RelevanceScore).ToList();
    }

    private bool IsGeneralBrowseQuery(string query)
    {
        return query.Contains("co phim gi") || query.Contains("danh sach phim") ||
               query.Contains("phim dang chieu") || query.Contains("phim hot") ||
               query.Contains("tat ca phim") || query.Contains("hom nay co gi") || query.Contains("rap co gi");
    }

    private (double Score, List<string> Reasons) CalculateSemanticScore(Movie movie, string query, bool isGeneralBrowse)
    {
        double score = 30.0;
        var reasons = new List<string>();

        string titleNorm = NormalizeText(movie.Title);
        string genreNorm = NormalizeText(movie.Genre);
        string descNorm = NormalizeText(movie.Description ?? string.Empty);
        string directorNorm = NormalizeText(movie.Director ?? string.Empty);
        string castNorm = NormalizeText(movie.Cast ?? string.Empty);
        string rating = movie.AgeRating?.ToUpperInvariant().Trim() ?? "P";

        if (isGeneralBrowse)
        {
            score += 35;
            reasons.Add("Phim đang chiếu tại rạp");
            return (score, reasons);
        }

        // 1. Khớp chính xác tên phim hoặc một phần tên phim
        if (titleNorm.Contains(query) || query.Contains(titleNorm))
        {
            score += 60;
            reasons.Add($"Khớp tên phim: {movie.Title}");
        }

        // 2. Tìm theo nhân vật nổi tiếng
        if (query.Contains("gau po") && (titleNorm.Contains("panda") || descNorm.Contains("po")))
        {
            score += 70;
            reasons.Add("Phim có chú gấu Po (Kung Fu Panda)");
        }
        else if ((query.Contains("kong") || query.Contains("godzilla")) && (titleNorm.Contains("godzilla") || titleNorm.Contains("kong") || descNorm.Contains("titan")))
        {
            score += 70;
            reasons.Add("Đại chiến quái thú Godzilla x Kong");
        }
        else if (query.Contains("sau cat") && (titleNorm.Contains("dune") || descNorm.Contains("cat")))
        {
            score += 70;
            reasons.Add("Siêu phẩm viễn tưởng Xứ Cát (Dune 2)");
        }

        // 3. Tìm theo Đạo diễn / Diễn viên nổi tiếng
        if (query.Contains("tran thanh") && (directorNorm.Contains("tran thanh") || castNorm.Contains("tran thanh")))
        {
            score += 75;
            reasons.Add("Tác phẩm của đạo diễn Trấn Thành");
        }
        else if (query.Contains("ly hai") && (directorNorm.Contains("ly hai") || castNorm.Contains("ly hai")))
        {
            score += 75;
            reasons.Add("Phim của đạo diễn Lý Hải");
        }
        else if (query.Contains("zendaya") && castNorm.Contains("zendaya"))
        {
            score += 70;
            reasons.Add("Phim có sự tham gia của Zendaya");
        }
        else if (query.Contains("jack black") && castNorm.Contains("jack black"))
        {
            score += 70;
            reasons.Add("Phim do Jack Black lồng tiếng");
        }

        // 4. Nhóm Gia đình, Trẻ em, Hài hước
        bool isFamilyQuery = query.Contains("gia dinh") || query.Contains("tre em") || 
                             query.Contains("hoat hinh") || query.Contains("hai huoc") ||
                             query.Contains("cuoi") || query.Contains("xa stress");

        if (isFamilyQuery)
        {
            if (rating == "P" || rating == "K")
            {
                score += 45;
                reasons.Add($"Phim nhãn {rating} phù hợp cho gia đình và trẻ em");
            }
            if (genreNorm.Contains("hoat hinh") || genreNorm.Contains("hai"))
            {
                score += 40;
                reasons.Add("Thể loại hoạt hình, hài hước vui nhộn");
            }
        }

        // 5. Nhóm Hành động, Bom tấn, Viễn tưởng
        bool isActionQuery = query.Contains("hanh dong") || query.Contains("bom tan") || 
                             query.Contains("chien dau") || query.Contains("vien tuong") ||
                             query.Contains("danh nhau") || query.Contains("quai vat");

        if (isActionQuery)
        {
            if (genreNorm.Contains("hanh dong") || genreNorm.Contains("vien tuong"))
            {
                score += 45;
                reasons.Add("Thể loại hành động, viễn tưởng mãn nhãn");
            }
        }

        // 6. Nhóm Tình cảm, Tâm lý
        bool isRomanceQuery = query.Contains("tinh cam") || query.Contains("tam ly") || 
                              query.Contains("lang man") || query.Contains("hen ho") || query.Contains("tinh yeu");

        if (isRomanceQuery)
        {
            if (genreNorm.Contains("tam ly") || genreNorm.Contains("tinh cam"))
            {
                score += 50;
                reasons.Add("Nội dung tâm lý tình cảm sâu sắc");
            }
        }

        // 7. Token Matching
        var queryWords = query.Split(new[] { ' ', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
                              .Where(w => w.Length >= 2)
                              .ToList();

        int matchedWords = 0;
        foreach (var word in queryWords)
        {
            if (titleNorm.Contains(word) || genreNorm.Contains(word))
            {
                score += 15;
                matchedWords++;
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
        if (query.Contains("10 gio dem") || query.Contains("10h dem") || query.Contains("22h") || query.Contains("sau 10"))
        {
            return (new TimeSpan(22, 0, 0), new TimeSpan(23, 59, 59), "Ưu tiên các suất chiếu sau 22h00");
        }
        if (query.Contains("dem") || query.Contains("muon") || query.Contains("khuya"))
        {
            return (new TimeSpan(21, 0, 0), new TimeSpan(23, 59, 59), "Ưu tiên các suất chiếu muộn sau 21h00");
        }
        if (query.Contains("toi nay") || query.Contains("buoi toi") || query.Contains("toi"))
        {
            return (new TimeSpan(18, 0, 0), new TimeSpan(21, 30, 0), "Ưu tiên các suất chiếu buổi tối (18h00 - 21h30)");
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
        result = result.Replace('\u0111', 'd').Replace('\u0110', 'd');
        return Regex.Replace(result, @"\s+", " ");
    }
}
