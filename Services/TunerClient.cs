using HDHRBuddy.Models;
using System.Net;
using System.Xml.Linq;

namespace HDHRBuddy.Services;

public class TunerClient
{
    private static readonly TimeSpan DiscoverTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GuideDownloadTimeout = TimeSpan.FromMinutes(5);
    
    private readonly HttpClient _client;
    private readonly ILogger<TunerClient> _logger;

    public TunerClient(ILogger<TunerClient> logger)
    {
        _logger = logger;

        _client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            Timeout = GuideDownloadTimeout
        };

        _client.DefaultRequestHeaders.UserAgent.ParseAdd("HDHRBuddy/1.0");
    }

    public async Task<Discover?> TryGetDeviceAuthAsync(
        string address,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DiscoverTimeout);
            
            var discoveryUrl = $"http://{address}/discover.json";

            using var response = await _client
                .GetAsync(discoveryUrl, timeoutCts.Token)
                .ConfigureAwait(false);
            
            response.EnsureSuccessStatusCode();
            
            var deviceDiscover = await response.Content
                .ReadFromJsonAsync<Discover>(timeoutCts.Token)
                .ConfigureAwait(false);
            
            // Validate the required fields
            if (deviceDiscover is null
                || string.IsNullOrWhiteSpace(deviceDiscover.DeviceID)
                || string.IsNullOrWhiteSpace(deviceDiscover.DeviceAuth))
            {
                _logger.LogWarning(
                    "Tuner at {Address} responded but returned no usable DeviceID/DeviceAuth",
                    address);
                return null;
            }
            
            return deviceDiscover;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No usable HDHomeRun tuner at {Address}", address);
        }

        return null;
    }

    public async Task<XDocument> DownloadGuideAsync(string deviceAuth, CancellationToken cancellationToken)
    {
        var url = $"https://api.hdhomerun.com/api/xmltv?DeviceAuth={Uri.EscapeDataString(deviceAuth)}";

        using var response = await _client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        
        return await XDocument
            .LoadAsync(stream, LoadOptions.None, cancellationToken)
            .ConfigureAwait(false);
    }
}