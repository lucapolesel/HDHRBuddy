using HDHRBuddy.Models;
using System.Text.Json;

namespace HDHRBuddy.Services;

public class GuideRefreshWorker(
    GuideServiceOptions options,
    GuideCache cache,
    TunerClient tunerClient,
    ILogger<GuideRefreshWorker> logger)
    : BackgroundService
{
    /// <summary>
    /// How often to retry when no configured tuner can be reached.
    /// </summary>
    private static readonly TimeSpan TunerRetryDelay = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan[] RetryBackoff =
    [
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(4)
    ];

    private static readonly JsonSerializerOptions StateJsonOptions = new() { WriteIndented = true };

    private GuideState _state = new();

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cache.LoadFromDisk();
        _state = LoadState();

        // Nothing to serve so fetch right away.
        if (!cache.HasData)
        {
            _state.NextRunUtc = null;
            
            logger.LogInformation("No cached guide found. Downloading now..");
        }
        else if (_state.NextRunUtc > DateTime.UtcNow)
        {
            logger.LogInformation(
                "Serving cached guide. Next download scheduled for {Next:u}.",
                _state.NextRunUtc);
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var delay = _state.NextRunUtc.HasValue
                    ? _state.NextRunUtc.Value - DateTime.UtcNow
                    : TimeSpan.Zero;

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }

                await RunOnceAsync(cancellationToken).ConfigureAwait(false);

                SaveState();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            //
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var addresses = options.TunerAddresses
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (addresses.Count == 0)
        {
            logger.LogWarning(
                "No HDHomeRun tuner address configured. Please set `TUNER_ADDRESSES`. Retrying in 5 minutes..");

            _state.NextRunUtc = DateTime.UtcNow.Add(TunerRetryDelay);
            return;
        }

        var devices = new List<Discover>();

        foreach (var address in addresses)
        {
            var result = await tunerClient
                .TryGetDeviceAuthAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (result is not null)
            {
                devices.Add(result);
            }
            else
            {
                logger.LogWarning(
                    "Could not fetch `discover.json` from HDHomeRun tuner at {Address}. Skipping it..",
                    address);
            }
        }

        if (devices.Count == 0)
        {
            logger.LogError("Could not reach any configured HDHomeRun tuner. Retrying in 5 minutes..");

            _state.NextRunUtc = DateTime.UtcNow.Add(TunerRetryDelay);
            return;
        }

        var isPartial = devices.Count < addresses.Count;

        _state.LastRunUtc = DateTime.UtcNow;

        try
        {
            var bytes = await tunerClient
                .DownloadGuideAsync(devices.Select(d => d.DeviceAuth!), cancellationToken)
                .ConfigureAwait(false);

            await cache.SetAsync(bytes, cancellationToken).ConfigureAwait(false);

            _state.LastSuccessUtc = DateTime.UtcNow;

            logger.LogInformation(
                "Downloaded XMLTV guide for tuner(s) {DeviceIDs} ({Size:N0} bytes).",
                string.Join(", ", devices.Select(d => d.DeviceID)),
                bytes.Length);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Failed to download XMLTV guide.");

            ScheduleRetry(giveUpAfterBackoff: false);
            return;
        }

        if (isPartial)
        {
            logger.LogWarning(
                "Guide saved, but only {Reached} of {Configured} tuner(s) were reachable.",
                devices.Count,
                addresses.Count);

            ScheduleRetry(giveUpAfterBackoff: true);
            return;
        }

        _state.ConsecutiveFailures = 0;
        ScheduleNextRandomRun();
    }

    private void ScheduleRetry(bool giveUpAfterBackoff)
    {
        var attempt = _state.ConsecutiveFailures++;

        if (attempt >= RetryBackoff.Length && giveUpAfterBackoff)
        {
            _state.ConsecutiveFailures = 0;

            ScheduleNextRandomRun();
            return;
        }

        var delay = RetryBackoff[Math.Min(attempt, RetryBackoff.Length - 1)];

        _state.NextRunUtc = DateTime.UtcNow.Add(delay);

        logger.LogInformation(
            "Retrying XMLTV guide download at {Next:u} (in {Delay}).",
            _state.NextRunUtc,
            delay);
    }

    private void ScheduleNextRandomRun()
    {
        var min = Math.Min(options.MinDelayHours, options.MaxDelayHours);
        var max = Math.Max(options.MinDelayHours, options.MaxDelayHours);

        if (min <= 0)
        {
            min = 20;
        }

        if (max <= min)
        {
            max = min + 8;
        }

        var delayHours = min + Random.Shared.NextDouble() * (max - min);

        _state.NextRunUtc = DateTime.UtcNow.AddHours(delayHours);

        logger.LogInformation(
            "Next XMLTV guide download scheduled for {Next:u} (~{Hours:F1}h from now).",
            _state.NextRunUtc,
            delayHours);
    }

    private string StatePath => Path.Combine(options.ConfigDirectory, "state.json");

    private GuideState LoadState()
    {
        try
        {
            if (File.Exists(StatePath))
            {
                var json = File.ReadAllText(StatePath);
                var loaded = JsonSerializer.Deserialize<GuideState>(json);

                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not read persisted state from {Path}. Starting fresh..",
                StatePath);
        }

        return new GuideState();
    }

    private void SaveState()
    {
        try
        {
            Directory.CreateDirectory(options.ConfigDirectory);

            var json = JsonSerializer.Serialize(_state, StateJsonOptions);
            var tempPath = StatePath + ".tmp";

            File.WriteAllText(tempPath, json);
            File.Move(tempPath, StatePath, true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not persist state to {Path}.",
                StatePath);
        }
    }
}