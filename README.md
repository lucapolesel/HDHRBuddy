# HDHRBuddy

HDHRBuddy is a small service that pulls the XMLTV guide data for your HDHomeRun
tuners from SiliconDust's API and merges it into a single file, served over HTTP
or readable directly from disk, so Jellyfin (or anything else that accepts XMLTV)
can fetch it without relying on plugins.

## Docker compose example

```yaml
services:
  hdhrbuddy:
    image: ghcr.io/lucapolesel/hdhrbuddy:latest
    container_name: hdhrbuddy
    restart: unless-stopped
    environment:
      TUNER_ADDRESSES: 192.168.0.121
    ports:
      - "8080:8080"
    volumes:
      - hdhrbuddy-config:/config
      - ./guide:/data

volumes:
  hdhrbuddy-config:
```

## Configuration

| Variable | Required | Default | Description |
|---|:---:|---|---|
| `TUNER_ADDRESSES` | ✅ | *(none)* | Comma-separated tuner IPs or hostnames, e.g. `192.168.0.121,192.168.0.122`. |
| `MIN_DELAY_HOURS` | ❌ | `20` | Lower bound of the randomized refresh interval. [[1]](#notes) |
| `MAX_DELAY_HOURS` | ❌ | `28` | Upper bound of the randomized refresh interval. [[1]](#notes) |
| `CONFIG_DIR` | ❌ | `/config` | Where the refresh schedule (`state.json`) is kept. |
| `DATA_DIR` | ❌ | `/data` | Where the merged guide (`xmltv.xml`) is written. |

## Endpoints

| Route | Description |
|---|---|
| `GET /xmltv.xml` | The merged guide. Returns `503` until the first download completes. |

## Notes

1. These defaults come straight from SiliconDust, who ask that downloads aren't
scheduled at a fixed time each day and suggest a randomized 20-28 hour interval.
See [XMLTV Guide Data](https://github.com/Silicondust/documentation/wiki/XMLTV-Guide-Data).
You can change them, but there's little to gain since guide data only updates a few
times a day.

## License

[MIT](LICENSE)
