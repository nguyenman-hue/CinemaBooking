using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cinema.CoreBusiness.Models;
using Cinema.CoreBusiness.Utilities;
using Cinema.UseCases.DTOs;
using Cinema.UseCases.PluginInterfaces.DataStore;

namespace Cinema.UseCases.AI;

/// <summary>
/// Trien khai UseCase tim kiem phim thong minh (Semantic Search) va Tro ly AI da nang nhu ChatGPT.
/// Ho tro tich hop truc tiep Google Gemini API voi bo nho hoi thoai (Multi-turn Chat History),
/// co che tu dong chuyen doi Model khi cham han muc (Quota), va Fallback thong minh cuc bo (Offline).
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
        "gemini-flash-lite-latest",
        "gemini-2.5-flash",
        "gemini-3.5-flash-lite"
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
    /// Hoi thoai thuc te voi Tro ly AI (toan dien nhu ChatGPT voi bo nho ngu canh lich su)
    /// </summary>
    public async Task<AiChatResponseDto> ChatAsync(string naturalLanguageQuery, DateTime? preferredDate = null, List<AiChatMessageDto>? history = null)
    {
        if (string.IsNullOrWhiteSpace(naturalLanguageQuery))
        {
            return new AiChatResponseDto
            {
                ReplyMessage = "Chào bạn! Mình là Trợ lý AI Starlight Cinema. Mình có thể giúp gì cho bạn hôm nay nà? Bạn muốn tìm phim, xem suất chiếu hay trò chuyện gì cứ nhắn mình nhé!",
                Recommendations = new List<AiMovieRecommendationDto>()
            };
        }

        string originalQuery = naturalLanguageQuery.Trim();
        string normalizedQuery = NormalizeText(originalQuery);

        var targetDate = preferredDate?.Date ?? DetectTargetDate(normalizedQuery);
        var (timeStart, timeEnd, timeReason) = DetectTimePreference(normalizedQuery);

        var activeMovies = (await _movieRepository.GetAllMoviesAsync(activeOnly: true)).ToList();
        var allShowtimes = (await _showtimeRepository.GetAllUpcomingShowtimesAsync()).ToList();

        // Loc suat chieu chuan gio Viet Nam (UTC+7)
        var nowVn = VietnamTime.Now;
        var todayVn = VietnamTime.Today;
        var upcomingShowtimes = allShowtimes
            .Where(s => s.StartTime.Date == targetDate.Date)
            .Where(s => targetDate.Date > todayVn || s.StartTime > nowVn)
            .OrderBy(s => s.StartTime)
            .ToList();

        if (!string.IsNullOrWhiteSpace(_effectiveApiKey))
        {
            // Thu lan luot model chinh, neu gap 429 quota thi tu dong chuyen sang cac model nhe du phong
            var modelsToTry = new List<string>();
            if (!string.IsNullOrWhiteSpace(_primaryModel)) modelsToTry.Add(_primaryModel);
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
                        history,
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
            timeReason,
            history);
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
        List<AiChatMessageDto>? history,
        string apiKey,
        string modelName)
    {
        var nowVn = VietnamTime.Now;
        bool isToday = (targetDate.Date == nowVn.Date);
        bool hasRemainingShowtimesToday = upcomingShowtimes.Any(s => s.StartTime > nowVn);

        var moviesSummary = string.Join("\n", movies.Select(m =>
        {
            var movieShowtimes = upcomingShowtimes
                .Where(st => st.MovieId == m.MovieId)
                .OrderBy(st => st.StartTime)
                .GroupBy(st => st.StartTime)
                .Select(g => g.First())
                .ToList();

            string showtimeListStr = movieShowtimes.Any()
                ? string.Join(", ", movieShowtimes.Select(st => $"{st.StartTime:HH:mm} ({st.AuditoriumName ?? "Phòng chiếu"})"))
                : "Đã hết suất chiếu";

            return $"[ID: {m.MovieId}] {m.Title} | Thể loại: {m.Genre} | Thời lượng: {m.DurationMinutes}p | Đạo diễn: {m.Director} | Diễn viên: {m.Cast} | Độ tuổi: {m.AgeRating} | Suất chiếu thực tế còn lại: {showtimeListStr} | Tóm tắt: {m.Description}";
        }));

        var contextHeader =
            $"THỜI GIAN HIỆN TẠI TẠI RẠP: {nowVn:HH:mm}, Ngày {nowVn:dd/MM/yyyy} (Giờ Việt Nam UTC+7).\n" +
            (isToday
                ? (hasRemainingShowtimesToday
                    ? $"TÌNH TRẠNG RẠP HÔM NAY ({nowVn:dd/MM/yyyy}): Hôm nay rạp CÓ các suất chiếu diễn ra trong ngày (sau {nowVn:HH:mm}). Hãy nhiệt tình giới thiệu các phim và khung giờ chiếu này cho khách!"
                    : $"TÌNH TRẠNG RẠP HÔM NAY ({nowVn:dd/MM/yyyy}): Hôm nay rạp KHÔNG CÒN bất kỳ suất chiếu nào (tất cả các suất chiếu đã kết thúc).")
                : $"TÌNH TRẠNG: Khách đang hỏi ngày {targetDate:dd/MM/yyyy}.");

        var historyText = (history != null && history.Any())
            ? string.Join("\n", history.Select(h => (h.IsUser ? "Khách: " : "Starlight AI: ") + h.Message))
            : "Chưa có hội thoại trước đó.";

        var systemPrompt = $@"Bạn là Starlight AI - Trợ lý AI thế hệ mới đa năng và toàn diện của rạp chiếu phim Starlight Cinema Huế (trung tâm TP. Huế).
Bạn sở hữu trí tuệ nhân tạo sâu rộng, toàn diện như ChatGPT / Gemini: bạn am hiểu mọi lĩnh vực từ văn hóa, xã hội, giáo dục, trường học, giảng viên, công nghệ thông tin (C#, .NET, SQL, React, AI, thuật toán), kinh tế, đời sống...
Bạn hiểu được MỌI kiểu giao tiếp của người Việt: nói tắt, viết tắt (k, j, z, s...), tiếng lóng (cuốn, đỉnh, cháy, ổn áp, bánh cuốn...), hỏi tâm trạng, đùa vui, hay hỏi tư vấn cụ thể.

=== NĂNG LỰC TÌM KIẾM THỜI GIAN THỰC (GOOGLE SEARCH GROUNDING) ===
- Bạn được tích hợp công cụ tìm kiếm Google thời gian thực (Google Search).
- Khi người dùng hỏi về bất kỳ ai (giảng viên, thầy cô, trường Đại học Kinh tế HCE, Đại học Huế, nhà khoa học, nhân vật, sự kiện, công nghệ thông tin...), hãy tự động tìm kiếm trên Google để trả lời chính xác, mới nhất mà KHÔNG cần viết sẵn thông tin.
- Phong cách phản hồi: Tự nhiên, ngắn gọn (khoảng 2-3 câu), đúng trọng tâm, tôn trọng và lịch sự.
- Tuyệt đối không dùng những câu máy móc kiểu: 'Theo danh sách được cung cấp', 'Theo dữ liệu rạp'...

=== NĂNG LỰC ĐIỆN ẢNH & SUẤT CHIẾU RẠP STARLIGHT HUẾ (BẢO LƯU 100%) ===
=== NGỮ CẢNH THỜI GIAN THỰC ===
{contextHeader}

=== DANH SÁCH PHIM & SUẤT CHIẾU THỰC TẾ TẠI RẠP ===
{moviesSummary}

=== LỊCH SỬ TRÒ CHUYỆN GẦN ĐÂY CỦA PHIÊN HỘI THOẠI ===
{historyText}

Khách hàng vừa gửi tin nhắn mới nhất: ""{userQuery}""

=== QUY TẮC PHẢN HỒI THÔNG MINH, TOÀN DIỆN CHUẨN CHATGPT ===
1. KHI KHÁCH HỎI VỀ PHIM ẢNH, RẠP CHIẾU, TÌM PHIM, SUẤT CHIẾU HÔM NAY:
   - ĐÂY LÀ NHIỆM VỤ ĐIỆN ẢNH CỦA RẠP: Phải đọc chính xác dữ liệu phim và suất chiếu thực tế từ danh sách bên trên.
   - NẾU HÔM NAY CÓ SUẤT CHIẾU (như 09:00, 14:00, 19:00...): Phải khẳng định NGAY là hôm nay rạp CÓ suất chiếu, giới thiệu hào hứng tên phim, khung giờ và phòng chiếu! Đưa các phim phù hợp vào danh sách 'movies' để giao diện hiển thị thẻ phim cho khách đặt vé.
   - NẾU KHÁCH TÌM PHIM THEO THỂ LOẠI / TÂM TRẠNG / ĐI VỚI BẠN BÈ / NGƯỜI YÊU: Tư vấn tinh tế, chọn đúng 1 đến 4 phim thực sự phù hợp từ danh sách rạp, đưa vào danh sách 'movies' kèm 'score' (70-99) và 'reason'.
   - CHỈ KHI NÀO thực sự trong cả ngày hôm nay KHÔNG CÒN một suất chiếu nào thì mới thông báo rạp đã kết thúc suất hôm nay (khi đó 'movies': []).

2. KHI KHÁCH HỎI VỀ BẤT KỲ CHỦ ĐỀ NÀO KHÁC (Thầy cô, trường học HCE, nhân vật, kiến thức, công nghệ thông tin C#/.NET/AI, văn hóa, đời sống...):
   - Tận dụng Google Search để tìm kiếm và trả lời CHÍNH XÁC, THÔNG MINH, NGẮN GỌN (2-3 câu).
   - Hãy xâu chuỗi thông tin từ lịch sử trò chuyện để hiểu rõ câu hỏi tiếp nối.
   - Giữ văn phong tự nhiên, lễ phép, thân thiện. Tuyệt đối không ép giới thiệu phim khi khách đang hỏi kiến thức hay giao tiếp.
   - Trong trường hợp này, danh sách 'movies' là [] (vì không phải đang tìm phim chiếu rạp).

3. ĐỊNH DẠNG TRẢ VỀ: BẮT BUỘC là JSON thuần túy (không bọc trong markdown ```json ``` thừa thãi):
{{
  ""reply"": ""Nội dung trả lời chi tiết, thông minh, sâu rộng của bạn dành cho khách..."",
  ""movies"": [
    {{ ""movieId"": 1, ""score"": 95, ""reason"": ""Lý do ngắn gọn..."" }}
  ]
}}";

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
            },
            tools = new object[]
            {
                new { google_search = new { } }
            }
        };

        var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var response = await _httpClient!.PostAsync(url, jsonContent, cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            return null; // Model nay bi loi (vd 429 quota), de vong lap thu model tiep theo
        }

        var responseString = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseString);

        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            return null;
        }

        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var contentProp) ||
            !contentProp.TryGetProperty("parts", out var partsProp))
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var part in partsProp.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textProp))
            {
                sb.Append(textProp.GetString());
            }
        }

        var textResult = sb.ToString();
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
        string timeReason,
        List<AiChatMessageDto>? history = null)
    {
        // 0. Khi he thong offline, neu nguoi dung hoi cac cau hoi khong lien quan den rap/phim
        bool isMovieRelated = normalizedQuery.Contains("phim") || normalizedQuery.Contains("ve") ||
                              normalizedQuery.Contains("chieu") || normalizedQuery.Contains("rap") ||
                              normalizedQuery.Contains("suat") || normalizedQuery.Contains("gia") ||
                              normalizedQuery.Contains("lich");

        // 1. Kiem tra hoi con phim / con suat chieu khong khi da khuya het suat
        bool isLookingForCurrentShowings = normalizedQuery.Contains("con phim") || normalizedQuery.Contains("dang chieu") ||
                                           normalizedQuery.Contains("con suat") || normalizedQuery.Contains("chieu khong") ||
                                           normalizedQuery.Contains("chieu k") || normalizedQuery.Contains("chieu j") ||
                                           normalizedQuery.Contains("gio chieu");

        if (isLookingForCurrentShowings && !upcomingShowtimes.Any(s => s.StartTime > VietnamTime.Now))
        {
            return new AiChatResponseDto
            {
                ReplyMessage = $"Chào bạn! Hiện tại đã khuya ({VietnamTime.Now:HH:mm}) và rạp Starlight Cinema Huế đã kết thúc toàn bộ suất chiếu của ngày hôm nay rồi ạ. Bạn vui lòng tham khảo lịch chiếu ngày mai hoặc quay lại vào ngày mai để đặt vé sớm nhé! Chúc bạn một buổi tối tốt lành!",
                Recommendations = new List<AiMovieRecommendationDto>()
            };
        }

        // 2. Kiem tra chao hoi hoac tro chuyen chung chung
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

        // 3. Kiem tra hoi khung gio cu the (chi khi khong phai hoi the loai)
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

        // 4. Cham diem theo the loai, dien vien, dao dien, tu khoa
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

        // 5. Neu khong tim thay phim nao phu hop
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

    private DateTime DetectTargetDate(string query)
    {
        var today = VietnamTime.Today;
        if (query.Contains("ngay mai") || query.Contains("mai")) return today.AddDays(1);
        if (query.Contains("ngay kia") || query.Contains("mot")) return today.AddDays(2);
        if (query.Contains("cuoi tuan"))
        {
            int daysUntilSat = ((int)DayOfWeek.Saturday - (int)today.DayOfWeek + 7) % 7;
            return today.AddDays(daysUntilSat == 0 ? 7 : daysUntilSat);
        }
        return today;
    }

    private (TimeSpan Start, TimeSpan End, string Reason) DetectTimePreference(string query)
    {
        var match = Regex.Match(query, @"(\d{1,2})\s*(gio|h|g|:00)?");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int hour))
        {
            if (hour >= 7 && hour <= 23)
            {
                return (new TimeSpan(Math.Max(0, hour - 1), 0, 0), new TimeSpan(Math.Min(23, hour + 2), 0, 0), $"quanh khung gio {hour}:00");
            }
        }

        if (query.Contains("sang") || query.Contains("buoi sang"))
            return (new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0), "buoi sang (08:00 - 12:00)");

        if (query.Contains("trua") || query.Contains("buoi trua"))
            return (new TimeSpan(11, 30, 0), new TimeSpan(14, 0, 0), "buoi trua (11:30 - 14:00)");

        if (query.Contains("chieu") || query.Contains("buoi chieu"))
            return (new TimeSpan(13, 0, 0), new TimeSpan(18, 0, 0), "buoi chieu (13:00 - 18:00)");

        if (query.Contains("toi muon") || query.Contains("khuya") || query.Contains("dem") || query.Contains("muon"))
            return (new TimeSpan(20, 30, 0), new TimeSpan(23, 59, 59), "suat chieu toi muon (sau 20:30)");

        if (query.Contains("toi") || query.Contains("buoi toi"))
            return (new TimeSpan(18, 0, 0), new TimeSpan(22, 30, 0), "buoi toi (18:00 - 22:30)");

        return (TimeSpan.Zero, new TimeSpan(23, 59, 59), "tat ca khung gio");
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var normalized = text.ToLowerInvariant().Replace('\u0111', 'd').Replace('\u0110', 'd').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        var cleaned = Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC), @"[^a-z0-9\s]", " ");
        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }
}
