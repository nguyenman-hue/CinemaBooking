using Cinema.UseCases.DTOs;

namespace Cinema.UseCases.PluginInterfaces.DataStore;

public interface IDashboardRepository
{
    Task<DashboardOverviewDto> GetDashboardOverviewAsync(DateTime today);
}
