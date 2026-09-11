namespace ImageGenerator.McpServer.Tools;

using System.Buffers.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using ModelContextProtocol.Protocol;

public static class ToolResults
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string ToJson<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    public static CallToolResult Success<T>(T value, IEnumerable<ContentBlock>? additionalContent = null)
    {
        var content = new List<ContentBlock> { new TextContentBlock { Text = ToJson(value) } };
        if (additionalContent is not null)
        {
            content.AddRange(additionalContent);
        }

        return new CallToolResult { Content = content };
    }

    public static CallToolResult Error(string message) =>
        new() { IsError = true, Content = [new TextContentBlock { Text = message }] };

    public static ImageContentBlock Image(ReadOnlyMemory<byte> data, string format) =>
        new() { Data = EncodeBase64(data), MimeType = ImageFormats.GetContentType(format) };

    public static ResourceLinkBlock ResourceLink(string locator, string name, string format, long size) =>
        new() { Uri = locator, Name = name, MimeType = ImageFormats.GetContentType(format), Size = size };

    // The SDK expects the UTF-8 bytes of the base64 string
    public static ReadOnlyMemory<byte> EncodeBase64(ReadOnlyMemory<byte> data)
    {
        var buffer = new byte[Base64.GetMaxEncodedToUtf8Length(data.Length)];
        Base64.EncodeToUtf8(data.Span, buffer, out _, out var written);
        return buffer.AsMemory(0, written);
    }
}
