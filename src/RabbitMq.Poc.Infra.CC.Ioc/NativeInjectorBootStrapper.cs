using RabbitMq.Poc.Infra.CC.Ioc.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace RabbitMq.Poc.Infra.CC.Ioc
{
    public class NativeInjectorBootStrapper
    {
        public static void RegisterServices(IServiceCollection services)
        {
            ApplicationModule.RegisterServices(services);
            DomainModule.RegisterServices(services);
        }
    }
}
