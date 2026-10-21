using Cinema.CoreBusiness.Utilities;
using Cinema.UseCases.DTOs;
using Cinema.UseCases.PluginInterfaces.DataStore;

namespace Cinema.UseCases.Reports;

public class ViewDashboardOverviewUseCase : IViewDashboardOverviewUseCase
{
    private readonly IDashboardRepository _dashboardRepo;

    public ViewDashboardOverviewUseCase(IDashboardRepository dashboardRepo)
    {
        _dashboardRepo = dashboardRepo;
    }

    public async Task<DashboardOverviewDto> ExecuteAsync()
    {
        return await _dashboardRepo.GetDashboardOverviewAsync(VietnamTime.Today);
    }
}
