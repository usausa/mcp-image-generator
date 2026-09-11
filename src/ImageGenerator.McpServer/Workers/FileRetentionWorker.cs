namespace ImageGenerator.McpServer.Workers;

using ImageGenerator.McpServer.Services;

// 既定の出力ディレクトリ内の古いファイルを定期的に削除する。outputPathで明示指定された保存先は対象外
public sealed class FileRetentionWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly ILogger<FileRetentionWorker> logger;

    private readonly ImageGeneratorSetting setting;

    private readonly ImagePathService paths;

    private readonly TimeProvider timeProvider;

    public FileRetentionWorker(
        ILogger<FileRetentionWorker> logger,
        ImageGeneratorSetting setting,
        ImagePathService paths,
        TimeProvider timeProvider)
    {
        this.logger = logger;
        this.setting = setting;
        this.paths = paths;
        this.timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (setting.RetentionDays <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval, timeProvider);
        try
        {
            do
            {
                var cutoff = timeProvider.GetLocalNow().AddDays(-setting.RetentionDays).DateTime;
                var deleted = Cleanup(paths.OutputRoot, cutoff);
                if (deleted > 0)
                {
                    logger.InfoRetentionCompleted(deleted, setting.RetentionDays);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Shutdown
        }
    }

    private int Cleanup(string directory, DateTime cutoff)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (File.GetLastWriteTime(file) >= cutoff)
            {
                continue;
            }

            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (IOException e)
            {
                logger.WarnRetentionDeleteFailed(file, e.Message);
            }
            catch (UnauthorizedAccessException e)
            {
                logger.WarnRetentionDeleteFailed(file, e.Message);
            }
        }

        return deleted;
    }
}
