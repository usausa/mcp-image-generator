namespace ImageGenerator.McpServer.Services;

using System.Security.Cryptography;

using ImageGenerator.McpServer.Errors;

public sealed record OutputTarget(string Path, bool IsDirectory, string Format);

public sealed class ImagePathService
{
    private const int CopyBufferSize = 81920;

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly TimeProvider timeProvider;

    private readonly string[] inputRoots;

    private readonly string[] outputRoots;

    public string OutputRoot { get; }

    public ImagePathService(ImageGeneratorSetting setting, TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
        OutputRoot = NormalizeRoot(setting.OutputPath);
        inputRoots = setting.InputRoots.Select(NormalizeRoot).ToArray();
        // 既定の出力先は常に許可する
        outputRoots = setting.OutputRoots.Count > 0 ? setting.OutputRoots.Select(NormalizeRoot).Append(OutputRoot).ToArray() : [];
    }

    //--------------------------------------------------------------------------------
    // Input
    //--------------------------------------------------------------------------------

    // 相対パスは出力ディレクトリ基準 (生成済みファイルをファイル名で参照できるようにする)
    public string ResolveInputPath(string path, string parameterName)
    {
        if (String.IsNullOrWhiteSpace(path))
        {
            throw new AppException(AppErrorCode.InvalidParameter, $"{parameterName} must not be empty.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(OutputRoot, path.Trim()));
        if (!IsUnderRoots(fullPath, inputRoots))
        {
            throw new AppException(AppErrorCode.InputNotAllowed, $"Reading '{path}' is not allowed. Allowed directories: {String.Join(", ", inputRoots)}");
        }

        if (!File.Exists(fullPath))
        {
            throw new AppException(AppErrorCode.InputNotFound, $"Input file not found: {path}");
        }

        if (ImageFormats.FromExtension(Path.GetExtension(fullPath)) is null)
        {
            throw new AppException(AppErrorCode.InvalidParameter, $"Unsupported image file: {path}. Supported extensions: .png, .jpg, .jpeg, .webp");
        }

        return fullPath;
    }

    //--------------------------------------------------------------------------------
    // Output
    //--------------------------------------------------------------------------------

    // 画像拡張子を持つパスはファイル、それ以外はディレクトリとして扱う
    public OutputTarget ResolveOutput(string? outputPath, string? explicitFormat, string defaultFormat)
    {
        if (String.IsNullOrWhiteSpace(outputPath))
        {
            return new OutputTarget(OutputRoot, true, explicitFormat ?? defaultFormat);
        }

        var text = outputPath.Trim();
        var fullPath = Path.GetFullPath(Path.Combine(OutputRoot, text));
        if (!IsUnderRoots(fullPath, outputRoots))
        {
            throw new AppException(AppErrorCode.OutputNotAllowed, $"Writing to '{outputPath}' is not allowed. Allowed directories: {String.Join(", ", outputRoots)}");
        }

        var endsWithSeparator = text.EndsWith(Path.DirectorySeparatorChar) || text.EndsWith(Path.AltDirectorySeparatorChar);
        var extensionFormat = endsWithSeparator ? null : ImageFormats.FromExtension(Path.GetExtension(fullPath));
        if (extensionFormat is null)
        {
            return new OutputTarget(fullPath, true, explicitFormat ?? defaultFormat);
        }

        if ((explicitFormat is not null) && (explicitFormat != extensionFormat))
        {
            throw new AppException(AppErrorCode.InvalidParameter, $"outputFormat '{explicitFormat}' does not match the extension of outputPath '{Path.GetFileName(fullPath)}'.");
        }

        return new OutputTarget(fullPath, false, extensionFormat);
    }

    // 生成前に保存先を確定し、上書き禁止の場合は既存ファイルを事前に検出する
    public IReadOnlyList<string> CreateFilePaths(OutputTarget target, string prefix, int count, bool overwrite)
    {
        var paths = new List<string>(count);
        if (target.IsDirectory)
        {
            var timestamp = timeProvider.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var suffix = RandomNumberGenerator.GetHexString(6, lowercase: true);
            var extension = ImageFormats.GetExtension(target.Format);
            for (var index = 1; index <= count; index++)
            {
                paths.Add(Path.Combine(target.Path, String.Create(CultureInfo.InvariantCulture, $"{prefix}-{timestamp}-{index:D2}-{suffix}{extension}")));
            }
        }
        else if (count == 1)
        {
            paths.Add(target.Path);
        }
        else
        {
            var directory = Path.GetDirectoryName(target.Path)!;
            var name = Path.GetFileNameWithoutExtension(target.Path);
            var extension = Path.GetExtension(target.Path);
            for (var index = 1; index <= count; index++)
            {
                paths.Add(Path.Combine(directory, String.Create(CultureInfo.InvariantCulture, $"{name}-{index:D2}{extension}")));
            }
        }

        if (!overwrite)
        {
            var existing = paths.FirstOrDefault(File.Exists);
            if (existing is not null)
            {
                throw new AppException(AppErrorCode.OutputExists, $"File already exists: {existing}. Pass overwrite=true to replace it.");
            }
        }

        return paths;
    }

    public static async Task WriteAsync(string path, ReadOnlyMemory<byte> data, bool overwrite, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true))
            {
                await fs.WriteAsync(data, cancellationToken);
            }

            File.Move(tempPath, path, overwrite);
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static string NormalizeRoot(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsUnderRoots(string fullPath, string[] roots)
    {
        if (roots.Length == 0)
        {
            return true;
        }

        foreach (var root in roots)
        {
            if (fullPath.Equals(root, PathComparison) || fullPath.StartsWith(root + Path.DirectorySeparatorChar, PathComparison))
            {
                return true;
            }
        }

        return false;
    }
}
