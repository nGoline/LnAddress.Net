FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

COPY ["LnAddress.Net.csproj", "LnAddress.Net.csproj"]
RUN dotnet restore "LnAddress.Net.csproj" -a $TARGETARCH --no-cache

COPY . .
RUN dotnet publish "LnAddress.Net.csproj" -c Release -a $TARGETARCH --self-contained false --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app

COPY --from=build --chown=app:app /app/publish .

USER $APP_UID

# The aspnet alpine image has busybox wget but no curl
HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
    CMD wget -qO /dev/null "http://127.0.0.1:${ASPNETCORE_HTTP_PORTS:-8080}/health" || exit 1

ENTRYPOINT ["dotnet", "LnAddress.Net.dll"]
