namespace ImageGenerator.McpServer.Tools;

using System.Diagnostics;

using ImageGenerator.McpServer.Errors;
using ImageGenerator.McpServer.Services;
using ImageGenerator.McpServer.Telemetry;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using SkiaSharp;

[McpServerToolType]
public sealed class ImageTools
{
    private readonly ImageProcessingService processing;

    private readonly ImagePathService paths;

    private readonly ImageProcessingSetting processingSetting;

    private readonly ApplicationInstrument instrument;

    private readonly ILogger<ImageTools> logger;

    public ImageTools(
        ImageProcessingService processing,
        ImagePathService paths,
        ImageProcessingSetting processingSetting,
        ApplicationInstrument instrument,
        ILogger<ImageTools> logger)
    {
        this.processing = processing;
        this.paths = paths;
        this.processingSetting = processingSetting;
        this.instrument = instrument;
        this.logger = logger;
    }

    //--------------------------------------------------------------------------------
    // Info
    //--------------------------------------------------------------------------------

    [McpServerTool(Name = ToolNames.GetImageInfo, Title = "Get image info", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Returns the width, height, format, alpha channel presence and file size of an image file (png, jpg or webp) on the server machine.")]
    public async Task<CallToolResult> GetImageInfoAsync(
        [Description(ImageParameters.InputDescription)] string input,
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

    //--------------------------------------------------------------------------------
    // Resize
    //--------------------------------------------------------------------------------

    [McpServerTool(Name = ToolNames.ResizeImage, Title = "Resize image", Destructive = false, OpenWorld = false)]
    [Description("Resizes an image file to a target size or scale factor and saves the result. Use fit to control how the aspect ratio is handled.")]
    public Task<CallToolResult> ResizeImageAsync(
        [Description(ImageParameters.InputDescription)] string input,
        CancellationToken cancellationToken,
        [Description("Target width in pixels. Omit to derive it from height while keeping the aspect ratio.")] int? width = null,
        [Description("Target height in pixels. Omit to derive it from width while keeping the aspect ratio.")] int? height = null,
        [Description("Scale factor such as 0.5 or 2. Cannot be combined with width/height.")] double? scale = null,
        [Description("How to fit into width x height: cover (center crop to the target aspect ratio, default), contain (fit inside; the result can be smaller than the target), pad (fit inside and pad with background) or stretch (ignore the aspect ratio).")] string? fit = null,
        [Description("Background color used by pad. " + ImageParameters.ColorDescription + " Default: transparent for png/webp, white for jpeg.")] string? background = null,
        [Description(ImageParameters.OutputFormatDescription)] string? outputFormat = null,
        [Description(ImageParameters.QualityDescription)] int? quality = null,
        [Description(ImageParameters.OutputPathDescription)] string? outputPath = null,
        [Description(ImageParameters.OverwriteDescription)] bool overwrite = false,
        [Description(ImageParameters.IncludeImageDescription)] bool includeImage = false)
    {
        var arguments = new ProcessingArguments(input, outputPath, overwrite, outputFormat, quality, includeImage);
        return ExecuteAsync(ToolNames.ResizeImage, "resize", arguments, context =>
        {
            ImageParameters.ValidateRange(width, 1, processingSetting.MaxDimension, "width");
            ImageParameters.ValidateRange(height, 1, processingSetting.MaxDimension, "height");
            var fitMode = ImageParameters.ParseFit(fit, allowStretch: true);
            var color = ImageParameters.ParseColor(background, "background");
            return processing.Resize(context.Data, width, height, scale, fitMode, color, context.Format, context.Quality);
        }, cancellationToken);
    }

    //--------------------------------------------------------------------------------
    // Crop
    //--------------------------------------------------------------------------------

    [McpServerTool(Name = ToolNames.CropImage, Title = "Crop image", Destructive = false, OpenWorld = false)]
    [Description("Crops an image file either by a rectangle (x, y, width, height) or by an aspect ratio with an anchor (the largest region of that ratio), and saves the result without resizing.")]
    public Task<CallToolResult> CropImageAsync(
        [Description(ImageParameters.InputDescription)] string input,
        CancellationToken cancellationToken,
        [Description("Left edge of the crop rectangle in pixels. Default 0.")] int? x = null,
        [Description("Top edge of the crop rectangle in pixels. Default 0.")] int? y = null,
        [Description("Width of the crop rectangle in pixels. Default: the remaining width from x.")] int? width = null,
        [Description("Height of the crop rectangle in pixels. Default: the remaining height from y.")] int? height = null,
        [Description("Aspect ratio such as 16:9, 1:1 or 2:1. When given, the largest region with that ratio is cropped and x/y/width/height are ignored.")] string? aspect = null,
        [Description("Position of the aspect crop: center (default), top, bottom, left, right, top-left, top-right, bottom-left or bottom-right.")] string? anchor = null,
        [Description(ImageParameters.OutputFormatDescription)] string? outputFormat = null,
        [Description(ImageParameters.QualityDescription)] int? quality = null,
        [Description(ImageParameters.OutputPathDescription)] string? outputPath = null,
        [Description(ImageParameters.OverwriteDescription)] bool overwrite = false,
        [Description(ImageParameters.IncludeImageDescription)] bool includeImage = false)
    {
        var arguments = new ProcessingArguments(input, outputPath, overwrite, outputFormat, quality, includeImage);
        return ExecuteAsync(ToolNames.CropImage, "crop", arguments, context =>
        {
            SKRectI rect;
            if (!String.IsNullOrWhiteSpace(aspect))
            {
                rect = ImageProcessingService.ComputeAspectRect(context.Info.Width, context.Info.Height, ImageParameters.ParseAspect(aspect), ImageParameters.ParseAnchor(anchor));
            }
            else
            {
                ImageParameters.ValidateRange(x, 0, context.Info.Width - 1, "x");
                ImageParameters.ValidateRange(y, 0, context.Info.Height - 1, "y");
                var left = x ?? 0;
                var top = y ?? 0;
                ImageParameters.ValidateRange(width, 1, context.Info.Width - left, "width");
                ImageParameters.ValidateRange(height, 1, context.Info.Height - top, "height");
                rect = SKRectI.Create(left, top, width ?? (context.Info.Width - left), height ?? (context.Info.Height - top));
            }

            return processing.Crop(context.Data, rect, context.Format, context.Quality);
        }, cancellationToken);
    }

    //--------------------------------------------------------------------------------
    // Trim
    //--------------------------------------------------------------------------------

    [McpServerTool(Name = ToolNames.TrimImage, Title = "Trim image", Destructive = false, OpenWorld = false)]
    [Description("Removes transparent or solid-color margins around the content of an image file (auto-detected from the corners unless color is given), optionally adds padding, and saves the result.")]
    public Task<CallToolResult> TrimImageAsync(
        [Description(ImageParameters.InputDescription)] string input,
        CancellationToken cancellationToken,
        [Description("Margin color to trim. " + ImageParameters.ColorDescription + " Default: transparent if a corner is transparent, otherwise the corner color.")] string? color = null,
        [Description("Color tolerance 0-255 for margin detection. Default 10.")] int tolerance = 10,
        [Description("Margin in pixels to add around the trimmed content, filled with the margin color. Default 0.")] int padding = 0,
        [Description(ImageParameters.OutputFormatDescription)] string? outputFormat = null,
        [Description(ImageParameters.QualityDescription)] int? quality = null,
        [Description(ImageParameters.OutputPathDescription)] string? outputPath = null,
        [Description(ImageParameters.OverwriteDescription)] bool overwrite = false,
        [Description(ImageParameters.IncludeImageDescription)] bool includeImage = false)
    {
        var arguments = new ProcessingArguments(input, outputPath, overwrite, outputFormat, quality, includeImage);
        return ExecuteAsync(ToolNames.TrimImage, "trim", arguments, context =>
        {
            ImageParameters.ValidateRange(tolerance, 0, 255, "tolerance");
            ImageParameters.ValidateRange(padding, 0, processingSetting.MaxDimension, "padding");
            var marginColor = ImageParameters.ParseColor(color, "color");
            return processing.Trim(context.Data, marginColor, tolerance, padding, context.Format, context.Quality);
        }, cancellationToken);
    }

    //--------------------------------------------------------------------------------
    // Convert
    //--------------------------------------------------------------------------------

    [McpServerTool(Name = ToolNames.ConvertImage, Title = "Convert image", Destructive = false, OpenWorld = false)]
    [Description("Converts an image file to png, jpeg or webp (re-encoding with the given quality) and saves the result. Transparent areas become white in jpeg.")]
    public Task<CallToolResult> ConvertImageAsync(
        [Description(ImageParameters.InputDescription)] string input,
        CancellationToken cancellationToken,
        [Description("Output format: png, jpeg or webp. Default: the extension of outputPath. One of the two must be given.")] string? outputFormat = null,
        [Description(ImageParameters.QualityDescription)] int? quality = null,
        [Description(ImageParameters.OutputPathDescription)] string? outputPath = null,
        [Description(ImageParameters.OverwriteDescription)] bool overwrite = false,
        [Description(ImageParameters.IncludeImageDescription)] bool includeImage = false)
    {
        var arguments = new ProcessingArguments(input, outputPath, overwrite, outputFormat, quality, includeImage, RequireFormat: true);
        return ExecuteAsync(ToolNames.ConvertImage, "convert", arguments, context => processing.ConvertFormat(context.Data, context.Format, context.Quality), cancellationToken);
    }

    //--------------------------------------------------------------------------------
    // Transparent
    //--------------------------------------------------------------------------------

    [McpServerTool(Name = ToolNames.MakeTransparent, Title = "Make transparent", Destructive = false, OpenWorld = false)]
    [Description("Makes a background color of an image file transparent (auto-detected from the corners unless color is given) and saves the result as png or webp. Useful for icons and sprites rendered on a solid background.")]
    public Task<CallToolResult> MakeTransparentAsync(
        [Description(ImageParameters.InputDescription)] string input,
        CancellationToken cancellationToken,
        [Description("Background color to remove. " + ImageParameters.ColorDescription + " Default: the most common corner color.")] string? color = null,
        [Description("Color tolerance 0-255. Pixels within the tolerance become fully transparent. Default 10.")] int tolerance = 10,
        [Description("Additional range 0-255 above the tolerance where pixels become partially transparent for softer edges. Default 0.")] int feather = 0,
        [Description("Output format: png (default) or webp.")] string? outputFormat = null,
        [Description(ImageParameters.QualityDescription)] int? quality = null,
        [Description(ImageParameters.OutputPathDescription)] string? outputPath = null,
        [Description(ImageParameters.OverwriteDescription)] bool overwrite = false,
        [Description(ImageParameters.IncludeImageDescription)] bool includeImage = false)
    {
        var arguments = new ProcessingArguments(input, outputPath, overwrite, outputFormat, quality, includeImage, RequireTransparency: true);
        return ExecuteAsync(ToolNames.MakeTransparent, "transparent", arguments, context =>
        {
            ImageParameters.ValidateRange(tolerance, 0, 255, "tolerance");
            ImageParameters.ValidateRange(feather, 0, 255, "feather");
            var backgroundColor = ImageParameters.ParseColor(color, "color");
            return ImageProcessingService.MakeTransparent(context.Data, backgroundColor, tolerance, feather, context.Format, context.Quality);
        }, cancellationToken);
    }

    //--------------------------------------------------------------------------------
    // Execute
    //--------------------------------------------------------------------------------

    private async Task<CallToolResult> ExecuteAsync(string tool, string prefix, ProcessingArguments arguments, Func<ProcessingContext, byte[]> operation, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var status = "success";
        using var activity = instrument.ActivitySource.StartActivity(tool);

        logger.InfoToolStarted(tool);

        try
        {
            var inputPath = paths.ResolveInputPath(arguments.Input, "input");
            var data = await File.ReadAllBytesAsync(inputPath, cancellationToken);
            var info = ImageProcessingService.GetInfo(data);

            var explicitFormat = ImageParameters.NormalizeFormat(arguments.OutputFormat);
            ImageParameters.ValidateRange(arguments.Quality, 1, 100, "quality");

            // 既定の出力形式は入力と同じ
            var defaultFormat = ImageFormats.Normalize(info.Format) ?? ImageFormats.Png;
            var output = paths.ResolveOutput(arguments.OutputPath, explicitFormat, defaultFormat);
            if (arguments.RequireFormat && (explicitFormat is null) && output.IsDirectory)
            {
                throw new AppException(AppErrorCode.InvalidParameter, "Specify outputFormat or an outputPath with a .png, .jpg or .webp extension.");
            }

            if (arguments.RequireTransparency && !ImageFormats.SupportsTransparency(output.Format))
            {
                throw new AppException(AppErrorCode.InvalidParameter, $"{tool} requires png or webp output.");
            }

            var quality = processing.ResolveQuality(output.Format, arguments.Quality);
            var outputFile = paths.CreateFilePaths(output, prefix, 1, arguments.Overwrite)[0];

            activity?.SetTag("image.format", output.Format);

            var result = operation(new ProcessingContext(data, info, output.Format, quality));
            await ImagePathService.WriteAsync(outputFile, result, arguments.Overwrite, cancellationToken);

            var resultInfo = ImageProcessingService.GetInfo(result);
            logger.InfoImageSaved(tool, outputFile, resultInfo.Width, resultInfo.Height, result.Length);

            sw.Stop();
            logger.InfoToolCompleted(tool, 1, sw.Elapsed);

            var value = new ProcessingToolResult(
                outputFile,
                resultInfo.Width,
                resultInfo.Height,
                output.Format,
                result.Length,
                new SourceInfo(inputPath, info.Width, info.Height, info.Format));
            return ToolResults.Success(value, arguments.IncludeImage ? [ToolResults.Image(result, output.Format)] : null);
        }
        catch (AppException ex)
        {
            status = "error";
            logger.WarnToolFailed(tool, ex.Code, ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return ToolResults.Error(ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = "cancelled";
            logger.InfoToolCancelled(tool);
            throw;
        }
        catch (IOException ex)
        {
            status = "error";
            logger.ErrorToolFailed(ex, tool);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return ToolResults.Error($"File operation failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            status = "error";
            logger.ErrorToolFailed(ex, tool);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return ToolResults.Error($"Access denied: {ex.Message}");
        }
        finally
        {
            instrument.RecordToolCall(tool, status, sw.Elapsed);
        }
    }

    //--------------------------------------------------------------------------------
    // Types
    //--------------------------------------------------------------------------------

    private sealed record ProcessingArguments(
        string Input,
        string? OutputPath,
        bool Overwrite,
        string? OutputFormat,
        int? Quality,
        bool IncludeImage,
        bool RequireFormat = false,
        bool RequireTransparency = false);

    private sealed record ProcessingContext(ReadOnlyMemory<byte> Data, ImageInfo Info, string Format, int Quality);

    private sealed record ImageInfoResult(string Path, int Width, int Height, string Format, bool HasAlpha, long Bytes);

    private sealed record SourceInfo(string Path, int Width, int Height, string Format);

    private sealed record ProcessingToolResult(string Path, int Width, int Height, string Format, long Bytes, SourceInfo Source);
}
