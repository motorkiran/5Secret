using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SecretManager.Shared.Contracts.Configuration;

namespace SecretManager.Infrastructure.Hosting;

public static class ServiceDefaultsExtensions
{
    public static IHostApplicationBuilder AddSecretManagerServiceDefaults(
        this IHostApplicationBuilder builder,
        string serviceName,
        bool isWeb = false)
    {
        builder.Services
            .AddOptions<RedisOptions>()
            .Bind(builder.Configuration.GetSection(RedisOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Configuration),
                $"{RedisOptions.SectionName}.Configuration is required.")
            .ValidateOnStart();

        builder.Services
            .AddOptions<TelemetryOptions>()
            .Bind(builder.Configuration.GetSection(TelemetryOptions.SectionName));

        var telemetryOptions = GetTelemetryOptions(builder.Configuration);
        var serviceVersion = GetServiceVersion();

        builder.Logging.ClearProviders();
        builder.Logging.Configure(options =>
        {
            options.ActivityTrackingOptions =
                ActivityTrackingOptions.SpanId |
                ActivityTrackingOptions.TraceId |
                ActivityTrackingOptions.ParentId;
        });
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.TimestampFormat = "O";
            options.UseUtcTimestamp = true;
        });
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.ParseStateValues = true;

            ConfigureLogExporters(logging, telemetryOptions);
        });

        builder.Services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName, serviceVersion: serviceVersion))
            .WithTracing(tracing =>
            {
                tracing.AddHttpClientInstrumentation();

                if (isWeb)
                {
                    tracing.AddAspNetCoreInstrumentation();
                }

                ConfigureTraceExporters(tracing, telemetryOptions);
            })
            .WithMetrics(metrics =>
            {
                metrics.AddRuntimeInstrumentation();
                metrics.AddHttpClientInstrumentation();

                if (isWeb)
                {
                    metrics.AddAspNetCoreInstrumentation();
                }

                ConfigureMetricExporters(metrics, telemetryOptions);
            });

        return builder;
    }

    private static TelemetryOptions GetTelemetryOptions(IConfiguration configuration)
    {
        var options = new TelemetryOptions();
        configuration.GetSection(TelemetryOptions.SectionName).Bind(options);
        return options;
    }

    private static string GetServiceVersion()
    {
        return Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? "0.1.0";
    }

    private static void ConfigureLogExporters(OpenTelemetryLoggerOptions logging, TelemetryOptions options)
    {
        if (options.EnableConsoleExporter)
        {
            logging.AddConsoleExporter();
        }

        if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint))
        {
            logging.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(options.OtlpEndpoint));
        }
    }

    private static void ConfigureTraceExporters(TracerProviderBuilder tracing, TelemetryOptions options)
    {
        if (options.EnableConsoleExporter)
        {
            tracing.AddConsoleExporter();
        }

        if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint))
        {
            tracing.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(options.OtlpEndpoint));
        }
    }

    private static void ConfigureMetricExporters(MeterProviderBuilder metrics, TelemetryOptions options)
    {
        if (options.EnableConsoleExporter)
        {
            metrics.AddConsoleExporter();
        }

        if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint))
        {
            metrics.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(options.OtlpEndpoint));
        }
    }
}