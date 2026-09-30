using HDHRBuddy.Models;
using System.Net;
using System.Xml;
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

        _client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15)
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
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "No usable HDHomeRun tuner at {Address}", address);
        }

        return null;
    }

    public async Task<byte[]> DownloadGuideAsync(IEnumerable<string> deviceAuths, CancellationToken cancellationToken)
    {
        var deviceAuth = string.Concat(deviceAuths);
        var url = $"https://api.hdhomerun.com/api/xmltv?DeviceAuth={Uri.EscapeDataString(deviceAuth)}";

        using var response = await _client
            .GetAsync(url, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var bytes = await response.Content
            .ReadAsByteArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        // Make sure we got an actual XMLTV document before overwriting the last good one
        using var stream = new MemoryStream(bytes, writable: false);

        XDocument document;

        try
        {
            document = XDocument.Load(stream, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("The guide response is not valid XML.", ex);
        }

        if (document.Root?.Name.LocalName != "tv")
        {
            throw new InvalidDataException(
                $"Unexpected guide root element <{document.Root?.Name.LocalName}>, expected <tv>.");
        }

        return bytes;
    }
}