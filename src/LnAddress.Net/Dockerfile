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

ENTRYPOINT ["dotnet", "LnAddress.Net.dll"]
