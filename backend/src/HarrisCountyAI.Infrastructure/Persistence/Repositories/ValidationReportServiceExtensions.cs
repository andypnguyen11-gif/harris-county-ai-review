using HarrisCountyAI.Application.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace HarrisCountyAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// Service registration for validation report persistence. Call from the
/// composition root alongside the other infrastructure registrations:
/// <c>services.AddValidationReports();</c>.
/// </summary>
public static class ValidationReportServiceExtensions
{
    public static IServiceCollection AddValidationReports(this IServiceCollection services)
    {
        services.AddScoped<IValidationReportRepository, ValidationReportRepository>();

        return services;
    }
}
