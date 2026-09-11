namespace ImageGenerator.McpServer.Services;

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

using ImageGenerator.McpServer.Errors;
using ImageGenerator.McpServer.Telemetry;

public sealed class ImageGenerationService : IDisposable
{
    public const string HttpClientName = "Foundry";

    public const string BackgroundAuto = "auto";

    private const int ErrorMessageMaxLength = 300;

    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(1);

    private readonly ILogger<ImageGenerationService> logger;

    private readonly HttpClient httpClient;

    private readonly ImageGeneratorSetting setting;

    private readonly ApplicationInstrument instrument;

    private readonly SemaphoreSlim semaphore;

    public ImageGenerationService(
        ILogger<ImageGenerationService> logger,
        IHttpClientFactory httpClientFactory,
        ImageGeneratorSetting setting,
        ApplicationInstrument instrument)
    {
        this.logger = logger;
        this.setting = setting;
        this.instrument = instrument;
        httpClient = httpClientFactory.CreateClient(HttpClientName);
        semaphore = new SemaphoreSlim(setting.MaxConcurrency);
    }

    public void Dispose()
    {
        semaphore.Dispose();
        httpClient.Dispose();
    }

    //--------------------------------------------------------------------------------
    // Generate
    //--------------------------------------------------------------------------------

    public async Task<ImageGenerationResult> GenerateAsync(ImageGenerationRequest request, Action<string>? onStatus, CancellationToken cancellationToken)
    {
        var tool = request.IsEdit ? ToolNames.EditImage : ToolNames.GenerateImage;

        logger.InfoGenerationStarted(tool, request.Size, request.Quality, request.Background, request.OutputFormat, request.ReferenceImages.Count, request.MaskImage is not null);

        // 上流の同時実行数を制限する。空きがなければ待機する
        if (semaphore.CurrentCount == 0)
        {
            onStatus?.Invoke("Waiting for a free generation slot...");
        }

        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var sw = Stopwatch.StartNew();
            var (data, usage) = await ExecuteWithRetryAsync(tool, ct => SendAsync(request, onStatus, ct), onStatus, cancellationToken);
            sw.Stop();

            logger.InfoGenerationCompleted(tool, data.Length, sw.Elapsed, usage?.InputTokens, usage?.OutputTokens);

            return new ImageGenerationResult { Data = data, Usage = usage, Elapsed = sw.Elapsed };
        }
        finally
        {
            semaphore.Release();
        }
    }

    //--------------------------------------------------------------------------------
    // Request
    //--------------------------------------------------------------------------------

    private async Task<(byte[] Data, ImageUsage? Usage)> SendAsync(ImageGenerationRequest request, Action<string>? onStatus, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
#pragma warning disable CA2000
        content.Add(new StringContent(request.Prompt), "prompt");
        content.Add(new StringContent(setting.DeploymentName), "model");
        content.Add(new StringContent(request.Size), "size");
        content.Add(new StringContent(request.Quality), "quality");
        content.Add(new StringContent(request.OutputFormat), "output_format");
        if (request.OutputFormat is ImageFormats.Jpeg or ImageFormats.Webp)
        {
            content.Add(new StringContent(request.OutputCompression.ToString(CultureInfo.InvariantCulture)), "output_compression");
        }

        if (request.Background != BackgroundAuto)
        {
            content.Add(new StringContent(request.Background), "background");
        }

        content.Add(new StringContent("1"), "n");

        var index = 0;
        foreach (var image in request.ReferenceImages)
        {
            index++;
            content.Add(await CreateFileContentAsync(image, cancellationToken), "image[]", String.Create(CultureInfo.InvariantCulture, $"image{index:D2}{Path.GetExtension(image)}"));
        }

        if (request.MaskImage is not null)
        {
            content.Add(await CreateFileContentAsync(request.MaskImage, cancellationToken), "mask", "mask" + Path.GetExtension(request.MaskImage));
        }
#pragma warning restore CA2000

        var apiPath = request.IsEdit
            ? $"openai/deployments/{setting.DeploymentName}/images/edits?api-version={setting.ApiVersion}"
            : $"openai/deployments/{setting.DeploymentName}/images/generations?api-version={setting.ApiVersion}";

        var endpoint = setting.Endpoint.EndsWith('/') ? setting.Endpoint : setting.Endpoint + "/";
        var requestUrl = new Uri(new Uri(endpoint), apiPath);

        onStatus?.Invoke("Sending request to Foundry...");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(setting.RequestTimeoutMinutes));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        httpRequest.Content = content;
        httpRequest.Headers.Add("api-key", setting.ApiKey);

        using var response = await httpClient.SendAsync(httpRequest, cts.Token);
        var responseBody = await response.Content.ReadAsStringAsync(cts.Token);

        if (!response.IsSuccessStatusCode)
        {
            logger.WarnGenerationRequestFailed((int)response.StatusCode, responseBody);
            throw CreateRequestException(response, responseBody);
        }

        return ParseResponse(responseBody);
    }

    private static async Task<ByteArrayContent> CreateFileContentAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var format = ImageFormats.FromExtension(Path.GetExtension(path)) ?? ImageFormats.Png;
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(ImageFormats.GetContentType(format));
        return content;
    }

    //--------------------------------------------------------------------------------
    // Response
    //--------------------------------------------------------------------------------

    private static (byte[] Data, ImageUsage? Usage) ParseResponse(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;

            if (!root.TryGetProperty("data", out var data) || (data.ValueKind != JsonValueKind.Array) || (data.GetArrayLength() == 0))
            {
                throw new AppException(AppErrorCode.GenerationNoData, "The API returned no image data.");
            }

            if (!data[0].TryGetProperty("b64_json", out var b64) || (b64.ValueKind != JsonValueKind.String))
            {
                throw new AppException(AppErrorCode.GenerationNoData, "The API returned no image data (b64_json is missing).");
            }

            return (Convert.FromBase64String(b64.GetString()!), ParseUsage(root));
        }
        catch (JsonException ex)
        {
            throw new AppException(AppErrorCode.GenerationNoData, "The API returned an invalid response.", ex);
        }
        catch (FormatException ex)
        {
            throw new AppException(AppErrorCode.GenerationNoData, "The API returned invalid image data.", ex);
        }
    }

    private static ImageUsage? ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || (usage.ValueKind != JsonValueKind.Object))
        {
            return null;
        }

        long? inputTextTokens = null;
        long? inputImageTokens = null;
        if (usage.TryGetProperty("input_tokens_details", out var details) && (details.ValueKind == JsonValueKind.Object))
        {
            inputTextTokens = GetInt64OrNull(details, "text_tokens");
            inputImageTokens = GetInt64OrNull(details, "image_tokens");
        }

        return new ImageUsage
        {
            InputTokens = GetInt64OrNull(usage, "input_tokens") ?? 0,
            OutputTokens = GetInt64OrNull(usage, "output_tokens") ?? 0,
            TotalTokens = GetInt64OrNull(usage, "total_tokens") ?? 0,
            InputTextTokens = inputTextTokens,
            InputImageTokens = inputImageTokens
        };
    }

    private static long? GetInt64OrNull(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && (value.ValueKind == JsonValueKind.Number) && value.TryGetInt64(out var number) ? number : null;

    //--------------------------------------------------------------------------------
    // Retry
    //--------------------------------------------------------------------------------

    private async Task<T> ExecuteWithRetryAsync<T>(string tool, Func<CancellationToken, Task<T>> operation, Action<string>? onStatus, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (AppException ex) when ((attempt <= setting.MaxRetries) &&
                                         (ex.StatusCode is not null) &&
                                         IsRetryableStatus(ex.StatusCode.Value))
            {
                var delay = GetRetryDelay(attempt, ex.RetryAfter);
                instrument.IncrementGenerationRetry(tool, (int)ex.StatusCode.Value);
                logger.WarnGenerationRetry(delay.TotalSeconds, attempt, setting.MaxRetries, ex.Message);
                onStatus?.Invoke(String.Create(CultureInfo.InvariantCulture, $"Foundry is busy. Retrying in {delay.TotalSeconds:0}s ({attempt}/{setting.MaxRetries})..."));
                await Task.Delay(delay, cancellationToken);
            }
            catch (HttpRequestException ex) when ((attempt <= setting.MaxRetries) &&
                                                 ((ex.StatusCode is null) || IsRetryableStatus(ex.StatusCode.Value)))
            {
                var delay = GetRetryDelay(attempt, null);
                instrument.IncrementGenerationRetry(tool, (int?)ex.StatusCode ?? 0);
                logger.WarnGenerationRetry(delay.TotalSeconds, attempt, setting.MaxRetries, ex.Message);
                onStatus?.Invoke(String.Create(CultureInfo.InvariantCulture, $"Connection failed. Retrying in {delay.TotalSeconds:0}s ({attempt}/{setting.MaxRetries})..."));
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // 呼び出し元のキャンセルではなく、1リクエストあたりのタイムアウト
                if (attempt > setting.MaxRetries)
                {
                    throw new AppException(AppErrorCode.GenerationTimeout, "The request to Foundry timed out.", ex);
                }

                var delay = GetRetryDelay(attempt, null);
                instrument.IncrementGenerationRetry(tool, 0);
                logger.WarnGenerationRetryTimeout(delay.TotalSeconds, attempt, setting.MaxRetries);
                onStatus?.Invoke(String.Create(CultureInfo.InvariantCulture, $"Request timed out. Retrying in {delay.TotalSeconds:0}s ({attempt}/{setting.MaxRetries})..."));
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool IsRetryableStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static TimeSpan GetRetryDelay(int attempt, TimeSpan? retryAfter)
    {
        var delay = (retryAfter > TimeSpan.Zero) ? retryAfter.Value : TimeSpan.FromSeconds(Math.Pow(2, attempt));
        return (delay > MaxRetryDelay) ? MaxRetryDelay : delay;
    }

    //--------------------------------------------------------------------------------
    // Error
    //--------------------------------------------------------------------------------

    // ツール結果にはステータスとAPIのエラー要約のみ含める (本文全体はログへ)
    private static AppException CreateRequestException(HttpResponseMessage response, string responseBody)
    {
        var message = $"Image generation request failed. Status={(int)response.StatusCode} {response.StatusCode}.";
        var detail = ExtractErrorDetail(responseBody);
        if (detail is not null)
        {
            message += " " + detail;
        }

        return new AppException(AppErrorCode.GenerationRequestFailed, message, response.StatusCode, GetRetryAfter(response));
    }

    private static string? ExtractErrorDetail(string responseBody)
    {
        if (String.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if ((document.RootElement.ValueKind != JsonValueKind.Object) || !document.RootElement.TryGetProperty("error", out var error))
            {
                return null;
            }

            string? code = null;
            string? text;
            if (error.ValueKind == JsonValueKind.Object)
            {
                code = error.TryGetProperty("code", out var codeElement) && (codeElement.ValueKind == JsonValueKind.String) ? codeElement.GetString() : null;
                text = error.TryGetProperty("message", out var messageElement) && (messageElement.ValueKind == JsonValueKind.String) ? messageElement.GetString() : error.ToString();
            }
            else
            {
                text = error.ToString();
            }

            if ((text is not null) && (text.Length > ErrorMessageMaxLength))
            {
                text = text[..ErrorMessageMaxLength] + "...";
            }

            return code is not null ? $"Error: {code}. {text}" : $"Error: {text}";
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is not null)
        {
            return retryAfter.Delta;
        }

        return (retryAfter.Date is not null) ? retryAfter.Date - DateTimeOffset.UtcNow : null;
    }
}
