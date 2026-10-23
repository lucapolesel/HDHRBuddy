self:
{
  config,
  lib,
  pkgs,
  ...
}:
let
  cfg = config.services.hdhrbuddy;
  stateDir = "/var/lib/hdhrbuddy";
  host = if lib.hasInfix ":" cfg.listenAddress then "[${cfg.listenAddress}]" else cfg.listenAddress;
in
{
  options.services.hdhrbuddy = {
    enable = lib.mkEnableOption "HDHRBuddy, an XMLTV guide server for HDHomeRun tuners";

    package = lib.mkOption {
      type = lib.types.package;
      default = self.packages.${pkgs.stdenv.hostPlatform.system}.default;
      defaultText = lib.literalExpression "hdhrbuddy.packages.\${pkgs.stdenv.hostPlatform.system}.default";
      description = "The HDHRBuddy package to run.";
    };

    tunerAddresses = lib.mkOption {
      type = lib.types.listOf lib.types.str;
      example = [ "192.168.1.50" ];
      description = "IP addresses of the HDHomeRun tuners whose guides are combined.";
    };

    listenAddress = lib.mkOption {
      type = lib.types.str;
      default = "127.0.0.1";
      description = "Address the guide is served on (http://<address>:<port>/xmltv.xml).";
    };

    port = lib.mkOption {
      type = lib.types.port;
      default = 8080;
      description = "Port the guide is served on.";
    };

    openFirewall = lib.mkOption {
      type = lib.types.bool;
      default = false;
      description = "Open the port in the firewall (only useful with a non-loopback listenAddress).";
    };

    minDelayHours = lib.mkOption {
      type = lib.types.either lib.types.int lib.types.float;
      default = 20;
      description = "Shortest randomized delay before the next guide download after one completes.";
    };

    maxDelayHours = lib.mkOption {
      type = lib.types.either lib.types.int lib.types.float;
      default = 28;
      description = "Longest randomized delay before the next guide download after one completes.";
    };
  };

  config = lib.mkIf cfg.enable {
    assertions = [
      {
        assertion = cfg.tunerAddresses != [ ];
        message = "services.hdhrbuddy.tunerAddresses must list at least one tuner.";
      }
    ];

    systemd.services.hdhrbuddy = {
      description = "HDHRBuddy: XMLTV guide from HDHomeRun tuners";
      wantedBy = [ "multi-user.target" ];
      wants = [ "network-online.target" ];
      after = [ "network-online.target" ];

      environment = {
        TUNER_ADDRESSES = lib.concatStringsSep "," cfg.tunerAddresses;
        MIN_DELAY_HOURS = toString cfg.minDelayHours;
        MAX_DELAY_HOURS = toString cfg.maxDelayHours;
        CONFIG_DIR = "${stateDir}/config";
        DATA_DIR = "${stateDir}/data";
        ASPNETCORE_URLS = "http://${host}:${toString cfg.port}";
        DOTNET_NOLOGO = "1";
      };

      serviceConfig = {
        ExecStart = lib.getExe cfg.package;
        Restart = "on-failure";
        RestartSec = 10;

        DynamicUser = true;
        StateDirectory = [
          "hdhrbuddy"
          "hdhrbuddy/config"
          "hdhrbuddy/data"
        ];
        UMask = "0077";

        # .NET JIT-compiles, so MemoryDenyWriteExecute stays off.
        CapabilityBoundingSet = "";
        ProtectSystem = "strict";
        ProtectHome = true;
        PrivateTmp = true;
        PrivateDevices = true;
        ProtectKernelTunables = true;
        ProtectKernelModules = true;
        ProtectKernelLogs = true;
        ProtectControlGroups = true;
        ProtectClock = true;
        ProtectHostname = true;
        ProtectProc = "invisible";
        RestrictNamespaces = true;
        RestrictRealtime = true;
        RestrictSUIDSGID = true;
        LockPersonality = true;
        RestrictAddressFamilies = [
          "AF_INET"
          "AF_INET6"
          "AF_UNIX"
          "AF_NETLINK"
        ];
        SystemCallArchitectures = "native";
        # Not "~@resources": the .NET runtime calls sched_setaffinity at start (SIGSYS otherwise).
        SystemCallFilter = [
          "@system-service"
          "~@privileged"
        ];
      };
    };

    networking.firewall.allowedTCPPorts = lib.mkIf cfg.openFirewall [ cfg.port ];
  };
}
