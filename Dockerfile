FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:496914451b05ac5a24592024362ec370c3a2d3c3fe817f864e4aa55c4c194a56 AS build
ARG TARGETARCH
WORKDIR /src
COPY src/HDHRBuddy/HDHRBuddy.csproj src/HDHRBuddy/
RUN dotnet restore src/HDHRBuddy/HDHRBuddy.csproj -a $TARGETARCH
COPY . .
RUN dotnet publish src/HDHRBuddy/HDHRBuddy.csproj -c Release -a $TARGETARCH --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:ecf8dcbaab8ebc4bdfb2322d6fe4217bbb118894310ebb6d53c580874ab4b27d
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /config /data && chown -R $APP_UID /config /data
USER $APP_UID
EXPOSE 8080
VOLUME ["/config", "/data"]
ENTRYPOINT ["dotnet", "HDHRBuddy.dll"]
