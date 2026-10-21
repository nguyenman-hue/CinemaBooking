using Cinema.CoreBusiness.Utilities;
namespace Cinema.CoreBusiness.Models;

public class User
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string Role { get; set; } = "Customer"; // "Customer" hoặc "Admin"
    public DateTime CreatedAt { get; set; } = VietnamTime.Now;
}
