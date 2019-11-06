using System;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using RabbitMq.Poc.Api.Configurations;
using RabbitMq.Poc.Application.Events;
using RabbitMq.Poc.Infra.CC.EventBus;
using RabbitMq.Poc.Infra.CC.EventBus.Interfaces;
using RabbitMq.Poc.Infra.CC.Ioc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using IHostingEnvironment = Microsoft.AspNetCore.Hosting.IHostingEnvironment;
using RabbitMq.Poc.Application.EventsHandlers;

namespace RabbitMq.Poc.Api
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            services
                .RegisterEventBus(Configuration)
                .AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_1);

            NativeInjectorBootStrapper.RegisterServices(services);

            return services.BuildCustomDependencyInjectionContainer();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            ServiceConfiguration.Configure(Configuration);

            if (env.IsDevelopment())
                app.UseDeveloperExceptionPage();

            app.UseMvc();

            ConfigureEventBus(app);
        }

        private void ConfigureEventBus(IApplicationBuilder app)
        {
            var eventBus = app.ApplicationServices.GetRequiredService<IEventBus>();

            eventBus
                .Subscribe<RespostaWS01Event, RespostaWS01EventHandle>();
        }
    }

    internal static class CustomStartupExtensionsMethods
    {
        public static IServiceCollection RegisterEventBus(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton<IEventBusPersistentConnection>(sp =>
            {
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(configuration["CloudAMQP:Uri"].Replace("amqp://", "amqps://"))
                };

                return new EventBusPersistentConnection(factory,
                    int.TryParse(configuration["CloudAMQP:BusRetryCount"], out var retryCount) ? retryCount : 5);
            });

            services.AddSingleton<IEventBus, EventBus>(sp =>
            {
                var persistentConnection = sp.GetRequiredService<IEventBusPersistentConnection>();
                var iLifetimeScope = sp.GetRequiredService<ILifetimeScope>();
                //var logger = sp.GetRequiredService<ILogService>();

                return new EventBus(
                    persistentConnection,
                    iLifetimeScope,
                    //logger,
                    configuration["CloudAMQP:ServiceName"],
                    configuration["CloudAMQP:Environment"],
                    int.TryParse(configuration["BusRetryCount"], out var retryCount) ? retryCount : 5);
            });

            return services;
        }

        public static IServiceProvider BuildCustomDependencyInjectionContainer(this IServiceCollection services)
        {
            var container = new ContainerBuilder();
            container.Populate(services);
            return new AutofacServiceProvider(container.Build());
        }
    }
}