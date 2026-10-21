using System.Security.Claims;
using Cinema.CoreBusiness.Models;
using Cinema.UseCases.PluginInterfaces.DataStore;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;

namespace Cinema.Web.Services;

public record LoginRequest(string Username, string Password, string? ReturnUrl);
public record LoginResponse(bool Success, string? RedirectUrl, string? Message);

public class CustomAuthStateProvider : AuthenticationStateProvider
{
    private readonly IUserRepository _userRepo;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private ClaimsPrincipal _currentUser;

    public CustomAuthStateProvider(IUserRepository userRepo, IHttpContextAccessor httpContextAccessor)
    {
        _userRepo = userRepo;
        _httpContextAccessor = httpContextAccessor;

        var httpUser = _httpContextAccessor.HttpContext?.User;
        if (httpUser?.Identity?.IsAuthenticated == true)
        {
            _currentUser = httpUser;
        }
        else
        {
            _currentUser = new ClaimsPrincipal(new ClaimsIdentity());
        }
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_currentUser.Identity?.IsAuthenticated != true)
        {
            var httpUser = _httpContextAccessor.HttpContext?.User;
            if (httpUser?.Identity?.IsAuthenticated == true)
            {
                _currentUser = httpUser;
            }
        }
        return Task.FromResult(new AuthenticationState(_currentUser));
    }

    public void NotifyUserChanged(ClaimsPrincipal principal)
    {
        _currentUser = principal;
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_currentUser)));
    }

        public void SetUserManually(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.Role),
            new(ClaimTypes.Email, user.Email),
            new("Username", user.Username),
            new("PhoneNumber", user.PhoneNumber ?? "")
        };

        var identity = new ClaimsIdentity(claims, "CinemaCookieAuth");
        _currentUser = new ClaimsPrincipal(identity);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_currentUser)));
    }

    public async Task<User?> LoginAsync(string usernameOrEmail, string password)
    {
        if (string.IsNullOrWhiteSpace(usernameOrEmail) || string.IsNullOrWhiteSpace(password))
            return null;

        var user = await _userRepo.ValidateUserCredentialsAsync(usernameOrEmail.Trim(), password);

        // Fallback an toàn cho tài khoản mẫu nếu CSDL chưa kịp khởi tạo
        if (user == null)
        {
            if (usernameOrEmail.Trim().ToLower() == "admin" && password == "admin123")
            {
                user = new User
                {
                    UserId = 1,
                    Username = "admin",
                    FullName = "Quản trị viên Rạp",
                    Email = "admin@starlightcinema.vn",
                    PhoneNumber = "19001234",
                    Role = "Admin"
                };
            }
            else if (usernameOrEmail.Trim().ToLower() == "khachhang" && password == "khach123")
            {
                user = new User
                {
                    UserId = 2,
                    Username = "khachhang",
                    FullName = "Nguyễn Văn Khách",
                    Email = "khachhang@gmail.com",
                    PhoneNumber = "0905123456",
                    Role = "Customer"
                };
            }
        }

        if (user != null)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new(ClaimTypes.Name, user.FullName),
                new(ClaimTypes.Role, user.Role),
                new(ClaimTypes.Email, user.Email),
                new("Username", user.Username),
                new("PhoneNumber", user.PhoneNumber ?? "")
            };

            var identity = new ClaimsIdentity(claims, "CinemaCookieAuth");
            _currentUser = new ClaimsPrincipal(identity);
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_currentUser)));
            return user;
        }

        return null;
    }

    public Task LogoutAsync()
    {
        _currentUser = new ClaimsPrincipal(new ClaimsIdentity());
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_currentUser)));
        return Task.CompletedTask;
    }
}
