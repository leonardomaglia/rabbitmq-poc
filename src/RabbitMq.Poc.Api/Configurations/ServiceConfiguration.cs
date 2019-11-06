using Microsoft.Extensions.Configuration;

namespace RabbitMq.Poc.Api.Configurations
{
    public class ServiceConfiguration
    {
        public static bool IsRunningLocal { get; set; }

        public static void Configure(IConfiguration configuration)
        {
            SetCurrentEnvironment(configuration);
        }

        private static void SetCurrentEnvironment(IConfiguration configuration) =>
            IsRunningLocal = configuration.GetValue<string>("ASPNETCORE_ENVIRONMENT") == "Local";
    }
}
