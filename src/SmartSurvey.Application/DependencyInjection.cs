using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Dashboard;
using SmartSurvey.Application.Reports;
using SmartSurvey.Application.Reports.Charts;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;

namespace SmartSurvey.Application;

/// <summary>Registers Application-layer services.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds use-case services, validators and the chart renderer. Services are scoped: in Blazor
    /// Server a scope equals a user circuit, so services must stay stateless and obtain database
    /// contexts per operation via <see cref="Common.IAppDbContextFactory"/>.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<ISurveyService, SurveyService>();
        services.AddScoped<IResponseService, ResponseService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IReportEngine, ReportEngine>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddSingleton<ISvgChartRenderer, SvgChartRenderer>();

        return services;
    }
}
