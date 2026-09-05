FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

COPY ["Eris.Api/Eris.Api.csproj", "Eris.Api/"]
COPY ["Eris.ServiceDefaults/Eris.ServiceDefaults.csproj", "Eris.ServiceDefaults/"]

RUN dotnet restore "Eris.Api/Eris.Api.csproj"

COPY . .
RUN dotnet publish "Eris.Api/Eris.Api.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app

# curl is used by container health checks.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=builder /app/publish .

# Keep the SQLite data mount path present.
RUN mkdir -p /data

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Eris.Api.dll"]
