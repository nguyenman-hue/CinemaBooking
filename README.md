# ĐỒ ÁN CAPSTONE: RẠP PHIM - CHỌN GHẾ & ĐẶT VÉ (CINEMA SEAT BOOKING)
**Học phần:** Lập trình ứng dụng Web  
**Trường:** Trường Đại học Kinh tế - Đại học Huế (HUE)  
**Đề tài 14:** Rạp phim: Chọn ghế & Đặt vé - Cinema Seat Booking  
**Độ khó:** ★★★ (3 sao - Thang điểm 9.0 - 10.0)  
**Sinh viên thực hiện:** Nguyễn Viết Mẫn  
**Mã số sinh viên (MSSV):** 23K4080026 — Lớp: K57 Tin Học Kinh Tế  
**GitHub Repository:** [https://github.com/nguyenman-hue/CinemaBooking](https://github.com/nguyenman-hue/CinemaBooking)

---

## 1. Tổng Quan Kiến Trúc (Clean Architecture 4 Tầng)
Dự án được cấu trúc nghiêm ngặt theo mô hình **Clean Architecture** (Dependency Inversion Principle), đảm bảo mã nguồn độc lập, dễ kiểm thử và mở rộng:

1. `Cinema.CoreBusiness`:
   - Chứa Domain Entities (`Movie`, `Auditorium`, `Seat`, `Showtime`, `Booking`, `Ticket`).
   - Cài đặt 2 Luật nghiệp vụ cốt lõi: `SeatBookingRule` (chống bán trùng ghế) và `ShowtimeScheduleRule` (chống trùng lịch cùng phòng).
   - Domain Exceptions: `SeatConflictException`, `ShowtimeOverlapException`.
   - Độc lập tuyệt đối, 0 dependencies bên ngoài.
2. `Cinema.UseCases`:
   - Chứa toàn bộ nghiệp vụ tương tác của khách hàng và quản trị viên.
   - Định nghĩa các Plugin Interfaces (`IMovieRepository`, `IShowtimeRepository`, `IAuditoriumRepository`, `ISeatRepository`, `IBookingRepository`, `ISeatHoldingStateStore`).
   - Tích hợp UseCase mở rộng 3 sao: `ISemanticMovieSearchUseCase` (Trợ lý AI Semantic Search tích hợp Google Gemini 2.5 Flash API).
   - Chỉ phụ thuộc duy nhất vào `Cinema.CoreBusiness`.
3. `Plugins/Cinema.DataStore.SQL.Dapper`:
   - Triển khai truy xuất CSDL SQL Server 2019/2022 (`CinemaDB`) sử dụng Dapper thuần với câu lệnh tham số hóa (Parameterized Queries) chống SQL Injection 100%.
   - Quản lý giao dịch Transaction ACID (`IDbTransaction`) cho cặp Header-Line (`Booking` & `Ticket`) đảm bảo tính toàn vẹn dữ liệu.
4. `Plugins/Cinema.StateStore.DI`:
   - Triển khai State Store giữ ghế tạm thời (Thread-safe `ConcurrentDictionary`).
   - Quản lý giữ chỗ thời gian thực trong 10 phút, tự động nhả ghế khi hết hạn.
5. `Cinema.Web`:
   - Ứng dụng Blazor Web App (.NET 8 - Interactive Server Mode).
   - Giao diện **Dark Neon Cinema Theme** chuẩn rạp quốc tế Starlight Cinema Huế.
   - Cơ chế quản lý phiên giữ ghế `UserSessionService` đồng bộ qua Cookie HTTP và LocalStorage, khôi phục ghế đã chọn và đồng hồ đếm ngược khi F5.
   - Floating AI Cinema Assistant (Widget Trợ lý AI ở góc dưới bên phải ở mọi trang).
   - Hệ thống xác thực phân quyền Cookie Authentication (Admin / Khách vãng lai).

---

## 2. Hai Quy Tắc Nghiệp Vụ Cốt Lõi (Mandatory Rules)
- **LUẬT 1 (Tranh chấp ghế - Seat Conflict):**
  - Một ghế trong một suất chiếu chỉ được bán DUY NHẤT một lần, kể cả khi 2 người bấm thanh toán cùng lúc.
  - Cơ chế bảo vệ 2 lớp:
    1. Kiểm tra trạng thái ghế khả dụng tại `SeatBookingRule.cs` và State Store `SeatHoldingStateStore.cs`.
    2. Ràng buộc toàn vẹn `CONSTRAINT UQ_Showtime_Seat UNIQUE(ShowtimeId, SeatId)` trong SQL Server kết hợp `IDbTransaction` trong `BookingRepository.cs` (tự động rollback nếu xảy ra vi phạm).
- **LUẬT 2 (Không chồng giờ - Showtime Non-Overlapping):**
  - Các suất chiếu cùng phòng chiếu không được phép chồng giờ.
  - Thời gian kết thúc tự động tính: `EndTime = StartTime + MovieDuration + 15 phút dọn dẹp vệ sinh phòng chiếu`.
  - Được kiểm tra và bảo đảm bởi `ShowtimeScheduleRule.cs`. Khi vi phạm sẽ ném ngoại lệ `ShowtimeOverlapException`.

---

## 3. Tính Năng Mở Rộng 3 Sao: Trợ Lý AI Semantic Search (Google Gemini 2.5 Flash)
- **AI Semantic Search:** Tìm kiếm phim theo mô tả ngôn ngữ tự nhiên (VD: *"phim nhẹ nhàng cho gia đình"*, *"bom tấn hành động kịch tính"*).
- **Trợ lý gợi ý suất chiếu:** Nhận diện khung giờ (tối, chiều, sau 21h), gợi ý suất chiếu phù hợp kèm nút chọn nhanh dẫn thẳng vào sơ đồ ghế.
- **Tích hợp API thực tế:** Kết nối trực tiếp Google Gemini 2.5 Flash API với cơ chế dự phòng tự động (Local Semantic Engine Fallback) nếu mất mạng hoặc hết quota.
- **Widget nổi hiện đại:** Nút bấm `.ai-floating-btn` và cửa sổ chat `.ai-chat-popup` cố định góc dưới bên phải ở mọi trang.

---

## 4. Hướng Dẫn Cài Đặt & Chạy Ứng Dụng
1. **Khởi tạo CSDL SQL Server:**
   - Đảm bảo SQL Server đang chạy tại `localhost`.
   - File script CSDL: `Cinema.SchemaAndData.sql` (tự động tạo DB `CinemaDB` và nạp sẵn dữ liệu 5 phim, 2 phòng chiếu, 96 ghế, 12 suất chiếu, vé mẫu).
   - Lệnh chạy bằng PowerShell:
     ```powershell
     sqlcmd -S localhost -E -C -i Cinema.SchemaAndData.sql
     ```
2. **Cấu hình Gemini API Key (Tùy chọn):**
   - Tạo file `Cinema.Web/appsettings.Local.json` (đã cấu hình sẵn trong `.gitignore`):
     ```json
     {
       "Gemini": {
         "ApiKey": "YOUR_GEMINI_API_KEY",
         "Model": "gemini-2.5-flash"
       }
     }
     ```
3. **Khởi chạy ứng dụng:**
   ```bash
   dotnet run --project Cinema.Web/Cinema.Web.csproj --launch-profile http
   ```
   Truy cập trình duyệt: `http://localhost:5284` (hoặc profile https tại `https://localhost:7190`).
4. **Tài khoản Demo Đăng nhập:**
   - **Quản trị viên (Admin):** `admin` / Mật khẩu: `admin123`
   - **Khách hàng (Customer):** `khachhang` / Mật khẩu: `khach123` (hoặc đăng ký mới trên Web)

