namespace ImageGenerator.McpServer.Settings;

#pragma warning disable CA1002
#pragma warning disable CA1034
public sealed class ImageGeneratorSetting
{
    [Required]
    public string Endpoint { get; set; } = default!;

    [Required]
    public string DeploymentName { get; set; } = default!;

    [Required]
    public string ApiKey { get; set; } = default!;

    [Required]
    public string ApiVersion { get; set; } = default!;

    [Required]
    public string OutputPath { get; set; } = default!;

    // Empty lists allow any path (intended for local use)
    public List<string> InputRoots { get; } = [];

    public List<string> OutputRoots { get; } = [];

    [Range(0, 10)]
    public int MaxRetries { get; set; }

    [Range(1, 60)]
    public int RequestTimeoutMinutes { get; set; }

    [Range(1, 16)]
    public int MaxConcurrency { get; set; }

    [Range(1, 10)]
    public int MaxCount { get; set; }

    [Range(0, 365)]
    public int RetentionDays { get; set; }

    [Required]
    public DefaultEntry Defaults { get; set; } = default!;

    public sealed class DefaultEntry
    {
        [Required]
        public string Size { get; set; } = default!;

        [Required]
        public string Quality { get; set; } = default!;

        [Required]
        public string OutputFormat { get; set; } = default!;

        [Range(0, 100)]
        public int OutputCompression { get; set; }
    }
}
#pragma warning restore CA1034
#pragma warning restore CA1002
