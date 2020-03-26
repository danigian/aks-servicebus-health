using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceBusSubscriptionProcessor.Configurations;
using ServiceBusSubscriptionProcessor.HealthChecks;
using ServiceBusSubscriptionProcessor.Processor;
using ServiceBusSubscriptionProcessor.Processor.Interfaces;

namespace ServiceBusSubscriptionProcessor
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<ServiceBusConfiguration>(Configuration.GetSection("ServiceBusConfiguration"));

            services.AddSingleton<IMonitor, SubscriptionMonitor>();
            services.AddSingleton<SubscriptionProcessor>();
            
            services.AddHealthChecks()
                .AddCheck<SubscriptionHealthCheck>("service_bus_health_check", HealthStatus.Degraded, new[] { "liveness" });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHealthChecks("/health/liveness", new HealthCheckOptions
                {
                    Predicate = check => check.Tags.Contains("liveness")
                });
            });
        }
    }
}
