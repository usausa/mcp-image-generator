namespace ImageGenerator.McpServer;

using ImageGenerator.McpServer.Errors;

internal static partial class Log
{
    // Startup

    [LoggerMessage(Level = LogLevel.Information, Message = "Service start.")]
    public static partial void InfoServiceStart(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Runtime: os=[{osDescription}], framework=[{frameworkDescription}], rid=[{runtimeIdentifier}]")]
    public static partial void InfoServiceSettingsRuntime(this ILogger logger, string osDescription, string frameworkDescription, string runtimeIdentifier);

    [LoggerMessage(Level = LogLevel.Information, Message = "Environment: version=[{version}], directory=[{directory}]")]
    public static partial void InfoServiceSettingsEnvironment(this ILogger logger, Version? version, string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "GCSettings: serverGC=[{isServerGC}], latencyMode=[{latencyMode}], largeObjectHeapCompactionMode=[{largeObjectHeapCompactionMode}]")]
    public static partial void InfoServiceSettingsGC(this ILogger logger, bool isServerGC, GCLatencyMode latencyMode, GCLargeObjectHeapCompactionMode largeObjectHeapCompactionMode);

    [LoggerMessage(Level = LogLevel.Information, Message = "ThreadPool: workerThreads=[{workerThreads}], completionPortThreads=[{completionPortThreads}]")]
    public static partial void InfoServiceSettingsThreadPool(this ILogger logger, int workerThreads, int completionPortThreads);

    [LoggerMessage(Level = LogLevel.Information, Message = "Telemetry: otelEndPoint=[{otelEndPoint}], prometheusUri=[{prometheusUri}]")]
    public static partial void InfoServiceSettingsTelemetry(this ILogger logger, string otelEndPoint, string prometheusUri);

    [LoggerMessage(Level = LogLevel.Information, Message = "ImageGenerator: endpoint=[{endpoint}], deployment=[{deploymentName}], apiVersion=[{apiVersion}], apiKey=[{apiKey}], outputPath=[{outputPath}], maxRetries=[{maxRetries}], requestTimeoutMinutes=[{requestTimeoutMinutes}], maxConcurrency=[{maxConcurrency}], maxCount=[{maxCount}], retentionDays=[{retentionDays}]")]
    public static partial void InfoServiceSettingsImageGenerator(this ILogger logger, string endpoint, string deploymentName, string apiVersion, string apiKey, string outputPath, int maxRetries, int requestTimeoutMinutes, int maxConcurrency, int maxCount, int retentionDays);

    // Tool

    [LoggerMessage(Level = LogLevel.Information, Message = "Tool started. tool=[{tool}]")]
    public static partial void InfoToolStarted(this ILogger logger, string tool);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tool completed. tool=[{tool}], images=[{images}], elapsed=[{elapsed}]")]
    public static partial void InfoToolCompleted(this ILogger logger, string tool, int images, TimeSpan elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tool cancelled. tool=[{tool}]")]
    public static partial void InfoToolCancelled(this ILogger logger, string tool);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tool failed. tool=[{tool}], code=[{code}], message=[{message}]")]
    public static partial void WarnToolFailed(this ILogger logger, string tool, AppErrorCode code, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "Tool failed. tool=[{tool}]")]
    public static partial void ErrorToolFailed(this ILogger logger, Exception ex, string tool);

    [LoggerMessage(Level = LogLevel.Information, Message = "Image saved. tool=[{tool}], path=[{path}], width=[{width}], height=[{height}], bytes=[{bytes}]")]
    public static partial void InfoImageSaved(this ILogger logger, string tool, string path, int width, int height, long bytes);

    // Generation

    [LoggerMessage(Level = LogLevel.Information, Message = "Generation started. tool=[{tool}], size=[{size}], quality=[{quality}], background=[{background}], format=[{format}], references=[{references}], mask=[{mask}]")]
    public static partial void InfoGenerationStarted(this ILogger logger, string tool, string size, string quality, string background, string format, int references, bool mask);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generation completed. tool=[{tool}], bytes=[{bytes}], elapsed=[{elapsed}], inputTokens=[{inputTokens}], outputTokens=[{outputTokens}]")]
    public static partial void InfoGenerationCompleted(this ILogger logger, string tool, long bytes, TimeSpan elapsed, long? inputTokens, long? outputTokens);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Generation request failed. status=[{status}], body=[{body}]")]
    public static partial void WarnGenerationRequestFailed(this ILogger logger, int status, string body);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Generation retry. delay=[{delay}], attempt=[{attempt}], max=[{max}], message=[{message}]")]
    public static partial void WarnGenerationRetry(this ILogger logger, double delay, int attempt, int max, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Generation retry by timeout. delay=[{delay}], attempt=[{attempt}], max=[{max}]")]
    public static partial void WarnGenerationRetryTimeout(this ILogger logger, double delay, int attempt, int max);

    // Retention

    [LoggerMessage(Level = LogLevel.Information, Message = "File retention completed. deleted=[{deleted}], retentionDays=[{retentionDays}]")]
    public static partial void InfoRetentionCompleted(this ILogger logger, int deleted, int retentionDays);

    [LoggerMessage(Level = LogLevel.Warning, Message = "File retention delete failed. path=[{path}], message=[{message}]")]
    public static partial void WarnRetentionDeleteFailed(this ILogger logger, string path, string message);
}
