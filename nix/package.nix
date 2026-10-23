{
  lib,
  buildDotnetModule,
  dotnetCorePackages,
}:
buildDotnetModule {
  pname = "hdhrbuddy";
  # Keep in step with the release tags (v1.1.1 → 1.1.1).
  version = "1.1.1";

  src = lib.fileset.toSource {
    root = ../.;
    fileset = ../src;
  };

  projectFile = "src/HDHRBuddy/HDHRBuddy.csproj";
  # No PackageReferences: the restore needs nothing beyond the SDK.
  nugetDeps = ./deps.json;

  dotnet-sdk = dotnetCorePackages.sdk_10_0;
  dotnet-runtime = dotnetCorePackages.aspnetcore_10_0;
  executables = [ "HDHRBuddy" ];
  # ASP.NET's content root is the working directory, and it watches it recursively for
  # config changes: started from / (a systemd service), that overflowed the stack. Start in
  # the app's own directory (appsettings.json), and don't watch the read-only store at all.
  makeWrapperArgs = [
    "--chdir"
    "${placeholder "out"}/lib/hdhrbuddy"
    "--set-default"
    "DOTNET_hostBuilder__reloadConfigOnChange"
    "false"
  ];

  meta = {
    description = "Your HDHomeRun guide bud: one XMLTV guide from your HDHomeRun tuners";
    homepage = "https://github.com/lucapolesel/HDHRBuddy";
    license = lib.licenses.mit;
    mainProgram = "HDHRBuddy";
    platforms = lib.platforms.linux;
  };
}
