namespace HDHRBuddy.Services;

public class GuideCache(GuideServiceOptions options, ILogger<GuideCache> logger)
{
    private const string FileName = "xmltv.xml";

    private readonly Lock _lock = new();
    private readonly string _dataDirectory = options.DataDirectory;

    private byte[]? _data;
    private DateTimeOffset? _lastModified;

    public void LoadFromDisk()
    {
        var filePath = Path.Combine(_dataDirectory, FileName);

        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            var bytes = File.ReadAllBytes(filePath);
            var lastModified = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath), TimeSpan.Zero);

            lock (_lock)
            {
                _data = bytes;
                _lastModified = lastModified;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read the cached guide from {Path}.", filePath);
        }
    }

    public async Task SetAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _data = bytes;

            var now = DateTimeOffset.UtcNow;

            _lastModified = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
        }

        var finalPath = Path.Combine(_dataDirectory, FileName);
        var tempPath = finalPath + ".tmp";

        try
        {
            Directory.CreateDirectory(_dataDirectory);

            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken)
                .ConfigureAwait(false);

            File.Move(tempPath, finalPath, true);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex,
                "Could not write the guide to {Path}. It's still served over HTTP. " +
                "If you are using a bind mount, make sure it's writable by the container user (UID {Uid}).",
                finalPath,
                Environment.GetEnvironmentVariable("APP_UID") ?? "1654");
        }
    }

    public (byte[]?, DateTimeOffset? LastModified) Get()
    {
        lock (_lock)
        {
            return (_data, _lastModified);
        }
    }

    public bool HasData
    {
        get
        {
            lock (_lock)
            {
                return _data is not null;
            }
        }
    }
}