namespace ImageGenerator.McpServer;

using System.Runtime.InteropServices;

using ImageGenerator.McpServer.Services;
using ImageGenerator.McpServer.Telemetry;
using ImageGenerator.McpServer.Tools;
using ImageGenerator.McpServer.Workers;

using ModelContextProtocol.Protocol;

using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using Serilog;

public static class ApplicationExtensions
{
    private const string HealthEndpointPath = "/health";
    private const string McpEndpointPath = "/mcp";

    // MCP SDKが公開する診断ソース (ActivitySource / Meter)
    private const string McpDiagnosticsSourceName = "Experimental.ModelContextProtocol";

    private const string ServerName = "image-generator";

    private const string ServerInstructions =
        "Image asset generator for application development, backed by the Microsoft Foundry image model (gpt-image) with SkiaSharp post-processing. " +
        "The model renders 1024x1024, 1024x1536 or 1536x1024 images; pass width/height to generate_image or edit_image to get the final asset size (the server picks the closest aspect ratio, crops and resizes). " +
        "Generation takes 30 seconds to several minutes per image. " +
        "Input images and output paths are file paths on the server machine; use absolute paths for project assets and overwrite=true to replace existing files. " +
        "Results are saved to disk and the response contains the saved paths, image sizes and token usage.";

    //--------------------------------------------------------------------------------
    // System
    //--------------------------------------------------------------------------------

    public static IHostApplicationBuilder ConfigureSystem(this WebApplicationBuilder builder)
    {
        // Path
        builder.Configuration.SetBasePath(AppContext.BaseDirectory);

        return builder;
    }

    //--------------------------------------------------------------------------------
    // Host
    //--------------------------------------------------------------------------------

    public static IHostApplicationBuilder ConfigureHost(this WebApplicationBuilder builder)
    {
        // Service
        builder.Services
            .AddWindowsService()
            .AddSystemd();

        return builder;
    }

    //--------------------------------------------------------------------------------
    // Logging
    //--------------------------------------------------------------------------------

    public static IHostApplicationBuilder ConfigureLogging(this IHostApplicationBuilder builder)
    {
        var useOtlpExporter = builder.Configuration.IsOtelExporterEnabled();

        // Application log
        builder.Logging.ClearProviders();
        builder.Services.AddSerilog(
            options =>
            {
                options.ReadFrom.Configuration(builder.Configuration);
            },
            writeToProviders: useOtlpExporter);

        return builder;
    }

    //--------------------------------------------------------------------------------
    // Health
    //--------------------------------------------------------------------------------

    public static IHostApplicationBuilder ConfigureHealth(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks();

        return builder;
    }

    //--------------------------------------------------------------------------------
    // Telemetry
    //--------------------------------------------------------------------------------

    public static IHostApplicationBuilder ConfigureTelemetry(this IHostApplicationBuilder builder)
    {
        var useOtlpExporter = builder.Configuration.IsOtelExporterEnabled();

        var prometheusSection = builder.Configuration.GetSection("Prometheus");
        var prometheusUri = prometheusSection.GetValue<string>("Uri")!;
        var usePrometheusExporter = !String.IsNullOrEmpty(prometheusUri);

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(config =>
            {
                config.AddService(
                    serviceName: builder.Environment.ApplicationName,
                    serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString(),
                    serviceInstanceId: Environment.MachineName);
            });

        // Log
        if (useOtlpExporter)
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
            });
            builder.Services.Configure<OpenTelemetryLoggerOptions>(static logging =>
            {
                logging.AddOtlpExporter();
            });
        }

        // Metrics
        if (useOtlpExporter || usePrometheusExporter)
        {
            telemetry
                .WithMetrics(metrics =>
                {
                    metrics
                        .AddRuntimeInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddAspNetCoreInstrumentation()
                        .AddMeter(McpDiagnosticsSourceName)
                        .AddApplicationInstrumentation();

                    if (useOtlpExporter)
                    {
                        metrics.AddOtlpExporter();
                    }

                    if (usePrometheusExporter)
                    {
                        var prometheusEndpoint = new Uri(prometheusUri);
                        metrics.AddPrometheusHttpListener(config =>
                        {
                            config.Host = prometheusEndpoint.Host;
                            config.Port = prometheusEndpoint.Port;
                        });
                    }
                });
        }

        // Trace
        if (useOtlpExporter)
        {
            telemetry
                .WithTracing(tracing =>
                {
                    tracing
                        .AddAspNetCoreInstrumentation(static options =>
                        {
                            options.Filter = static context =>
                                !context.Request.Path.StartsWithSegments(HealthEndpointPath, StringComparison.OrdinalIgnoreCase);
                        })
                        .AddHttpClientInstrumentation()
                        .AddSource(McpDiagnosticsSourceName)
                        .AddApplicationInstrumentation();

                    tracing.AddOtlpExporter();
                });
        }

        // Custom instrument
        builder.Services.AddApplicationInstrument();

        return builder;
    }

    //--------------------------------------------------------------------------------
    // MCP
    //--------------------------------------------------------------------------------

    public static IHostApplicationBuilder ConfigureMcp(this IHostApplicationBuilder builder)
    {
        builder.Services
            .AddMcpServer(static options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = ServerName,
                    Title = "Image Generator",
                    Version = Source.Version
                };
                options.ServerInstructions = ServerInstructions;
            })
            .WithHttpTransport()
            .WithTools<GenerationTools>()
            .WithTools<ImageTools>();

        return builder;
    }

    //--------------------------------------------------------------------------------
    // Components
    //--------------------------------------------------------------------------------

    public static IHostApplicationBuilder ConfigureComponents(this IHostApplicationBuilder builder)
    {
        // System
        builder.Services.AddSingleton(TimeProvider.System);

        // Setting
        builder.Services.AddOptions<ImageGeneratorSetting>().BindConfiguration("ImageGenerator").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(static p => p.GetRequiredService<IOptions<ImageGeneratorSetting>>().Value);
        builder.Services.AddOptions<ImageProcessingSetting>().BindConfiguration("ImageProcessing").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(static p => p.GetRequiredService<IOptions<ImageProcessingSetting>>().Value);

        // Foundry
        builder.Services.AddHttpClient(ImageGenerationService.HttpClientName, static client =>
        {
            // タイムアウトはリクエストごとにCancellationTokenで制御する
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        // Service
        builder.Services.AddSingleton<ImageGenerationService>();
        builder.Services.AddSingleton<ImageProcessingService>();
        builder.Services.AddSingleton<ImagePathService>();

        // Worker
        builder.Services.AddHostedService<FileRetentionWorker>();

        return builder;
    }

    //--------------------------------------------------------------------------------
    // Information
    //--------------------------------------------------------------------------------

    public static void LogStartupInformation(this WebApplication app)
    {
        ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);

        var prometheusSection = app.Configuration.GetSection("Prometheus");
        var prometheusUri = prometheusSection.GetValue("Uri", string.Empty);

        var setting = app.Services.GetRequiredService<ImageGeneratorSetting>();
        var paths = app.Services.GetRequiredService<ImagePathService>();

        app.Logger.InfoServiceStart();
        app.Logger.InfoServiceSettingsRuntime(RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription, RuntimeInformation.RuntimeIdentifier);
        app.Logger.InfoServiceSettingsEnvironment(typeof(Program).Assembly.GetName().Version, Environment.CurrentDirectory);
        app.Logger.InfoServiceSettingsGC(GCSettings.IsServerGC, GCSettings.LatencyMode, GCSettings.LargeObjectHeapCompactionMode);
        app.Logger.InfoServiceSettingsThreadPool(workerThreads, completionPortThreads);
        app.Logger.InfoServiceSettingsTelemetry(app.Configuration.GetOtelExporterEndpoint(), prometheusUri);
        app.Logger.InfoServiceSettingsImageGenerator(
            setting.Endpoint,
            setting.DeploymentName,
            setting.ApiVersion,
            String.IsNullOrEmpty(setting.ApiKey) ? "(not set)" : "****",
            paths.OutputRoot,
            setting.MaxRetries,
            setting.RequestTimeoutMinutes,
            setting.MaxConcurrency,
            setting.MaxCount,
            setting.RetentionDays);
    }

    //--------------------------------------------------------------------------------
    // End point
    //--------------------------------------------------------------------------------

    public static WebApplication MapEndpoints(this WebApplication app)
    {
        // MCP
        app.MapMcp(McpEndpointPath);

        // Health
        app.MapHealthChecks(HealthEndpointPath);

        return app;
    }

    //--------------------------------------------------------------------------------
    // Startup
    //--------------------------------------------------------------------------------

    public static ValueTask InitializeApplicationAsync(this WebApplication app)
    {
        // Prepare instrument
        app.Services.GetRequiredService<ApplicationInstrument>();

        // Prepare output directory
        Directory.CreateDirectory(app.Services.GetRequiredService<ImagePathService>().OutputRoot);

        return ValueTask.CompletedTask;
    }

    //--------------------------------------------------------------------------------
    // Configuration
    //--------------------------------------------------------------------------------

    private static bool IsOtelExporterEnabled(this IConfiguration configuration) =>
        !String.IsNullOrWhiteSpace(configuration.GetOtelExporterEndpoint());

    private static string GetOtelExporterEndpoint(this IConfiguration configuration) =>
        configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? string.Empty;
}
