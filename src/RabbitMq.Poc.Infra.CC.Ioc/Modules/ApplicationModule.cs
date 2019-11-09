using Microsoft.Extensions.DependencyInjection;
using RabbitMq.Poc.Application;

namespace RabbitMq.Poc.Infra.CC.Ioc.Modules
{
    public class ApplicationModule
    {
        public static void RegisterServices(IServiceCollection services)
        {
            services.AddScoped<EventPublisher>();
        }
    }
}
