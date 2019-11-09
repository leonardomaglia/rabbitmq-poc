using RabbitMq.Poc.Domain.EventsHandlers;
using Microsoft.Extensions.DependencyInjection;

namespace RabbitMq.Poc.Infra.CC.Ioc.Modules
{
    public class DomainModule
    {
        public static void RegisterServices(IServiceCollection services)
        {
            services.AddScoped<TestMessageEventHandle>();
        }
    }
}
