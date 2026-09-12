FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG TARGETARCH
WORKDIR /src
COPY HDHRBuddy.csproj .
RUN dotnet restore -a $TARGETARCH
COPY . .
RUN dotnet publish -c Release -a $TARGETARCH --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /config /data && chown -R $APP_UID /config /data
USER $APP_UID
EXPOSE 8080
VOLUME ["/config", "/data"]
ENTRYPOINT ["dotnet", "HDHRBuddy.dll"]
