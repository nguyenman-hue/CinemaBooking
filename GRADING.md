# BẢNG ĐỐI CHIẾU MINH CHỨNG MÃ KIỂM TRA (K1.1 – K5.3)

* **Học phần:** Lập trình ứng dụng Web (ECO2415) - Đồ án Capstone
* **Đề tài 14:** **Rạp phim: Chọn ghế & Đặt vé (Cinema Seat Booking)**
* **Độ khó đề tài:** ★★★ (3 sao - Thang điểm tối đa 100/100)
* **Sinh viên thực hiện:** **Nguyễn Viết Mẫn** (MSSV: **23K4080026** - Lớp: K46/21.02)
* **Giảng viên hướng dẫn & chấm thi:** TS. Hà Ngọc Long
* **GitHub Repository:** [https://github.com/nguyenman-hue/CinemaBooking](https://github.com/nguyenman-hue/CinemaBooking)

---

## TỔNG HỢP ĐIỂM THEO 5 TIÊU CHÍ RUBRIC (MỤC 11 & 12)

| Nhóm tiêu chí | Nội dung tiêu chí lớn | Trọng số | Các mã kiểm tra | Điểm tối đa | Điểm tự đánh giá |
| :---: | :--- | :---: | :---: | :---: | :---: |
| **Nhóm 1** | Kiến trúc Clean Architecture & Code Quality | 25% | K1.1, K1.2, K1.3, K1.4 | 25 | **25 / 25** |
| **Nhóm 2** | Cơ sở Dữ liệu & Tính toàn vẹn Dữ liệu | 25% | K2.1, K2.2, K2.3, K2.4 | 25 | **25 / 25** |
| **Nhóm 3** | Giao diện & Trải nghiệm Người dùng | 20% | K3.1, K3.2, K3.3, K3.4 | 20 | **20 / 20** |
| **Nhóm 4** | Bảo mật, Phân quyền & Tính năng Nâng cao / AI | 15% | K4.1, K4.2, K4.3 | 15 | **15 / 15** |
| **Nhóm 5** | Hồ sơ Thiết kế (PlantUML) & Thuyết trình Vấn đáp | 15% | K5.1, K5.2, K5.3 | 15 | **15 / 15** |
| **TỔNG CỘNG** | **ĐỒ ÁN CAPSTONE HOÀN CHỈNH** | **100%** | **18 mã kiểm tra (K1.1 – K5.3)** | **100** | **100 / 100** |

---

## BẢNG MINH CHỨNG CHI TIẾT THEO MỤC 12

### NHÓM K1: KIẾN TRÚC CLEAN ARCHITECTURE & CHẤT LƯỢNG MÃ NGUỒN (25 ĐIỂM)

| Mã | Điểm | Tiêu chí kiểm tra | Đường dẫn file làm bằng chứng | Số dòng | Mô tả chi tiết minh chứng & Cách kiểm |
| :---: | :---: | :--- | :--- | :---: | :--- |
| **K1.1** | 8đ | Đủ 4 project; CoreBusiness không tham chiếu project nào; UseCases chỉ tham chiếu CoreBusiness | `Cinema.CoreBusiness/Cinema.CoreBusiness.csproj`<br>`Cinema.UseCases/Cinema.UseCases.csproj`<br>`Cinema.Web/Cinema.Web.csproj`<br>`CinemaBooking.sln` | Toàn bộ file | • `Cinema.CoreBusiness.csproj` độc lập tuyệt đối, không có bất kỳ thẻ `<ProjectReference>` nào.<br>• `Cinema.UseCases.csproj` chỉ tham chiếu duy nhất `Cinema.CoreBusiness`.<br>• `Cinema.Web.csproj` đóng vai trò Composition Root, liên kết các Plugin tại `Program.cs`.<br>• *Cách kiểm:* Mở các file `.csproj`, xem thẻ `<ProjectReference>`. |
| **K1.2** | 7đ | Interface repository nằm ở UseCases, Plugin cài đặt; file .razor inject interface, không inject lớp cụ thể | `Cinema.UseCases/PluginInterfaces/DataStore/`<br>`Plugins/Cinema.DataStore.SQL.Dapper/Repositories/`<br>`Cinema.Web/Components/Pages/BookingPage.razor` | Toàn bộ Repository & Razor | • Tầng UseCases định nghĩa toàn bộ Interface: `IMovieRepository`, `IShowtimeRepository`, `ISeatRepository`, `IBookingRepository`, `IUserRepository`, `IDashboardRepository`.<br>• Tầng Plugins hiện thực các Interface bằng Dapper thuần.<br>• 100% file `.razor` chỉ `@inject` UseCase Interface (hoặc Service Interface), tuyệt đối không inject lớp cụ thể hay Repository.<br>• *Cách kiểm:* Tìm `@inject` trong thư mục `Cinema.Web/Components`. |
| **K1.3** | 6đ | 2 luật nghiệp vụ của đề cài trong CoreBusiness / UseCases, không nằm trong .razor (3đ mỗi luật) | `Cinema.CoreBusiness/Rules/SeatBookingRule.cs`<br>`Cinema.CoreBusiness/Rules/ShowtimeScheduleRule.cs`<br>`Cinema.UseCases/Bookings/CreateBookingUseCase.cs`<br>`Cinema.UseCases/Showtimes/CreateShowtimeUseCase.cs` | `SeatBookingRule.cs`: Dòng 14–210<br>`ShowtimeScheduleRule.cs`: Dòng 10–65<br>`CreateBookingUseCase.cs`: Dòng 35–65 | • **Luật 1 (Tranh chấp ghế & Giữ ghế 10 phút & Chống ghế đơn lẻ)**: Cài đặt trong `SeatBookingRule.cs`, chặn bán trùng ghế, kiểm tra ghế đang giữ tạm và chống để lại ghế trống đơn lẻ.<br>• **Luật 2 (Không chồng giờ chiếu)**: Cài đặt trong `ShowtimeScheduleRule.cs`, đảm bảo khoảng cách giữa các suất cùng phòng $\ge$ thời lượng phim + 15 phút dọn phòng vệ sinh.<br>• *Cách kiểm:* Mở đúng các file Rule trong CoreBusiness và UseCase thực thi. |
| **K1.4** | 4đ | Dependency Injection đăng ký đủ; giỏ/lựa chọn tạm dùng Scoped | `Cinema.Web/Program.cs`<br>`Cinema.Web/Services/UserSessionService.cs`<br>`Plugins/Cinema.StateStore.DI/SeatHoldingStateStore.cs` | `Program.cs`: Dòng 32–45, 87–90 | • Giỏ hàng / lựa chọn ghế tạm thời dùng `UserSessionService` đăng ký `AddScoped` (kết hợp Cookie Session per-user).<br>• Bộ lưu trữ giữ chỗ thời gian thực `SeatHoldingStateStore` đăng ký `AddSingleton` để quản lý tranh chấp đồng thời giữa nhiều phiên người dùng.<br>• *Cách kiểm:* Mở `Program.cs` kiểm tra các dòng đăng ký DI. |

---

### NHÓM K2: CƠ SỞ DỮ LIỆU & TÍNH TOÀN VẸN DỮ LIỆU (25 ĐIỂM)

| Mã | Điểm | Tiêu chí kiểm tra | Đường dẫn file làm bằng chứng | Số dòng | Mô tả chi tiết minh chứng & Cách kiểm |
| :---: | :---: | :--- | :--- | :---: | :--- |
| **K2.1** | 8đ | Từ 5 bảng trở lên, chuẩn 3NF, có khóa ngoại; script tạo bảng và dữ liệu mẫu nằm trong repo chạy được | `Cinema.SchemaAndData.sql` | Dòng 1–350 | • CSDL quan hệ gồm 7 bảng chuẩn 3NF: `[User]`, `Movie`, `Auditorium`, `Seat`, `Showtime`, `Booking`, `Ticket`.<br>• Đầy đủ các ràng buộc khóa chính, khóa ngoại `FOREIGN KEY` và dữ liệu mẫu nạp sẵn (5 phim, 2 phòng chiếu, 96 ghế, 12 suất chiếu). Chạy được ngay trên CSDL trống.<br>• *Cách kiểm:* Chạy script `Cinema.SchemaAndData.sql` trên SQL Server trống. |
| **K2.2** | 6đ | Cặp header–line ghi trong 1 transaction | `Plugins/Cinema.DataStore.SQL.Dapper/Repositories/BookingRepository.cs`<br>`Cinema.SchemaAndData.sql` | `BookingRepository.cs`: Dòng 25–65<br>`Cinema.SchemaAndData.sql`: Dòng 82 | • Cặp `Booking` (Header) và `Ticket` (Line Items) được thực thi trong cùng một `SqlTransaction` ACID duy nhất (`connection.BeginTransaction()`).<br>• Ràng buộc Unique `UQ_Ticket_Showtime_Seat` đảm bảo nếu 2 giao dịch tranh chấp cùng 1 ghế ở cùng mili-giây thì giao dịch thứ hai bị Rollback ngay lập tức.<br>• *Cách kiểm:* Xem phương thức `CreateBookingAsync` trong `BookingRepository.cs`. |
| **K2.3** | 5đ | Mọi truy vấn tham số hóa, không nối chuỗi SQL | `Plugins/Cinema.DataStore.SQL.Dapper/Repositories/` | Toàn bộ các file Repository | • 100% câu truy vấn Dapper sử dụng Parameterized Query với `@Param` (ví dụ: `@ShowtimeId`, `@BookingReference`, `@UserId`). Tuyệt đối không nối chuỗi SQL trực tiếp, ngăn chặn hoàn toàn SQL Injection.<br>• *Cách kiểm:* Tìm `$"SELECT` hoặc `+ "WHERE` trong Plugins (kết quả: 0 kết quả nối chuỗi). |
| **K2.4** | 6đ | Luồng giao dịch chính chạy end-to-end và dữ liệu nằm trong SQL Server | Giao diện đặt vé Web & Bảng `Booking`, `Ticket` trong CSDL `CinemaDB` | Toàn luồng | • Người dùng chọn phim $\rightarrow$ chọn suất chiếu $\rightarrow$ chọn ghế trên sơ đồ $\rightarrow$ điền thông tin $\rightarrow$ thanh toán $\rightarrow$ dữ liệu được lưu thành công vào SQL Server và xuất mã vé.<br>• *Cách kiểm:* Thực hiện đặt vé trên Web, sau đó mở bảng `Booking` và `Ticket` trong SSMS kiểm tra dữ liệu thật. |

---

### NHÓM K3: GIAO DIỆN & TRẢI NGHIỆM NGƯỜI DÙNG (20 ĐIỂM)

| Mã | Điểm | Tiêu chí kiểm tra | Đường dẫn file làm bằng chứng | Số dòng | Mô tả chi tiết minh chứng & Cách kiểm |
| :---: | :---: | :--- | :--- | :---: | :--- |
| **K3.1** | 6đ | Ít nhất 3 component tái sử dụng có `[Parameter]` / `EventCallback` | 1. `Cinema.Web/Components/Controls/SeatMapComponent.razor`<br>2. `Cinema.Web/Components/Controls/MovieCardComponent.razor`<br>3. `Cinema.Web/Components/Controls/ConfirmModalComponent.razor` | `SeatMapComponent.razor`: Dòng 80–110<br>`MovieCardComponent.razor`: Dòng 20–35<br>`ConfirmModalComponent.razor`: Dòng 15–30 | • `SeatMapComponent`: `[Parameter] ShowtimeId`, `EventCallback<List<SeatDto>> OnSelectionChanged`.<br>• `MovieCardComponent`: `[Parameter] Movie`, `EventCallback<Movie> OnSelectMovie`.<br>• `ConfirmModalComponent`: `[Parameter] Title`, `EventCallback<bool> OnResult`.<br>• *Cách kiểm:* Mở thư mục `Cinema.Web/Components/Controls/`. |
| **K3.2** | 5đ | EditForm + DataAnnotations; lỗi nhập liệu hiển thị cạnh ô nhập | `Cinema.Web/Components/Pages/BookingPage.razor`<br>`Cinema.Web/Components/Pages/RegisterPage.razor` | `BookingPage.razor`: Dòng 140–185<br>`RegisterPage.razor`: Dòng 40–105 | • Sử dụng `<EditForm Model="..." OnValidSubmit="...">`, `<DataAnnotationsValidator />`, `<InputText>` và `<ValidationMessage For="..." />`. Lỗi nhập liệu hiển thị trực tiếp ngay dưới trường tương ứng.<br>• *Cách kiểm:* Nhập sai email/sđt hoặc bỏ trống trường bắt buộc, bấm submit để thấy thông báo lỗi cạnh ô nhập. |
| **K3.3** | 4đ | Ca vi phạm 2 luật nghiệp vụ hiện thông báo thân thiện, không văng lỗi | `Cinema.Web/Components/Controls/SeatMapComponent.razor`<br>`Cinema.Web/Components/Pages/BookingPage.razor`<br>`Cinema.Web/Components/Pages/Admin/ManageShowtimesPage.razor` | `SeatMapComponent.razor`: Dòng 25–40, 160–185<br>`ManageShowtimesPage.razor`: Dòng 90–120 | • Vi phạm chọn ghế để lại ghế đơn lẻ hoặc tranh chấp ghế đang giữ: hiển thị banner cảnh báo thân thiện và tự động tắt sau 4 giây.<br>• Vi phạm trùng lịch chiếu cùng phòng: hiển thị thông báo chi tiết trên Modal quản lý, không để lộ crash hay ngoại lệ hệ thống ra màn hình.<br>• *Cách kiểm:* Cố tình chọn nhảy cóc để lại 1 ghế trống lẻ, hoặc xếp 2 suất chiếu cùng phòng cách nhau < 15 phút. |
| **K3.4** | 5đ | Dùng được trên màn hình điện thoại (DevTools 390px) | `Cinema.Web/wwwroot/app.css`<br>`Cinema.Web/Components/Controls/SeatMapComponent.razor` | Toàn bộ CSS bố cục responsive | • Sơ đồ ghế hỗ trợ cuộn ngang mượt mà (`overflow-x: auto`), thanh tóm tắt đơn vé và menu điều hướng co giãn hoàn chỉnh trên màn hình di động độ rộng 390px.<br>• *Cách kiểm:* Mở F12 DevTools, chọn chế độ giả lập điện thoại bề rộng 390px (iPhone 12/14/15). |

---

### NHÓM K4: BẢO MẬT, PHÂN QUYỀN & TÍNH NĂNG NÂNG CAO (15 ĐIỂM)

| Mã | Điểm | Tiêu chí kiểm tra | Đường dẫn file làm bằng chứng | Số dòng | Mô tả chi tiết minh chứng & Cách kiểm |
| :---: | :---: | :--- | :--- | :---: | :--- |
| **K4.1** | 4đ | Đăng nhập, đăng xuất; ít nhất 2 vai trò | `Cinema.Web/Components/Pages/LoginPage.razor`<br>`Cinema.Web/Components/Pages/RegisterPage.razor`<br>`Cinema.Web/Services/CustomAuthStateProvider.cs` | `LoginPage.razor`: Dòng 20–85<br>`CustomAuthStateProvider.cs`: Dòng 15–70 | • Cung cấp 2 vai trò phân quyền: **Admin** (`admin` / `admin123`) và **Customer** (`khachhang` / `khach123`).<br>• Hỗ trợ đăng ký người dùng mới, đăng nhập, đăng xuất và quản lý vé cá nhân tại `/my-bookings`.<br>• *Cách kiểm:* Demo đăng nhập tài khoản Customer và Admin, sau đó đăng xuất. |
| **K4.2** | 5đ | Tài khoản thường gõ thẳng URL trang quản trị bị chặn | `Cinema.Web/Components/Routes.razor`<br>`Cinema.Web/Components/Pages/Admin/*.razor` | `Routes.razor`: Dòng 6–25<br>Tất cả file Admin: Dòng 1–3 | • Các trang Admin được bảo vệ bởi `@attribute [Authorize(Roles = "Admin")]`.<br>• Khi người dùng không có quyền quản trị gõ thẳng URL (như `/admin`, `/admin/movies`, `/admin/showtimes`), `AuthorizeRouteView` chặn và chuyển vào nhánh `<NotAuthorized>` hiển thị màn hình từ chối truy cập 403 chuyên nghiệp.<br>• *Cách kiểm:* Đăng nhập bằng `khachhang`, sau đó gõ URL `/admin` trên thanh địa chỉ. |
| **K4.3** | 6đ | Tính năng nâng cao: AI đạt đủ mục 10, hoặc xuất báo cáo (Excel/PDF) từ dữ liệu thật | `Cinema.UseCases/AI/SemanticMovieSearchUseCase.cs`<br>`Cinema.Web/Components/Controls/AiMovieAssistant.razor`<br>`Cinema.Web/Components/Pages/Admin/OccupancyReportPage.razor` | `SemanticMovieSearchUseCase.cs`: Dòng 15–330<br>`AiMovieAssistant.razor`: Dòng 1–150<br>`OccupancyReportPage.razor`: Dòng 20–190 | • **Đạt đủ 5/5 yêu cầu Mục 10 về AI:**<br>  1. API Key đặt trong biến môi trường `GEMINI_API_KEY` / User Secrets, không commit lên git.<br>  2. Đặt trong UseCase riêng (`ISemanticMovieSearchUseCase`), không gọi trực tiếp từ `.razor`.<br>  3. Có xử lý Offline Fallback khi mất mạng hoặc hết quota.<br>  4. AI chỉ gợi ý, người dùng tự xác nhận chọn ghế và thanh toán trước khi lưu vào CSDL.<br>  5. Tích hợp Google Search Grounding thời gian thực và ghi nhớ hội thoại nhiều lượt.<br>• **Báo cáo từ dữ liệu thật:** Trang Báo cáo tỷ lệ lấp đầy (`OccupancyReportPage.razor`) tính toán từ các bảng `Showtime`, `Seat`, `Ticket` trong CSDL SQL Server và hỗ trợ in/xuất PDF.<br>• *Cách kiểm:* Mở khung chat AI hỏi phim/thầy cô/khoa học, hoặc vào Admin xem báo cáo lấp đầy. |

---

### NHÓM K5: HỒ SƠ THIẾT KẾ (UML) & MINH CHỨNG LÀM VIỆC (15 ĐIỂM)

| Mã | Điểm | Tiêu chí kiểm tra | Đường dẫn file làm bằng chứng | Số dòng | Mô tả chi tiết minh chứng & Cách kiểm |
| :---: | :---: | :--- | :--- | :---: | :--- |
| **K5.1** | 6đ | Use Case, Class, Sequence (PlantUML) khớp tên lớp và luồng trong code | `docs/uml/usecase_diagram.puml`<br>`docs/uml/class_diagram.puml`<br>`docs/uml/sequence_diagram.puml` | Toàn bộ 3 file trong `docs/uml/` | • 3 sơ đồ PlantUML chuẩn xác:<br>  1. `usecase_diagram.puml`: Phân rã Use Case của Customer và Admin.<br>  2. `class_diagram.puml`: Khớp 100% tên lớp 4 tầng Clean Architecture (Entities, Repositories, UseCases, Presentation).<br>  3. `sequence_diagram.puml`: Mô tả luồng giao dịch đặt vé end-to-end kèm Transaction ACID và cơ chế giữ chỗ.<br>• *Cách kiểm:* So sánh tên lớp trong `class_diagram.puml` với code C# thực tế. |
| **K5.2** | 6đ | Vấn đáp: mỗi thành viên chỉ đúng dòng cài luật và giải thích Dependency Rule | `Cinema.CoreBusiness/Rules/SeatBookingRule.cs`<br>`Cinema.CoreBusiness/Rules/ShowtimeScheduleRule.cs`<br>`Cinema.UseCases/Bookings/CreateBookingUseCase.cs` | `SeatBookingRule.cs`: Dòng 14–210<br>`ShowtimeScheduleRule.cs`: Dòng 10–65<br>`CreateBookingUseCase.cs`: Dòng 35–65 | • **Vị trí cài luật:** Chỉ chính xác dòng code cài Luật 1 trong `SeatBookingRule.cs` và Luật 2 trong `ShowtimeScheduleRule.cs`.<br>• **Giải thích Dependency Rule:** Tầng bên trong (`CoreBusiness`, `UseCases`) không bao giờ phụ thuộc tầng bên ngoài (`Dapper`, `Web`). Tầng Web chỉ biết Plugin cụ thể tại Composition Root (`Program.cs`) thông qua Dependency Injection.<br>• *Cách kiểm:* Vấn đáp trực tiếp với từng thành viên. |
| **K5.3** | 3đ | Mọi thành viên có commit, rải trên ít nhất 3 tuần | `git log --date=short`<br>`git shortlog -sn` | Git commit history (73 commits) | • Lịch sử commit minh bạch rải đều qua hơn 3 tuần thực hiện đồ án (từ 05/10/2026 đến 22/10/2026).<br>• 100% commit có thông điệp chuẩn xác, rõ ràng theo quy chuẩn Conventional Commits.<br>• *Cách kiểm:* Chạy `git shortlog -sn` và `git log --date=short` trong Terminal. |