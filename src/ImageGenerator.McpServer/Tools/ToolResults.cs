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

    // DataはBase64文字列のUTF-8バイト列
    public static ImageContentBlock Image(ReadOnlyMemory<byte> data, string format)
    {
        var buffer = new byte[Base64.GetMaxEncodedToUtf8Length(data.Length)];
        Base64.EncodeToUtf8(data.Span, buffer, out _, out var written);
        return new ImageContentBlock { Data = buffer.AsMemory(0, written), MimeType = ImageFormats.GetContentType(format) };
    }
}
