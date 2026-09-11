namespace ImageGenerator.McpServer;

using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// Fake Foundry Images API: records requests and returns a fixed image with usage
public sealed class FakeFoundryHandler : HttpMessageHandler
{
    public Collection<FoundryRequest> Requests { get; } = [];

    public ReadOnlyMemory<byte> ImageData { get; set; } = TestImages.CreatePng(768, 512, transparent: false);

    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

    public string ErrorBody { get; set; } = "{\"error\":{\"code\":\"server_error\",\"message\":\"Something went wrong.\"}}";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var apiKey = request.Headers.TryGetValues("api-key", out var values) ? values.First() : null;
        Requests.Add(new FoundryRequest(request.RequestUri!, apiKey, request.Content?.Headers.ContentType?.MediaType, body));

        if (StatusCode != HttpStatusCode.OK)
        {
            return CreateResponse(StatusCode, ErrorBody);
        }

        var json = JsonSerializer.Serialize(new
        {
            created = 1,
            data = new[] { new { b64_json = Convert.ToBase64String(ImageData.Span) } },
            usage = new
            {
                total_tokens = 130,
                input_tokens = 30,
                output_tokens = 100,
                input_tokens_details = new { text_tokens = 20, image_tokens = 10 }
            }
        });
        return CreateResponse(HttpStatusCode.OK, json);
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string json)
    {
#pragma warning disable CA2000
        return new HttpResponseMessage(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
#pragma warning restore CA2000
    }
}

public sealed partial record FoundryRequest(Uri Uri, string? ApiKey, string? MediaType, string Body)
{
    public bool IsJson => MediaType == "application/json";

    // Reads a field from the JSON body or a multipart text part
    public string? GetField(string name)
    {
        if (IsJson)
        {
            using var document = JsonDocument.Parse(Body);
            if (!document.RootElement.TryGetProperty(name, out var value))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        }

        var match = FieldRegex().Matches(Body).FirstOrDefault(m => m.Groups["name"].Value == name);
        return match?.Groups["value"].Value;
    }

    public bool HasField(string name) => GetField(name) is not null;

    [GeneratedRegex("name=\"?(?<name>[^\";\r\n]+)\"?[^\r\n]*\r\n\r\n(?<value>[^\r]*)")]
    private static partial Regex FieldRegex();
}
