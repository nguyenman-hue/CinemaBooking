using Cinema.CoreBusiness.Utilities;
using Cinema.CoreBusiness.Models;
using Cinema.UseCases.PluginInterfaces.DataStore;

namespace Cinema.UseCases.Users;

public interface IRegisterUserUseCase
{
    Task<User> ExecuteAsync(string username, string plainPassword, string fullName, string email, string? phone);
}

public class RegisterUserUseCase : IRegisterUserUseCase
{
    private readonly IUserRepository _userRepo;

    public RegisterUserUseCase(IUserRepository userRepo)
    {
        _userRepo = userRepo;
    }

    public async Task<User> ExecuteAsync(string username, string plainPassword, string fullName, string email, string? phone)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Trim().Length < 3)
            throw new ArgumentException("Tên đăng nhập phải có ít nhất 3 ký tự.");

        if (string.IsNullOrWhiteSpace(plainPassword) || plainPassword.Length < 6)
            throw new ArgumentException("Mật khẩu phải có ít nhất 6 ký tự.");

        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length < 2)
            throw new ArgumentException("Họ và tên không được để trống.");

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw new ArgumentException("Địa chỉ email không hợp lệ.");

        username = username.Trim().ToLower();
        email = email.Trim().ToLower();
        fullName = fullName.Trim();
        phone = phone?.Trim();

        var existingUser = await _userRepo.GetUserByUsernameAsync(username);
        if (existingUser != null)
            throw new InvalidOperationException($"Tên đăng nhập '{username}' đã được sử dụng. Vui lòng chọn tên khác.");

        var existingEmail = await _userRepo.GetUserByEmailAsync(email);
        if (existingEmail != null)
            throw new InvalidOperationException($"Địa chỉ email '{email}' đã được đăng ký tài khoản.");

        var newUser = new User
        {
            Username = username,
            FullName = fullName,
            Email = email,
            PhoneNumber = phone,
            Role = "Customer",
            CreatedAt = VietnamTime.Now
        };

        int newUserId = await _userRepo.CreateUserAsync(newUser, plainPassword);
        newUser.UserId = newUserId;
        return newUser;
    }
}
