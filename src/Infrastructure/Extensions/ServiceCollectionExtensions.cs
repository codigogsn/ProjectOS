using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProjectOS.Application.Common;
using ProjectOS.Application.Interfaces;
using ProjectOS.Infrastructure.Repositories;
using ProjectOS.Infrastructure.Services;

namespace ProjectOS.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Options
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName));

        services.AddOptions<GmailOptions>()
            .Bind(configuration.GetSection(GmailOptions.SectionName));

        // Repositories
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IContactRepository, ContactRepository>();
        services.AddScoped<IEmailMessageRepository, EmailMessageRepository>();

        // Services
        services.AddScoped<IGmailService, GmailService>();
        services.AddScoped<IEmailIngestionService, EmailIngestionService>();

        return services;
    }
}
