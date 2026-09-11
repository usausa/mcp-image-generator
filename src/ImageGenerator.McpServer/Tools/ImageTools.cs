namespace ImageGenerator.McpServer.Tools;

using System.Diagnostics;

using ImageGenerator.McpServer.Errors;
using ImageGenerator.McpServer.Resources;
using ImageGenerator.McpServer.Services;
using ImageGenerator.McpServer.Telemetry;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using SkiaSharp;

[McpServerToolType]
public sealed class ImageTools
{
    private const int MaxExportSizes = 64;

    private const int MaxListLimit = 1000;

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
    public Task<CallToolResult> GetImageInfoAsync(
        [Description(ImageParameters.InputDescription)] string input,
        CancellationToken cancellationToken) =>
        ExecuteReadOnlyAsync(ToolNames.GetImageInfo, async () =>
        {
            var path = paths.ResolveInputPath(input, "input");
            var data = await File.ReadAllBytesAsync(path, cancellationToken);
            var info = ImageProcessingService.GetInfo(data);
            return ToolResults.Success(new ImageInfoResult(path, info.Width, info.Height, info.Format, info.HasAlpha, info.Bytes));
        });

    [McpServerTool(Name = ToolNames.ListImages, Title = "List images", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists image files (png, jpg, webp) in a directory with their size and dimensions, newest first. Default: the server output directory where generated files are kept.")]
    public Task<CallToolResult> ListImagesAsync(
        CancellationToken cancellationToken,
        [Description("Directory path. Relative paths resolve under the server output directory. Default: the server output directory.")] string? directory = null,
        [Description("Include subdirectories. Default false.")] bool recursive = false,
        [Description("Maximum number of entries (1-1000). Default 100.")] int limit = 100) =>
        ExecuteReadOnlyAsync(ToolNames.ListImages, async () =>
        {
            ImageParameters.ValidateRange(limit, 1, MaxListLimit, "limit");
            var root = paths.ResolveInputDirectory(directory);

            var files = new DirectoryInfo(root)
                .EnumerateFiles("*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(static f => ImagePathService.IsSupportedImageFile(f.Name))
                .OrderByDescending(static f => f.LastWriteTimeUtc)
                .Take(limit);

            var images = new List<ListedImage>();
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var data = await File.ReadAllBytesAsync(file.FullName, cancellationToken);
                var info = ImageProcessingService.GetInfo(data);
                images.Add(new ListedImage(file.Name, file.FullName, info.Width, info.Height, info.Format, file.Length, file.LastWriteTime.ToString("O", CultureInfo.InvariantCulture)));
            }

            return ToolResults.Success(new ListImagesResult(root, images.Count, images));
        });

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
    // Export
    //--------------------------------------------------------------------------------

    [McpServerTool(Name = ToolNames.ExportImageSizes, Title = "Export image sizes", Destructive = false, OpenWorld = false)]
    [Description("Exports one image (typically a 1024x1024 icon) to a set of sizes in a single call, using a platform preset (favicon, pwa, android, ios, windows, scales) or explicit square sizes. Optionally writes a multi-size .ico. Files are written into outputPath (a directory).")]
    public Task<CallToolResult> ExportImageSizesAsync(
        [Description(ImageParameters.InputDescription)] string input,
        CancellationToken cancellationToken,
        [Description("Preset: favicon (16/32/48/180/192/512 + favicon.ico), pwa (72-512), android (mipmap densities + 512), ios (20-1024 AppIcon set), windows (16-256 + .ico) or scales (1x/2x/3x of width x height). Either preset or sizes is required.")] string? preset = null,
        [Description("Explicit square sizes in pixels, e.g. [16, 32, 64, 128]. Output names are {name}-{size}.png.")] int[]? sizes = null,
        [Description("Base width for the scales preset. Default: the source width.")] int? width = null,
        [Description("Base height for the scales preset. Default: the source height.")] int? height = null,
        [Description("Base name used in output file names. Default: the input file name without extension.")] string? name = null,
        [Description("Output directory. Relative paths resolve under the server output directory. Default: the server output directory.")] string? outputPath = null,
        [Description("Also write a multi-size .ico from the sizes up to 256. Default: true for the favicon and windows presets, otherwise false.")] bool? ico = null,
        [Description("How to fit the source into each size: cover (center crop, default), contain, pad or stretch.")] string? fit = null,
        [Description("Background color used by pad. " + ImageParameters.ColorDescription)] string? background = null,
        [Description("Output format for the size files: png (default), jpeg or webp. The .ico always contains png entries.")] string? outputFormat = null,
        [Description(ImageParameters.QualityDescription)] int? quality = null,
        [Description(ImageParameters.OverwriteDescription)] bool overwrite = false)
    {
        return ExecuteReadOnlyAsync(ToolNames.ExportImageSizes, async () =>
        {
            var inputPath = paths.ResolveInputPath(input, "input");
            var data = await File.ReadAllBytesAsync(inputPath, cancellationToken);
            var info = ImageProcessingService.GetInfo(data);

            var format = ImageParameters.NormalizeFormat(outputFormat) ?? ImageFormats.Png;
            ImageParameters.ValidateRange(quality, 1, 100, "quality");
            ImageParameters.ValidateRange(width, 1, processingSetting.MaxDimension, "width");
            ImageParameters.ValidateRange(height, 1, processingSetting.MaxDimension, "height");
            var fitMode = ImageParameters.ParseFit(fit, allowStretch: true);
            var color = ImageParameters.ParseColor(background, "background");
            var baseName = String.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(inputPath) : name.Trim();
            if (baseName != Path.GetFileName(baseName))
            {
                throw new AppException(AppErrorCode.InvalidParameter, "name must be a file name without directory separators.");
            }

            var sizePreset = ResolvePreset(preset, sizes, ico, baseName, ImageFormats.GetExtension(format), width ?? info.Width, height ?? info.Height);

            var directory = paths.ResolveOutputDirectory(outputPath);
            var outputs = sizePreset.Entries.Select(entry => (Entry: entry, Path: Path.Combine(directory, entry.FileName))).ToArray();
            var icoPath = sizePreset.IcoFileName is not null ? Path.Combine(directory, sizePreset.IcoFileName) : null;
            ImagePathService.EnsureWritable(outputs.Select(static x => x.Path).Concat(icoPath is not null ? [icoPath] : []), overwrite);

            var encodeQuality = processing.ResolveQuality(format, quality);
            var images = new List<SavedImage>(outputs.Length);
            var icoEntries = new List<(int Size, byte[] Png)>();
            foreach (var (entry, path) in outputs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = processing.Resize(data, entry.Width, entry.Height, null, fitMode, color, format, encodeQuality);
                await ImagePathService.WriteAsync(path, result, overwrite, cancellationToken);
                images.Add(new SavedImage { Path = path, Width = entry.Width, Height = entry.Height, Format = format, Bytes = result.Length });
                logger.InfoImageSaved(ToolNames.ExportImageSizes, path, entry.Width, entry.Height, result.Length);

                if ((entry.Width == entry.Height) && sizePreset.IcoSizes.Contains(entry.Width))
                {
                    icoEntries.Add((entry.Width, format == ImageFormats.Png ? result : processing.Resize(data, entry.Width, entry.Height, null, fitMode, color, ImageFormats.Png, 100)));
                }
            }

            ExportedIco? exportedIco = null;
            if ((icoPath is not null) && (icoEntries.Count > 0))
            {
                var icoData = IcoWriter.Write(icoEntries);
                await ImagePathService.WriteAsync(icoPath, icoData, overwrite, cancellationToken);
                exportedIco = new ExportedIco(icoPath, icoEntries.Select(static x => x.Size).ToArray(), icoData.Length);
                logger.InfoImageSaved(ToolNames.ExportImageSizes, icoPath, 0, 0, icoData.Length);
            }

            return ToolResults.Success(new ExportSizesResult(directory, images, exportedIco, new SourceInfo(inputPath, info.Width, info.Height, info.Format)));
        });
    }

    private SizePreset ResolvePreset(string? preset, int[]? sizes, bool? ico, string baseName, string extension, int baseWidth, int baseHeight)
    {
        var hasPreset = !String.IsNullOrWhiteSpace(preset);
        var hasSizes = sizes is { Length: > 0 };
        if (hasPreset == hasSizes)
        {
            throw new AppException(AppErrorCode.InvalidParameter, "Specify either preset or sizes.");
        }

        if (hasPreset)
        {
            var presetName = ImageParameters.NormalizeChoice(preset, SizePresets.Names, SizePresets.Favicon, "preset");
            var result = SizePresets.Get(presetName, baseName, extension, baseWidth, baseHeight)!;
            if (ico == false)
            {
                result = result with { IcoSizes = [], IcoFileName = null };
            }
            else if ((ico == true) && (result.IcoFileName is null))
            {
                result = result with { IcoSizes = result.Entries.Where(static e => (e.Width == e.Height) && (e.Width <= IcoWriter.MaxSize)).Select(static e => e.Width).ToArray(), IcoFileName = $"{baseName}.ico" };
            }

            ValidatePresetSizes(result);
            return result;
        }

        if (sizes!.Length > MaxExportSizes)
        {
            throw new AppException(AppErrorCode.InvalidParameter, $"sizes must contain at most {MaxExportSizes} entries.");
        }

        var custom = SizePresets.Custom(sizes.Distinct().Order().ToArray(), baseName, extension, ico ?? false);
        ValidatePresetSizes(custom);
        return custom;
    }

    private void ValidatePresetSizes(SizePreset preset)
    {
        foreach (var entry in preset.Entries)
        {
            if ((entry.Width < 1) || (entry.Height < 1) || (entry.Width > processingSetting.MaxDimension) || (entry.Height > processingSetting.MaxDimension))
            {
                throw new AppException(AppErrorCode.InvalidParameter, $"The size {entry.Width}x{entry.Height} must be between 1 and {processingSetting.MaxDimension}.");
            }
        }
    }

    //--------------------------------------------------------------------------------
    // Execute
    //--------------------------------------------------------------------------------

    private async Task<CallToolResult> ExecuteAsync(string tool, string prefix, ProcessingArguments arguments, Func<ProcessingContext, byte[]> operation, CancellationToken cancellationToken)
    {
        return await ExecuteReadOnlyAsync(tool, async () =>
        {
            var inputPath = paths.ResolveInputPath(arguments.Input, "input");
            var data = await File.ReadAllBytesAsync(inputPath, cancellationToken);
            var info = ImageProcessingService.GetInfo(data);

            var explicitFormat = ImageParameters.NormalizeFormat(arguments.OutputFormat);
            ImageParameters.ValidateRange(arguments.Quality, 1, 100, "quality");

            // The output format defaults to the input format
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

            var result = operation(new ProcessingContext(data, info, output.Format, quality));
            await ImagePathService.WriteAsync(outputFile, result, arguments.Overwrite, cancellationToken);

            var resultInfo = ImageProcessingService.GetInfo(result);
            logger.InfoImageSaved(tool, outputFile, resultInfo.Width, resultInfo.Height, result.Length);

            var value = new ProcessingToolResult(
                outputFile,
                resultInfo.Width,
                resultInfo.Height,
                output.Format,
                result.Length,
                new SourceInfo(inputPath, info.Width, info.Height, info.Format));

            var content = new List<ContentBlock>();
            if (GeneratedImageResources.CreateLink(paths, outputFile, output.Format, result.Length) is { } link)
            {
                content.Add(link);
            }

            if (arguments.IncludeImage)
            {
                content.Add(ToolResults.Image(result, output.Format));
            }

            return ToolResults.Success(value, content);
        });
    }

    private async Task<CallToolResult> ExecuteReadOnlyAsync(string tool, Func<Task<CallToolResult>> operation)
    {
        var sw = Stopwatch.StartNew();
        var status = "success";
        using var activity = instrument.ActivitySource.StartActivity(tool);

        logger.InfoToolStarted(tool);

        try
        {
            var result = await operation();

            sw.Stop();
            logger.InfoToolCompleted(tool, 1, sw.Elapsed);

            return result;
        }
        catch (AppException ex)
        {
            status = "error";
            logger.WarnToolFailed(tool, ex.Code, ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return ToolResults.Error(ex.Message);
        }
        catch (OperationCanceledException)
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

    private sealed record ListedImage(string Name, string Path, int Width, int Height, string Format, long Bytes, string Modified);

    private sealed record ListImagesResult(string Directory, int Count, IReadOnlyList<ListedImage> Images);

    private sealed record ExportedIco(string Path, IReadOnlyList<int> Sizes, long Bytes);

    private sealed record ExportSizesResult(string Directory, IReadOnlyList<SavedImage> Images, ExportedIco? Ico, SourceInfo Source);
}
