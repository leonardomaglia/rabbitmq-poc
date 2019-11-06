using RabbitMq.Poc.Application.EventsHandlers;
using Microsoft.Extensions.DependencyInjection;

namespace RabbitMq.Poc.Infra.CC.Ioc.Modules
{
    public class ApplicationModule
    {
        public static void RegisterServices(IServiceCollection services)
        {
            services.AddScoped<RespostaWS01EventHandle>();
        }
    }
}
