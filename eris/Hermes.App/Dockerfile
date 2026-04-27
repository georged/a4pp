FROM mcr.microsoft.com/dotnet/sdk:9.0 AS builder

WORKDIR /src

# Copy solution and project files first for better layer caching.
COPY ["Eris.slnx", "."]
COPY ["Eris.Api/Eris.Api.csproj", "Eris.Api/"]
COPY ["Eris.ServiceDefaults/Eris.ServiceDefaults.csproj", "Eris.ServiceDefaults/"]
COPY ["Hermes.App/Hermes.App.csproj", "Hermes.App/"]
COPY ["Eris.AppHost/Eris.AppHost.csproj", "Eris.AppHost/"]

RUN dotnet restore "Eris.slnx"

# Copy all source and publish Hermes.App.
COPY . .
RUN dotnet publish "Hermes.App/Hermes.App.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0

WORKDIR /app

# curl is used by container health checks.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=builder /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:8080/api/config || exit 1

ENTRYPOINT ["dotnet", "Hermes.App.dll"]
