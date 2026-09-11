namespace ImageGenerator.McpServer.Resources;

using ImageGenerator.McpServer.Services;
using ImageGenerator.McpServer.Tools;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

// Exposes the files in the default output directory as MCP resources (generated-image://{fileName})
public static class GeneratedImageResources
{
    public const string Scheme = "generated-image";

    private const string UriPrefix = Scheme + "://";

    private const int MaxEntries = 200;

    public static string GetResourceLocator(string fileName) => UriPrefix + Uri.EscapeDataString(fileName);

    public static ResourceLinkBlock? CreateLink(ImagePathService paths, string path, string format, long size)
    {
        if (!paths.IsInOutputRoot(path))
        {
            return null;
        }

        var name = Path.GetFileName(path);
        return ToolResults.ResourceLink(GetResourceLocator(name), name, format, size);
    }

    //--------------------------------------------------------------------------------
    // Handler
    //--------------------------------------------------------------------------------

    public static ValueTask<ListResourcesResult> ListAsync(RequestContext<ListResourcesRequestParams> context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var paths = context.Services!.GetRequiredService<ImagePathService>();
        var resources = new List<Resource>();
        if (Directory.Exists(paths.OutputRoot))
        {
            foreach (var file in new DirectoryInfo(paths.OutputRoot).EnumerateFiles().Where(static f => ImagePathService.IsSupportedImageFile(f.Name)).OrderByDescending(static f => f.LastWriteTimeUtc).Take(MaxEntries))
            {
                var format = ImageFormats.FromExtension(file.Extension) ?? ImageFormats.Png;
                resources.Add(new Resource
                {
                    Uri = GetResourceLocator(file.Name),
                    Name = file.Name,
                    MimeType = ImageFormats.GetContentType(format),
                    Size = file.Length
                });
            }
        }

        return ValueTask.FromResult(new ListResourcesResult { Resources = resources });
    }

    public static async ValueTask<ReadResourceResult> ReadAsync(RequestContext<ReadResourceRequestParams> context, CancellationToken cancellationToken)
    {
        var uri = context.Params.Uri;
        if (String.IsNullOrEmpty(uri) || !uri.StartsWith(UriPrefix, StringComparison.Ordinal))
        {
            throw new McpException($"Unknown resource: {uri}");
        }

        var name = Uri.UnescapeDataString(uri[UriPrefix.Length..]);
        if (String.IsNullOrEmpty(name) || (name != Path.GetFileName(name)) || !ImagePathService.IsSupportedImageFile(name))
        {
            throw new McpException($"Invalid resource: {uri}");
        }

        var paths = context.Services!.GetRequiredService<ImagePathService>();
        var path = Path.Combine(paths.OutputRoot, name);
        if (!File.Exists(path))
        {
            throw new McpException($"Resource not found: {uri}");
        }

        var data = await File.ReadAllBytesAsync(path, cancellationToken);
        var format = ImageFormats.FromExtension(Path.GetExtension(name)) ?? ImageFormats.Png;

        return new ReadResourceResult
        {
            Contents =
            [
                new BlobResourceContents
                {
                    Uri = uri,
                    MimeType = ImageFormats.GetContentType(format),
                    Blob = ToolResults.EncodeBase64(data)
                }
            ]
        };
    }
}
