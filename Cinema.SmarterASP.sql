
-- Drop tables in order of dependency if they exist for clean recreate
IF OBJECT_ID('dbo.Ticket', 'U') IS NOT NULL DROP TABLE dbo.Ticket;
IF OBJECT_ID('dbo.Booking', 'U') IS NOT NULL DROP TABLE dbo.Booking;
IF OBJECT_ID('dbo.[User]', 'U') IS NOT NULL DROP TABLE dbo.[User];
IF OBJECT_ID('dbo.Booking', 'U') IS NOT NULL DROP TABLE dbo.Booking;
IF OBJECT_ID('dbo.Showtime', 'U') IS NOT NULL DROP TABLE dbo.Showtime;
IF OBJECT_ID('dbo.Seat', 'U') IS NOT NULL DROP TABLE dbo.Seat;
IF OBJECT_ID('dbo.Auditorium', 'U') IS NOT NULL DROP TABLE dbo.Auditorium;
IF OBJECT_ID('dbo.Movie', 'U') IS NOT NULL DROP TABLE dbo.Movie;
GO

-- Table 0: User (Nguoi dung / Quan tri / Thanh vien)
CREATE TABLE dbo.[User] (
    UserId INT IDENTITY(1,1) PRIMARY KEY,
    Username NVARCHAR(50) NOT NULL UNIQUE,
    PasswordHash NVARCHAR(255) NOT NULL,
    FullName NVARCHAR(100) NOT NULL,
    Email NVARCHAR(100) NOT NULL UNIQUE,
    PhoneNumber NVARCHAR(20) NULL,
    Role NVARCHAR(20) NOT NULL DEFAULT 'Customer', -- 'Customer' hoac 'Admin'
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

-- Table 1: Movie
CREATE TABLE dbo.Movie (
    MovieId INT IDENTITY(1,1) PRIMARY KEY,
    Title NVARCHAR(200) NOT NULL,
    Director NVARCHAR(100) NOT NULL,
    Cast NVARCHAR(250) NOT NULL,
    Genre NVARCHAR(100) NOT NULL,
    DurationMinutes INT NOT NULL CHECK (DurationMinutes > 0),
    ReleaseDate DATE NOT NULL,
    AgeRating NVARCHAR(10) NOT NULL DEFAULT 'P', -- P, K, T13, T16, T18
    PosterUrl NVARCHAR(500) NOT NULL,
    TrailerUrl NVARCHAR(500) NULL,
    Description NVARCHAR(MAX) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1
);
GO

-- Table 2: Auditorium (Phòng chiếu)
CREATE TABLE dbo.Auditorium (
    AuditoriumId INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL,
    TotalSeats INT NOT NULL CHECK (TotalSeats > 0),
    ScreenType NVARCHAR(50) NOT NULL DEFAULT 'Standard 2D' -- 'Dolby Atmos', 'IMAX Laser'
);
GO

-- Table 3: Seat (Ghế ngồi)
CREATE TABLE dbo.Seat (
    SeatId INT IDENTITY(1,1) PRIMARY KEY,
    AuditoriumId INT NOT NULL,
    RowCode VARCHAR(5) NOT NULL, -- A, B, C, D, E, F
    SeatNumber INT NOT NULL CHECK (SeatNumber > 0),
    SeatType NVARCHAR(20) NOT NULL DEFAULT 'Standard', -- Standard, VIP, Sweetbox
    PriceModifier DECIMAL(10,2) NOT NULL DEFAULT 0,
    CONSTRAINT FK_Seat_Auditorium FOREIGN KEY (AuditoriumId) REFERENCES dbo.Auditorium(AuditoriumId) ON DELETE CASCADE,
    CONSTRAINT UQ_Seat_Auditorium_Row_Num UNIQUE (AuditoriumId, RowCode, SeatNumber)
);
GO

-- Table 4: Showtime (Suất chiếu)
CREATE TABLE dbo.Showtime (
    ShowtimeId INT IDENTITY(1,1) PRIMARY KEY,
    MovieId INT NOT NULL,
    AuditoriumId INT NOT NULL,
    StartTime DATETIME2 NOT NULL,
    EndTime DATETIME2 NOT NULL,
    BasePrice DECIMAL(10,2) NOT NULL CHECK (BasePrice > 0),
    IsActive BIT NOT NULL DEFAULT 1,
    CONSTRAINT FK_Showtime_Movie FOREIGN KEY (MovieId) REFERENCES dbo.Movie(MovieId),
    CONSTRAINT FK_Showtime_Auditorium FOREIGN KEY (AuditoriumId) REFERENCES dbo.Auditorium(AuditoriumId),
    CONSTRAINT CK_Showtime_Duration CHECK (EndTime > StartTime)
);
GO

-- Table 5: Booking (Header)
CREATE TABLE dbo.Booking (
    BookingId INT IDENTITY(1,1) PRIMARY KEY,
    BookingReference VARCHAR(30) NOT NULL UNIQUE,
    ShowtimeId INT NOT NULL,
    UserId INT NULL,
    CustomerName NVARCHAR(150) NOT NULL,
    CustomerEmail VARCHAR(150) NOT NULL,
    CustomerPhone VARCHAR(20) NOT NULL,
    TotalAmount DECIMAL(10,2) NOT NULL CHECK (TotalAmount >= 0),
    BookingTime DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    Status NVARCHAR(30) NOT NULL DEFAULT 'Confirmed',
    PaymentMethod NVARCHAR(50) NOT NULL DEFAULT 'VNPAY / QR',
    CONSTRAINT FK_Booking_Showtime FOREIGN KEY (ShowtimeId) REFERENCES dbo.Showtime(ShowtimeId),
    CONSTRAINT FK_Booking_User FOREIGN KEY (UserId) REFERENCES dbo.[User](UserId)
);
GO

-- Table 6: Ticket (Line)
CREATE TABLE dbo.Ticket (
    TicketId INT IDENTITY(1,1) PRIMARY KEY,
    BookingId INT NOT NULL,
    ShowtimeId INT NOT NULL,
    SeatId INT NOT NULL,
    TicketPrice DECIMAL(10,2) NOT NULL CHECK (TicketPrice >= 0),
    TicketCode VARCHAR(50) NOT NULL UNIQUE,
    CONSTRAINT FK_Ticket_Booking FOREIGN KEY (BookingId) REFERENCES dbo.Booking(BookingId) ON DELETE CASCADE,
    CONSTRAINT FK_Ticket_Showtime FOREIGN KEY (ShowtimeId) REFERENCES dbo.Showtime(ShowtimeId),
    CONSTRAINT FK_Ticket_Seat FOREIGN KEY (SeatId) REFERENCES dbo.Seat(SeatId),
    -- LUẬT 1: Một ghế trong một suất chiếu chỉ bán được 1 lần!
    CONSTRAINT UQ_Showtime_Seat UNIQUE (ShowtimeId, SeatId)
);
GO

-- SEED DATA
-- 1. Movies
INSERT INTO dbo.Movie (Title, Director, Cast, Genre, DurationMinutes, ReleaseDate, AgeRating, PosterUrl, Description)
VALUES
(N'Mai', N'Trấn Thành', N'Phương Anh Đào, Tuấn Trần, Trấn Thành, Hồng Đào', N'Tâm lý, Tình cảm', 131, '2024-02-10', 'T18', 'https://images.unsplash.com/photo-1518676590629-3dcbd9c5a5c9?w=600&auto=format&fit=crop&q=80', N'Một câu chuyện tình đầy trắc trở giữa Mai - người phụ nữ massage nhiều đau thương và Dương - chàng nhạc công lãng tử nhà giàu.'),
(N'Lật Mặt 7: Một Điều Ước', N'Lý Hải', N'Thanh Hiền, Trương Minh Cường, Đinh Y Nhung, Quách Ngọc Tuyên', N'Gia đình, Chính kịch', 138, '2024-04-26', 'K', 'https://images.unsplash.com/photo-1489599849927-2ee91cede3ba?w=600&auto=format&fit=crop&q=80', N'Câu chuyện cảm động về người mẹ 73 tuổi một mình nuôi 5 người con khôn lớn. Khi bà gặp nạn, ai sẽ là người chăm sóc mẹ già?'),
(N'Dune: Hành Tinh Cát - Phần 2', N'Denis Villeneuve', N'Timothée Chalamet, Zendaya, Rebecca Ferguson, Javier Bardem', N'Khoa học viễn tưởng, Hành động', 166, '2024-03-01', 'T16', 'https://images.unsplash.com/photo-1534447677768-be436bb09401?w=600&auto=format&fit=crop&q=80', N'Hành trình của Paul Atreides cùng người Fremen trả thù những kẻ đã hủy diệt gia tộc và đối mặt với định mệnh vũ trụ.'),
(N'Godzilla x Kong: Đế Chế Mới', N'Adam Wingard', N'Rebecca Hall, Brian Tyree Henry, Dan Stevens', N'Hành động, Phiêu lưu', 115, '2024-03-29', 'T13', 'https://images.unsplash.com/photo-1579783900882-c0d3dad7b119?w=600&auto=format&fit=crop&q=80', N'Hai titan hùng mạnh Godzilla và Kong phải liên thủ để đối đầu với mối đe dọa khổng lồ chưa từng thấy ẩn sâu trong Trái Đất Rỗng.'),
(N'Kung Fu Panda 4', N'Mike Mitchell', N'Jack Black, Awkwafina, Viola Davis, Dustin Hoffman', N'Hoạt hình, Hài, Phiêu lưu', 94, '2024-03-08', 'P', 'https://images.unsplash.com/photo-1536440136628-849c177e76a1?w=600&auto=format&fit=crop&q=80', N'Po bước vào vai trò Thủ lĩnh tinh thần của Thung lũng Bình Yên và phải tìm kiếm một Thần Long Đại Hiệp mới trước sự đe dọa của Tắc Kè Bông.');
GO

-- 2. Auditoriums
INSERT INTO dbo.Auditorium (Name, TotalSeats, ScreenType)
VALUES 
(N'Phòng Chiếu 01 - Dolby Atmos', 48, N'Dolby Atmos Cinema'),
(N'Phòng Chiếu 02 - IMAX Laser', 48, N'IMAX Laser 3D');
GO

-- 3. Seats (Rows A-F: 8 seats per row = 48 seats per auditorium)
-- Row A, B: Standard (Modifier = 0)
-- Row C, D, E: VIP (Modifier = 20,000 VND)
-- Row F: Sweetbox / Couple (Modifier = 40,000 VND)
DECLARE @AudId INT = 1;
WHILE @AudId <= 2
BEGIN
    -- Row A
    INSERT INTO dbo.Seat (AuditoriumId, RowCode, SeatNumber, SeatType, PriceModifier)
    SELECT @AudId, 'A', Number, 'Standard', 0 FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8)) AS N(Number);

    -- Row B
    INSERT INTO dbo.Seat (AuditoriumId, RowCode, SeatNumber, SeatType, PriceModifier)
    SELECT @AudId, 'B', Number, 'Standard', 0 FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8)) AS N(Number);

    -- Row C (VIP)
    INSERT INTO dbo.Seat (AuditoriumId, RowCode, SeatNumber, SeatType, PriceModifier)
    SELECT @AudId, 'C', Number, 'VIP', 20000 FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8)) AS N(Number);

    -- Row D (VIP)
    INSERT INTO dbo.Seat (AuditoriumId, RowCode, SeatNumber, SeatType, PriceModifier)
    SELECT @AudId, 'D', Number, 'VIP', 20000 FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8)) AS N(Number);

    -- Row E (VIP)
    INSERT INTO dbo.Seat (AuditoriumId, RowCode, SeatNumber, SeatType, PriceModifier)
    SELECT @AudId, 'E', Number, 'VIP', 20000 FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8)) AS N(Number);

    -- Row F (Sweetbox / Couple)
    INSERT INTO dbo.Seat (AuditoriumId, RowCode, SeatNumber, SeatType, PriceModifier)
    SELECT @AudId, 'F', Number, 'Sweetbox', 40000 FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8)) AS N(Number);

    SET @AudId = @AudId + 1;
END;
GO

-- 4. Showtimes (Schedule dynamically around today)
DECLARE @Today DATE = CAST(GETDATE() AS DATE);

-- Auditorium 1 (Phòng 1)
INSERT INTO dbo.Showtime (MovieId, AuditoriumId, StartTime, EndTime, BasePrice)
VALUES
(1, 1, DATEADD(MINUTE, 9*60, CAST(@Today AS DATETIME2)), DATEADD(MINUTE, 9*60 + 131 + 15, CAST(@Today AS DATETIME2)), 85000),
(2, 1, DATEADD(MINUTE, 12*60, CAST(@Today AS DATETIME2)), DATEADD(MINUTE, 12*60 + 138 + 15, CAST(@Today AS DATETIME2)), 85000),
(3, 1, DATEADD(MINUTE, 15*60 + 30, CAST(@Today AS DATETIME2)), DATEADD(MINUTE, 15*60 + 30 + 166 + 15, CAST(@Today AS DATETIME2)), 95000),
(1, 1, DATEADD(MINUTE, 19*60, CAST(@Today AS DATETIME2)), DATEADD(MINUTE, 19*60 + 131 + 15, CAST(@Today AS DATETIME2)), 95000);

-- Auditorium 2 (Phòng 2)
INSERT INTO dbo.Showtime (MovieId, AuditoriumId, StartTime, EndTime, BasePrice)
VALUES
(4, 2, DATEADD(MINUTE, 9*60 + 30, CAST(@Today AS DATETIME2)), DATEADD(MINUTE, 9*60 + 30 + 115 + 15, CAST(@Today AS DATETIME2)), 90000),
(5, 2, DATEADD(MINUTE, 12*60 + 30, CAST(@Today AS DATETIME2)), DATEADD(MINUTE, 12*60 + 30 + 94 + 15, CAST(@Today AS DATETIME2)), 80000),
(2, 2, DATEADD(MINUTE, 15*60, CAST(@Today AS DATETIME2)), DATEADD(MINUTE, 15*60 + 138 + 15, CAST(@Today AS DATETIME2)), 90000),
(4, 2, DATEADD(MINUTE, 18*60 + 30, CAST(@Today AS DATETIME2)), DATEADD(MINUTE, 18*60 + 30 + 115 + 15, CAST(@Today AS DATETIME2)), 95000);

-- Tomorrow Showtimes
DECLARE @Tomorrow DATE = DATEADD(DAY, 1, @Today);
INSERT INTO dbo.Showtime (MovieId, AuditoriumId, StartTime, EndTime, BasePrice)
VALUES
(1, 1, DATEADD(MINUTE, 10*60, CAST(@Tomorrow AS DATETIME2)), DATEADD(MINUTE, 10*60 + 131 + 15, CAST(@Tomorrow AS DATETIME2)), 85000),
(3, 1, DATEADD(MINUTE, 14*60, CAST(@Tomorrow AS DATETIME2)), DATEADD(MINUTE, 14*60 + 166 + 15, CAST(@Tomorrow AS DATETIME2)), 95000),
(5, 2, DATEADD(MINUTE, 10*60, CAST(@Tomorrow AS DATETIME2)), DATEADD(MINUTE, 10*60 + 94 + 15, CAST(@Tomorrow AS DATETIME2)), 80000),
(2, 2, DATEADD(MINUTE, 14*60, CAST(@Tomorrow AS DATETIME2)), DATEADD(MINUTE, 14*60 + 138 + 15, CAST(@Tomorrow AS DATETIME2)), 90000);
GO

-- 5. Seed Bookings & Tickets for Showtime 1 (Mai at 09:00 in Aud 1)
-- Seats C3, C4 booked by Nguyen Van A
INSERT INTO dbo.Booking (BookingReference, ShowtimeId, CustomerName, CustomerEmail, CustomerPhone, TotalAmount, Status, PaymentMethod)
VALUES ('BK-20261001-001', 1, N'Nguyễn Văn An', 'nguyenvanan@gmail.com', '0905123456', 210000, 'Confirmed', 'VNPAY-QR');

DECLARE @BookingId1 INT = SCOPE_IDENTITY();
-- Seat C3: Row C, Seat 3 (Aud 1) -> Base 85k + 20k = 105k
-- Seat C4: Row C, Seat 4 (Aud 1) -> Base 85k + 20k = 105k
DECLARE @SeatC3 INT = (SELECT SeatId FROM dbo.Seat WHERE AuditoriumId = 1 AND RowCode = 'C' AND SeatNumber = 3);
DECLARE @SeatC4 INT = (SELECT SeatId FROM dbo.Seat WHERE AuditoriumId = 1 AND RowCode = 'C' AND SeatNumber = 4);

INSERT INTO dbo.Ticket (BookingId, ShowtimeId, SeatId, TicketPrice, TicketCode)
VALUES
(@BookingId1, 1, @SeatC3, 105000, 'TK-ST1-C3-001'),
(@BookingId1, 1, @SeatC4, 105000, 'TK-ST1-C4-002');

-- Seats D4, D5 booked by Le Thi B
INSERT INTO dbo.Booking (BookingReference, ShowtimeId, CustomerName, CustomerEmail, CustomerPhone, TotalAmount, Status, PaymentMethod)
VALUES ('BK-20261001-002', 1, N'Lê Thị Bình', 'lethibinh@gmail.com', '0914987654', 210000, 'Confirmed', 'ATM');

DECLARE @BookingId2 INT = SCOPE_IDENTITY();
DECLARE @SeatD4 INT = (SELECT SeatId FROM dbo.Seat WHERE AuditoriumId = 1 AND RowCode = 'D' AND SeatNumber = 4);
DECLARE @SeatD5 INT = (SELECT SeatId FROM dbo.Seat WHERE AuditoriumId = 1 AND RowCode = 'D' AND SeatNumber = 5);

INSERT INTO dbo.Ticket (BookingId, ShowtimeId, SeatId, TicketPrice, TicketCode)
VALUES
(@BookingId2, 1, @SeatD4, 105000, 'TK-ST1-D4-003'),
(@BookingId2, 1, @SeatD5, 105000, 'TK-ST1-D5-004');
GO

-- Seed Users (2 Vai tro: Admin va Customer)
INSERT INTO dbo.[User] (Username, PasswordHash, FullName, Email, PhoneNumber, Role)
VALUES 
('admin', 'admin123', N'Quản trị viên Rạp', 'admin@starlightcinema.vn', '19001234', 'Admin'),
('khachhang', 'khach123', N'Nguyễn Văn Khách', 'khachhang@gmail.com', '0905123456', 'Customer');
GO

PRINT 'CinemaDB database created and initialized with 7 tables and seed data successfully!';

