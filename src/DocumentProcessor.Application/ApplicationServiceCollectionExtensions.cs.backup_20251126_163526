using DocumentProcessor.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentProcessor.Application
{
    public static class ApplicationServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            // Register application services
            services.AddScoped<IDocumentProcessingService, DocumentProcessingService>();
            services.AddScoped<IBackgroundDocumentProcessingService, BackgroundDocumentProcessingService>();
            
            // Note: Background task queue and hosted services are registered in InfrastructureServiceCollectionExtensions
            // to avoid duplicate registrations
            
            return services;
        }
    }
}