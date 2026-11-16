FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:0294f285d062afacc8b5f6d7febb0cd4ee879320e5585fa15f2f4ebf1feb9ba7 AS build
ARG TARGETARCH
WORKDIR /src
COPY src/HDHRBuddy/HDHRBuddy.csproj src/HDHRBuddy/
RUN dotnet restore src/HDHRBuddy/HDHRBuddy.csproj -a $TARGETARCH
COPY . .
RUN dotnet publish src/HDHRBuddy/HDHRBuddy.csproj -c Release -a $TARGETARCH --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:21c3f9798416cba4c0576a947c286231cb71ea2884896cb968253f5c9668f930
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /config /data && chown -R $APP_UID /config /data
USER $APP_UID
EXPOSE 8080
VOLUME ["/config", "/data"]
ENTRYPOINT ["dotnet", "HDHRBuddy.dll"]
