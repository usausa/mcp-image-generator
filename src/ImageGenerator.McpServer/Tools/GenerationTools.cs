namespace ImageGenerator.McpServer.Tools;

using System.Diagnostics;

using ImageGenerator.McpServer.Errors;
using ImageGenerator.McpServer.Resources;
using ImageGenerator.McpServer.Services;
using ImageGenerator.McpServer.Telemetry;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class GenerationTools
{
    private const string SizeDescription = "Model render size: 1024x1024, 1024x1536 (portrait) or 1536x1024 (landscape). Default: the closest aspect ratio to width/height, otherwise the server default.";
    private const string QualityDescription = "Render quality: low, medium or high. Default: server default.";
    private const string BackgroundDescription = "Background: auto, transparent or opaque. transparent requires png or webp output.";
    private const string OutputFormatDescription = "Output format: png, jpeg or webp. Default: the extension of outputPath, otherwise the server default.";
    private const string OutputCompressionDescription = "Compression quality 0-100 for jpeg/webp output. Default: server default.";
    private const string CountDescription = "Number of images to generate (each is a separate model call). Default 1.";
    private const string WidthDescription = "Final width in pixels. When width and/or height is given, the rendered image is cropped and resized to that size (see fit). Omit both to keep the render size.";
    private const string HeightDescription = "Final height in pixels.";
    private const string FitDescription = "How to fit the render into width x height: cover (center crop to the target aspect ratio, default), contain (fit inside without padding; the result can be smaller than the target) or pad (fit inside and pad with a transparent or white background).";
    private const string OutputPathDescription = "Destination file path (with .png, .jpg or .webp extension) or directory. Relative paths resolve under the server output directory. Default: server output directory with an automatic file name.";
    private const string OverwriteDescription = "Allow replacing an existing file at outputPath. Default false.";
    private const string IncludeImageDescription = "Include the image data as base64 in the result. Default false; the saved file path is always returned.";

    private static readonly string[] Sizes = ["1024x1024", "1024x1536", "1536x1024"];

    private static readonly string[] Qualities = ["low", "medium", "high"];

    private static readonly string[] Backgrounds = [ImageGenerationService.BackgroundAuto, "transparent", "opaque"];

    private readonly ImageGenerationService generation;

    private readonly ImageProcessingService processing;

    private readonly ImagePathService paths;

    private readonly ImageGeneratorSetting setting;

    private readonly ImageProcessingSetting processingSetting;

    private readonly ApplicationInstrument instrument;

    private readonly ILogger<GenerationTools> logger;

    public GenerationTools(
        ImageGenerationService generation,
        ImageProcessingService processing,
        ImagePathService paths,
        ImageGeneratorSetting setting,
        ImageProcessingSetting processingSetting,
        ApplicationInstrument instrument,
        ILogger<GenerationTools> logger)
    {
        this.generation = generation;
        this.processing = processing;
        this.paths = paths;
        this.setting = setting;
        this.processingSetting = processingSetting;
        this.instrument = instrument;
        this.logger = logger;
    }

    //--------------------------------------------------------------------------------
    // Tools
    //--------------------------------------------------------------------------------

    [McpServerTool(Name = ToolNames.GenerateImage, Title = "Generate image", Destructive = false, OpenWorld = true)]
    [Description("Generates an image asset from a text prompt with the Foundry image model (gpt-image) and saves it to disk. The model renders 1024x1024, 1024x1536 or 1536x1024; pass width/height to get the final asset size (the server crops to the aspect ratio and resizes with SkiaSharp). Takes 30 seconds to several minutes per image. Returns the saved file path(s), image size and token usage.")]
    public Task<CallToolResult> GenerateImageAsync(
        [Description("Text prompt describing the image. Include style, subject, composition and background; add 'no text, no watermark' for assets.")] string prompt,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken cancellationToken,
        [Description(SizeDescription)] string? size = null,
        [Description(QualityDescription)] string? quality = null,
        [Description(BackgroundDescription)] string? background = null,
        [Description(OutputFormatDescription)] string? outputFormat = null,
        [Description(OutputCompressionDescription)] int? outputCompression = null,
        [Description(CountDescription)] int count = 1,
        [Description(WidthDescription)] int? width = null,
        [Description(HeightDescription)] int? height = null,
        [Description(FitDescription)] string? fit = null,
        [Description(OutputPathDescription)] string? outputPath = null,
        [Description(OverwriteDescription)] bool overwrite = false,
        [Description(IncludeImageDescription)] bool includeImage = false)
    {
        var arguments = new GenerationArguments
        {
            Prompt = prompt,
            Size = size,
            Quality = quality,
            Background = background,
            OutputFormat = outputFormat,
            OutputCompression = outputCompression,
            Count = count,
            Width = width,
            Height = height,
            Fit = fit,
            OutputPath = outputPath,
            Overwrite = overwrite,
            IncludeImage = includeImage
        };
        return ExecuteAsync(ToolNames.GenerateImage, arguments, progress, cancellationToken);
    }

    [McpServerTool(Name = ToolNames.EditImage, Title = "Edit image", Destructive = false, OpenWorld = true)]
    [Description("Generates an image from one or more reference images and a text prompt with the Foundry image model (images/edits), e.g. redraw a character in another style, keep a character identity, or apply a composition. Refer to the references in the prompt as 'image 1', 'image 2' in order. Same sizing, output and post-processing options as generate_image.")]
    public Task<CallToolResult> EditImageAsync(
        [Description("Text prompt describing the desired result and how to use the reference images.")] string prompt,
        [Description("Reference image file paths (png, jpg or webp) on the server machine, in the order referred to by the prompt. At least one.")] string[] images,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken cancellationToken,
        [Description("Optional mask image file path. Fully transparent areas of the mask mark the region of the first reference image to be replaced.")] string? mask = null,
        [Description(SizeDescription)] string? size = null,
        [Description(QualityDescription)] string? quality = null,
        [Description(BackgroundDescription)] string? background = null,
        [Description(OutputFormatDescription)] string? outputFormat = null,
        [Description(OutputCompressionDescription)] int? outputCompression = null,
        [Description(CountDescription)] int count = 1,
        [Description(WidthDescription)] int? width = null,
        [Description(HeightDescription)] int? height = null,
        [Description(FitDescription)] string? fit = null,
        [Description(OutputPathDescription)] string? outputPath = null,
        [Description(OverwriteDescription)] bool overwrite = false,
        [Description(IncludeImageDescription)] bool includeImage = false)
    {
        var arguments = new GenerationArguments
        {
            Prompt = prompt,
            Images = images,
            Mask = mask,
            Size = size,
            Quality = quality,
            Background = background,
            OutputFormat = outputFormat,
            OutputCompression = outputCompression,
            Count = count,
            Width = width,
            Height = height,
            Fit = fit,
            OutputPath = outputPath,
            Overwrite = overwrite,
            IncludeImage = includeImage
        };
        return ExecuteAsync(ToolNames.EditImage, arguments, progress, cancellationToken);
    }

    //--------------------------------------------------------------------------------
    // Execute
    //--------------------------------------------------------------------------------

    private async Task<CallToolResult> ExecuteAsync(string tool, GenerationArguments arguments, IProgress<ProgressNotificationValue> progress, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var status = "success";
        using var activity = instrument.ActivitySource.StartActivity(tool);

        logger.InfoToolStarted(tool);

        try
        {
            var plan = CreatePlan(tool, arguments);

            activity?.SetTag("image.size", plan.Request.Size);
            activity?.SetTag("image.quality", plan.Request.Quality);
            activity?.SetTag("image.format", plan.Format);
            activity?.SetTag("image.count", plan.Count);

            var saved = new List<(SavedImage Image, ReadOnlyMemory<byte> Data)>(plan.Count);
            var usages = new List<ImageUsage>(plan.Count);
            for (var index = 1; index <= plan.Count; index++)
            {
                var current = index;
                progress.Report(new ProgressNotificationValue { Progress = current - 1, Total = plan.Count, Message = String.Create(CultureInfo.InvariantCulture, $"Generating image {current}/{plan.Count}...") });

                var result = await generation.GenerateAsync(
                    plan.Request,
                    message => progress.Report(new ProgressNotificationValue { Progress = current - 1, Total = plan.Count, Message = message }),
                    cancellationToken);

                if (result.Usage is not null)
                {
                    usages.Add(result.Usage);
                    RecordTokens(tool, result.Usage);
                }

                var data = result.Data;
                if (plan.NeedsFit)
                {
                    progress.Report(new ProgressNotificationValue { Progress = current - 1, Total = plan.Count, Message = "Cropping and resizing..." });
                    data = processing.Fit(data, plan.Width, plan.Height, plan.Fit, plan.Format, plan.EncodeQuality);
                }

                var path = plan.OutputPaths[index - 1];
                await ImagePathService.WriteAsync(path, data, arguments.Overwrite, cancellationToken);

                var info = ImageProcessingService.GetInfo(data);
                var image = new SavedImage { Path = path, Width = info.Width, Height = info.Height, Format = plan.Format, Bytes = data.Length };
                saved.Add((image, data));

                logger.InfoImageSaved(tool, path, info.Width, info.Height, data.Length);
            }

            progress.Report(new ProgressNotificationValue { Progress = plan.Count, Total = plan.Count, Message = "Completed" });
            instrument.AddGeneratedImages(tool, saved.Count);

            sw.Stop();
            logger.InfoToolCompleted(tool, saved.Count, sw.Elapsed);

            var usage = Aggregate(usages);
            activity?.SetTag("image.generation.tokens.input", usage?.InputTokens);
            activity?.SetTag("image.generation.tokens.output", usage?.OutputTokens);

            var value = new GenerationToolResult(saved.Select(static x => x.Image).ToArray(), usage, Math.Round(sw.Elapsed.TotalSeconds, 1));
            var content = new List<ContentBlock>();
            foreach (var (image, imageData) in saved)
            {
                if (GeneratedImageResources.CreateLink(paths, image.Path, plan.Format, image.Bytes) is { } link)
                {
                    content.Add(link);
                }

                if (arguments.IncludeImage)
                {
                    content.Add(ToolResults.Image(imageData, plan.Format));
                }
            }

            return ToolResults.Success(value, content);
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
        catch (HttpRequestException ex)
        {
            status = "error";
            logger.ErrorToolFailed(ex, tool);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return ToolResults.Error($"The request to Foundry failed: {ex.Message}");
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

    private void RecordTokens(string tool, ImageUsage usage)
    {
        instrument.AddGenerationTokens(tool, "input", usage.InputTokens);
        instrument.AddGenerationTokens(tool, "output", usage.OutputTokens);
        if (usage.InputTextTokens is not null)
        {
            instrument.AddGenerationTokens(tool, "input_text", usage.InputTextTokens.Value);
        }

        if (usage.InputImageTokens is not null)
        {
            instrument.AddGenerationTokens(tool, "input_image", usage.InputImageTokens.Value);
        }
    }

    private static ImageUsage? Aggregate(List<ImageUsage> usages)
    {
        if (usages.Count == 0)
        {
            return null;
        }

        return new ImageUsage
        {
            InputTokens = usages.Sum(static x => x.InputTokens),
            OutputTokens = usages.Sum(static x => x.OutputTokens),
            TotalTokens = usages.Sum(static x => x.TotalTokens),
            InputTextTokens = usages.Any(static x => x.InputTextTokens is not null) ? usages.Sum(static x => x.InputTextTokens ?? 0) : null,
            InputImageTokens = usages.Any(static x => x.InputImageTokens is not null) ? usages.Sum(static x => x.InputImageTokens ?? 0) : null
        };
    }

    //--------------------------------------------------------------------------------
    // Plan
    //--------------------------------------------------------------------------------

    // Validate and normalize the input and decide the output files before calling Foundry
    private GenerationPlan CreatePlan(string tool, GenerationArguments arguments)
    {
        var prompt = arguments.Prompt?.Trim();
        if (String.IsNullOrEmpty(prompt))
        {
            throw new AppException(AppErrorCode.InvalidParameter, "prompt must not be empty.");
        }

        if ((arguments.Count < 1) || (arguments.Count > setting.MaxCount))
        {
            throw new AppException(AppErrorCode.InvalidParameter, $"count must be between 1 and {setting.MaxCount}.");
        }

        ValidateDimension(arguments.Width, "width");
        ValidateDimension(arguments.Height, "height");
        var needsFit = (arguments.Width is not null) || (arguments.Height is not null);

        var quality = ImageParameters.NormalizeChoice(arguments.Quality, Qualities, setting.Defaults.Quality, "quality");
        var background = ImageParameters.NormalizeChoice(arguments.Background, Backgrounds, ImageGenerationService.BackgroundAuto, "background");
        var fit = ImageParameters.ParseFit(arguments.Fit, allowStretch: false);
        var size = arguments.Size is not null
            ? ImageParameters.NormalizeChoice(arguments.Size, Sizes, setting.Defaults.Size, "size")
            : SelectSize(arguments.Width, arguments.Height);

        var explicitFormat = ImageParameters.NormalizeFormat(arguments.OutputFormat);

        if (arguments.OutputCompression is < 0 or > 100)
        {
            throw new AppException(AppErrorCode.InvalidParameter, "outputCompression must be between 0 and 100.");
        }

        var defaultFormat = ImageFormats.Normalize(setting.Defaults.OutputFormat) ?? ImageFormats.Png;
        var output = paths.ResolveOutput(arguments.OutputPath, explicitFormat, defaultFormat);
        var format = output.Format;

        if ((background == "transparent") && !ImageFormats.SupportsTransparency(format))
        {
            throw new AppException(AppErrorCode.InvalidParameter, "background=transparent requires png or webp output.");
        }

        var references = (arguments.Images ?? []).Select((image, i) => paths.ResolveInputPath(image, String.Create(CultureInfo.InvariantCulture, $"images[{i}]"))).ToArray();
        if ((tool == ToolNames.EditImage) && (references.Length == 0))
        {
            throw new AppException(AppErrorCode.InvalidParameter, "images must contain at least one reference image.");
        }

        var mask = arguments.Mask is not null ? paths.ResolveInputPath(arguments.Mask, "mask") : null;

        var outputPaths = paths.CreateFilePaths(output, tool == ToolNames.EditImage ? "edit" : "generate", arguments.Count, arguments.Overwrite);

        return new GenerationPlan
        {
            Request = new ImageGenerationRequest
            {
                Prompt = prompt,
                Size = size,
                Quality = quality,
                Background = background,
                OutputFormat = format,
                OutputCompression = arguments.OutputCompression ?? setting.Defaults.OutputCompression,
                ReferenceImages = references,
                MaskImage = mask
            },
            Count = arguments.Count,
            Format = format,
            EncodeQuality = processing.ResolveQuality(format, arguments.OutputCompression),
            NeedsFit = needsFit,
            Width = arguments.Width,
            Height = arguments.Height,
            Fit = fit,
            OutputPaths = outputPaths
        };
    }

    private void ValidateDimension(int? value, string name)
    {
        if (value is not null && ((value.Value < 1) || (value.Value > processingSetting.MaxDimension)))
        {
            throw new AppException(AppErrorCode.InvalidParameter, $"{name} must be between 1 and {processingSetting.MaxDimension}.");
        }
    }

    private string SelectSize(int? width, int? height)
    {
        var defaultSize = ImageParameters.NormalizeChoice(setting.Defaults.Size, Sizes, Sizes[0], "Defaults:Size");
        if (width is null || height is null)
        {
            return defaultSize;
        }

        // Pick the render size whose aspect ratio is closest to the final size
        var target = Math.Log((double)width.Value / height.Value);
        return Sizes
            .Select(static x => (Size: x, Ratio: Math.Log(AspectRatio(x))))
            .OrderBy(x => Math.Abs(x.Ratio - target))
            .First()
            .Size;
    }

    private static double AspectRatio(string size)
    {
        var parts = size.Split('x');
        return Double.Parse(parts[0], CultureInfo.InvariantCulture) / Double.Parse(parts[1], CultureInfo.InvariantCulture);
    }

    //--------------------------------------------------------------------------------
    // Types
    //--------------------------------------------------------------------------------

    private sealed record GenerationArguments
    {
        public string? Prompt { get; init; }

        public string[]? Images { get; init; }

        public string? Mask { get; init; }

        public string? Size { get; init; }

        public string? Quality { get; init; }

        public string? Background { get; init; }

        public string? OutputFormat { get; init; }

        public int? OutputCompression { get; init; }

        public int Count { get; init; }

        public int? Width { get; init; }

        public int? Height { get; init; }

        public string? Fit { get; init; }

        public string? OutputPath { get; init; }

        public bool Overwrite { get; init; }

        public bool IncludeImage { get; init; }
    }

    private sealed record GenerationPlan
    {
        public required ImageGenerationRequest Request { get; init; }

        public required int Count { get; init; }

        public required string Format { get; init; }

        public required int EncodeQuality { get; init; }

        public required bool NeedsFit { get; init; }

        public int? Width { get; init; }

        public int? Height { get; init; }

        public required FitMode Fit { get; init; }

        public required IReadOnlyList<string> OutputPaths { get; init; }
    }

    private sealed record GenerationToolResult(IReadOnlyList<SavedImage> Images, ImageUsage? Usage, double ElapsedSeconds);
}
