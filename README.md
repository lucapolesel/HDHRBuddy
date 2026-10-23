# HDHRBuddy

HDHRBuddy is a small service that pulls the combined XMLTV guide data for all your
HDHomeRun tuners from SiliconDust's API as a single file, served over HTTP
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

## NixOS

The repository is a flake with a package and a NixOS module:

```nix
{
  inputs.hdhrbuddy = {
    url = "github:lucapolesel/HDHRBuddy";
    inputs.nixpkgs.follows = "nixpkgs";
  };

  # in a NixOS configuration (imports = [ inputs.hdhrbuddy.nixosModules.default ]):
  services.hdhrbuddy = {
    enable = true;
    tunerAddresses = [ "192.168.0.121" ];
    # listenAddress = "127.0.0.1"; port = 8080; openFirewall = false;
  };
}
```

It runs as a sandboxed service with its state in `/var/lib/hdhrbuddy`; the guide is at
`http://127.0.0.1:8080/xmltv.xml`. `nix run github:lucapolesel/HDHRBuddy` runs it directly,
configured with the variables below.

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
| `GET /xmltv.xml` | The combined guide. Returns `503` until the first download completes. |

## Notes

1. These defaults come straight from SiliconDust, who ask that downloads aren't
scheduled at a fixed time each day and suggest a randomized 20-28 hour interval.
See [XMLTV Guide Data](https://github.com/Silicondust/documentation/wiki/XMLTV-Guide-Data).
You can change them, but there's little to gain since guide data only updates a few
times a day.

## License

[MIT](LICENSE)
