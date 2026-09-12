namespace HDHRBuddy.Services;

public class GuideCache(GuideServiceOptions options, ILogger<GuideCache> logger)
{
    private const string FileName = "xmltv.xml";
    
    private readonly Lock _lock = new();
    private readonly string _dataDirectory = options.DataDirectory;

    private byte[]? _data;
    
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

            lock (_lock)
            {
                _data = bytes;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read the cached guide from {Path}.", filePath);
        }
    }

    public async Task SetAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_dataDirectory);

        var finalPath = Path.Combine(_dataDirectory, FileName);
        var tempPath = finalPath + ".tmp";

        await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);

        File.Move(tempPath, finalPath, true);

        lock (_lock)
        {
            _data = bytes;
        }
    }

    public byte[]? Get()
    {
        lock (_lock)
        {
            return _data;
        }
    }
}