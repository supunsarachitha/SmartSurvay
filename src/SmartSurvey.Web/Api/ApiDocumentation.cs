using Microsoft.OpenApi;

namespace SmartSurvey.Web.Api;

/// <summary>Swagger / OpenAPI configuration.</summary>
public static class ApiDocumentation
{
    /// <summary>Registers Swashbuckle with bearer-token security and XML comments.</summary>
    public static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "SmartSurvey API",
                Version = "v1",
                Description = "REST API for surveys, responses, reports and administration. "
                    + "Authenticate with POST /api/auth/login and send the access token as 'Authorization: Bearer {token}'.",
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                Description = "Access token returned by POST /api/auth/login.",
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
            });

            foreach (var xml in new[] { "SmartSurvey.Web.xml", "SmartSurvey.Application.xml", "SmartSurvey.Domain.xml" })
            {
                var path = Path.Combine(AppContext.BaseDirectory, xml);
                if (File.Exists(path))
                {
                    options.IncludeXmlComments(path);
                }
            }

            // DTO names are unique across namespaces except nested/generic types.
            options.CustomSchemaIds(type => type.FullName?.Replace('+', '.') ?? type.Name);
        });

        return services;
    }
}
