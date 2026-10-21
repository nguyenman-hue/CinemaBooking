using Cinema.UseCases.DTOs;

namespace Cinema.UseCases.Reports;

public interface IViewDashboardOverviewUseCase
{
    Task<DashboardOverviewDto> ExecuteAsync();
}
