﻿using Cinema.DataStore.SQL.Dapper;
using Cinema.DataStore.SQL.Dapper.Repositories;
using Cinema.StateStore.DI;
using Cinema.UseCases.AI;
using Cinema.UseCases.Auditoriums;
using Cinema.UseCases.Bookings;
using Cinema.UseCases.Movies;
using Cinema.UseCases.PluginInterfaces.DataStore;
using Cinema.UseCases.PluginInterfaces.Services;
using Cinema.UseCases.PluginInterfaces.StateStore;
using Cinema.UseCases.Reports;
using Cinema.UseCases.Seats;
using Cinema.UseCases.Showtimes;
using Cinema.UseCases.Users;
using Cinema.Web.Components;
using Cinema.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpContextAccessor();
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Add services to the container.
builder.Services.AddHttpClient();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// 1. Connection string & Dapper setup
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Server=localhost;Database=CinemaDB;Trusted_Connection=True;TrustServerCertificate=True;";
builder.Services.AddSingleton<ISqlConnectionFactory>(new SqlConnectionFactory(connectionString));

// 2. DataStore Repositories
builder.Services.AddScoped<IMovieRepository, MovieRepository>();
builder.Services.AddScoped<IAuditoriumRepository, AuditoriumRepository>();
builder.Services.AddScoped<ISeatRepository, SeatRepository>();
builder.Services.AddScoped<IShowtimeRepository, ShowtimeRepository>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();

// 3. StateStore & Services
builder.Services.AddSingleton<ISeatHoldingStateStore, SeatHoldingStateStore>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();

// 4. UseCases
builder.Services.AddTransient<IViewMoviesUseCase, ViewMoviesUseCase>();
builder.Services.AddTransient<ICreateMovieUseCase, CreateMovieUseCase>();
builder.Services.AddTransient<IUpdateMovieUseCase, UpdateMovieUseCase>();
builder.Services.AddTransient<IDeleteMovieUseCase, DeleteMovieUseCase>();
builder.Services.AddTransient<IViewShowtimesUseCase, ViewShowtimesUseCase>();
builder.Services.AddTransient<ICreateShowtimeUseCase, CreateShowtimeUseCase>();
builder.Services.AddTransient<IDeleteShowtimeUseCase, DeleteShowtimeUseCase>();
builder.Services.AddTransient<IViewAuditoriumsUseCase, ViewAuditoriumsUseCase>();
builder.Services.AddTransient<IGetSeatMapUseCase, GetSeatMapUseCase>();
builder.Services.AddTransient<IHoldSeatsUseCase, HoldSeatsUseCase>();
builder.Services.AddTransient<ICreateBookingUseCase, CreateBookingUseCase>();
builder.Services.AddTransient<IGetBookingDetailsUseCase, GetBookingDetailsUseCase>();
builder.Services.AddTransient<IViewRecentBookingsUseCase, ViewRecentBookingsUseCase>();
builder.Services.AddTransient<IViewOccupancyReportUseCase, ViewOccupancyReportUseCase>();
builder.Services.AddTransient<IViewDashboardOverviewUseCase, ViewDashboardOverviewUseCase>();
builder.Services.AddTransient<IRegisterUserUseCase, RegisterUserUseCase>();
builder.Services.AddTransient<ILoginUserUseCase, LoginUserUseCase>();
builder.Services.AddTransient<IViewMyBookingsUseCase, ViewMyBookingsUseCase>();
builder.Services.AddTransient<ISemanticMovieSearchUseCase>(sp =>
{
    var movieRepo = sp.GetRequiredService<IMovieRepository>();
    var showtimeRepo = sp.GetRequiredService<IShowtimeRepository>();
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var config = sp.GetRequiredService<IConfiguration>();
    var apiKey = config["Gemini:ApiKey"];
    var model = config["Gemini:Model"] ?? "gemini-2.5-flash";
    return new SemanticMovieSearchUseCase(movieRepo, showtimeRepo, httpClientFactory.CreateClient(), apiKey, model);
});

// 5. Auth & Session
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "CinemaAuthCookie";
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
        options.LogoutPath = "/logout";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<CustomAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<CustomAuthStateProvider>());
builder.Services.AddScoped<UserSessionService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
