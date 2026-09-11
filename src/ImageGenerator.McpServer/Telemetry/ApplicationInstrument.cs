namespace ImageGenerator.McpServer.Telemetry;

using System.Diagnostics;
using System.Diagnostics.Metrics;

public sealed class ApplicationInstrument : IDisposable
{
    private readonly Meter meter;

    public ActivitySource ActivitySource { get; }

    private readonly Counter<long> toolRequests;

    private readonly Histogram<double> toolDuration;

    private readonly Counter<long> generatedImages;

    private readonly Counter<long> generationTokens;

    private readonly Counter<long> generationRetries;

    public ApplicationInstrument(IMeterFactory meterFactory)
    {
        ActivitySource = new ActivitySource(Source.Name, Source.Version);
        meter = meterFactory.Create(Source.Name, Source.Version);

        meter.CreateObservableCounter("application.uptime", ObserveApplicationUptime, unit: "s", description: "Application uptime.");

        toolRequests = meter.CreateCounter<long>("mcp.tool.requests", unit: "{request}", description: "Number of tool calls.");
        toolDuration = meter.CreateHistogram<double>("mcp.tool.duration", unit: "s", description: "Duration of tool calls.");
        generatedImages = meter.CreateCounter<long>("image.generation.images", unit: "{image}", description: "Number of generated images.");
        generationTokens = meter.CreateCounter<long>("image.generation.tokens", unit: "{token}", description: "Tokens consumed by image generation.");
        generationRetries = meter.CreateCounter<long>("image.generation.retries", unit: "{retry}", description: "Number of image generation retries.");
    }

    public void Dispose()
    {
        meter.Dispose();
    }

    private static long ObserveApplicationUptime() =>
        (long)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds;

    // ツール呼び出し単位の回数と処理時間。statusはsuccess/error/cancelled
    public void RecordToolCall(string tool, string status, TimeSpan duration)
    {
        toolRequests.Add(1, new("tool", tool), new("status", status));
        toolDuration.Record(duration.TotalSeconds, new("tool", tool), new("status", status));
    }

    public void AddGeneratedImages(string tool, int count) =>
        generatedImages.Add(count, new KeyValuePair<string, object?>("tool", tool));

    // Foundry応答のusageから集計する。typeはinput/output等
    public void AddGenerationTokens(string tool, string type, long tokens) =>
        generationTokens.Add(tokens, new("tool", tool), new("type", type));

    public void IncrementGenerationRetry(string tool, int statusCode) =>
        generationRetries.Add(1, new("tool", tool), new("status_code", statusCode));
}
