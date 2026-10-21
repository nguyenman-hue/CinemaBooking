using System.ComponentModel.DataAnnotations;
using Cinema.CoreBusiness.Utilities;

namespace Cinema.CoreBusiness.Models;

public class Booking
{
    public int BookingId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public int ShowtimeId { get; set; }
    public int? UserId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập họ và tên người đặt")]
    [MinLength(2, ErrorMessage = "Họ và tên tối thiểu 2 ký tự")]
    public string CustomerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ email nhận vé")]
    [EmailAddress(ErrorMessage = "Địa chỉ email không đúng định dạng")]
    public string CustomerEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại liên hệ")]
    [Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
    public string CustomerPhone { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }
    public DateTime BookingTime { get; set; } = VietnamTime.Now;
    public string Status { get; set; } = "Confirmed";
    public string PaymentMethod { get; set; } = "VNPAY-QR";

    public List<Ticket> Tickets { get; set; } = new();
}