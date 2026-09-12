using System.Text;
using HDHRBuddy.Models;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace HDHRBuddy.Services;

public class GuideRefreshWorker(
    GuideServiceOptions options,
    GuideCache cache,
    TunerClient tunerClient,
    ILogger<GuideRefreshWorker> logger)
    : BackgroundService
{
    private GuideState _state = new();

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cache.LoadFromDisk();
        _state = LoadState();
        
        // TODO: Add manual refresh

        while (!cancellationToken.IsCancellationRequested)
        {
            var delay = _state.NextRunUtc.HasValue
                ? _state.NextRunUtc.Value - DateTime.UtcNow
                : TimeSpan.Zero;

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    logger.LogInformation("Refresh triggered manually before its scheduled time.");
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var addresses = options.TunerAddresses
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            
            if (addresses.Count == 0)
            {
                logger.LogWarning(
                    "No HDHomeRun tuner address configured. Please set `TUNER_ADDRESSES`. Retrying in 5 minutes..");
                _state.NextRunUtc = DateTime.UtcNow.AddMinutes(5);
                SaveState();
                continue;
            }
            
            // Parse `discover.json` off each tuner in order to mainly obtain the updated DeviceAuth field
            var deviceDiscovers = new List<Discover>();

            foreach (var address in addresses)
            {
                var result = await tunerClient
                    .TryGetDeviceAuthAsync(address, cancellationToken)
                    .ConfigureAwait(false);

                if (result != null)
                {
                    deviceDiscovers.Add(result);
                }
                else
                {
                    logger.LogWarning(
                        "Could not fetch `discover.json` from HDHomeRun tuner at {Address}. Skipping it..",
                        address);
                }
            }

            _state.LastRunUtc = DateTime.UtcNow;
            
            // If no tuner has been `discovered` then retry in 5 minutes
            if (deviceDiscovers.Count == 0)
            {
                logger.LogError("Could not reach any configured HDHomeRun tuner. Retrying in 5 minutes..");
                _state.NextRunUtc = DateTime.UtcNow.AddMinutes(5);
                SaveState();
                continue;
            }

            try
            {
                var guides = new List<XDocument>();

                // If we found some tuners then obtain their respective XMLTV file
                foreach (var device in deviceDiscovers)
                {
                    try
                    {
                        // Download the XMLTV file
                        var guide = await tunerClient
                            .DownloadGuideAsync(device.DeviceAuth, cancellationToken)
                            .ConfigureAwait(false);
                        
                        // Add it to the list in order to merge it later on
                        guides.Add(guide);
                    
                        logger.LogInformation(
                            "Downloaded XMLTV guide for tuner {DeviceID}.",
                            device.DeviceID);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex,
                            "Could not download XMLTV guide for tuner {DeviceID}",
                            device.DeviceID);
                    }
                }
                
                // Merge each guide
                if (guides.Count == 0)
                {
                    // TODO: Schedule the next attempt in a few hours?
                    throw new InvalidOperationException("No guide were provided.");
                }
                
                // Get the root of the first guide
                var root = guides[0].Root
                    ?? throw new InvalidOperationException("Could not find the first guide's root.");
                
                // Merge the channels
                var channels = guides
                    .SelectMany(d => d.Root?.Elements("channel") ?? [])
                    .GroupBy(c => (string?)c.Attribute("id"))
                    .Select(g => g.First())
                    .ToList();
                
                // Merge the programs
                var programs = guides
                    .SelectMany(d => d.Root?.Elements("programme") ?? [])
                    .GroupBy(p => new
                    {
                        Channel = (string?)p.Attribute("channel"),
                        // I don't think there's any need to parse the date (should have the same timestamp format)
                        Start = (string?)p.Attribute("start")
                    })
                    .Select(g => g.First())
                    .OrderBy(p => (string?)p.Attribute("start"))
                    .ToList();
                
                // Build the merged guide
                var mergedGuide = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    new XElement(
                        root.Name,
                        root.Attributes(),
                        channels,
                        programs
                    )
                );
                
                // Serialize the merged guide to UTF-8 bytes
                var settings = new XmlWriterSettings
                {
                    Encoding = new UTF8Encoding(false),
                    Indent = true,
                    Async = true
                };

                using var output = new MemoryStream();

                await using (var writer = XmlWriter.Create(output, settings))
                {
                    await mergedGuide.SaveAsync(writer, cancellationToken);
                }
                
                // Save it to disk
                await cache.SetAsync(output.ToArray(), cancellationToken);
                
                _state.LastSuccessUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to download XMLTV guide.");
            }
            
            ScheduleNextRandomRun();
            SaveState();
        }
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
            
            var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            
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