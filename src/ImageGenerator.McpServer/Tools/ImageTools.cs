namespace ImageGenerator.McpServer.Tools;

using System.Diagnostics;

using ImageGenerator.McpServer.Errors;
using ImageGenerator.McpServer.Services;
using ImageGenerator.McpServer.Telemetry;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class ImageTools
{
    private readonly ImagePathService paths;

    private readonly ApplicationInstrument instrument;

    private readonly ILogger<ImageTools> logger;

    public ImageTools(
        ImagePathService paths,
        ApplicationInstrument instrument,
        ILogger<ImageTools> logger)
    {
        this.paths = paths;
        this.instrument = instrument;
        this.logger = logger;
    }

    [McpServerTool(Name = ToolNames.GetImageInfo, Title = "Get image info", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Returns the width, height, format, alpha channel presence and file size of an image file (png, jpg or webp) on the server machine.")]
    public async Task<CallToolResult> GetImageInfoAsync(
        [Description("Image file path. Relative paths resolve under the server output directory.")] string input,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var status = "success";
        using var activity = instrument.ActivitySource.StartActivity(ToolNames.GetImageInfo);

        try
        {
            var path = paths.ResolveInputPath(input, "input");
            var data = await File.ReadAllBytesAsync(path, cancellationToken);
            var info = ImageProcessingService.GetInfo(data);

            return ToolResults.Success(new ImageInfoResult(path, info.Width, info.Height, info.Format, info.HasAlpha, info.Bytes));
        }
        catch (AppException ex)
        {
            status = "error";
            logger.WarnToolFailed(ToolNames.GetImageInfo, ex.Code, ex.Message);
            return ToolResults.Error(ex.Message);
        }
        catch (IOException ex)
        {
            status = "error";
            logger.ErrorToolFailed(ex, ToolNames.GetImageInfo);
            return ToolResults.Error($"File operation failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            status = "error";
            logger.ErrorToolFailed(ex, ToolNames.GetImageInfo);
            return ToolResults.Error($"Access denied: {ex.Message}");
        }
        finally
        {
            instrument.RecordToolCall(ToolNames.GetImageInfo, status, sw.Elapsed);
        }
    }

    private sealed record ImageInfoResult(string Path, int Width, int Height, string Format, bool HasAlpha, long Bytes);
}
