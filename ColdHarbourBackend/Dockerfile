FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore ColdHarbour.slnx
RUN dotnet publish src/ColdHarbour.Api/ColdHarbour.Api.csproj -c Release -o /app --no-restore

FROM base AS final
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "ColdHarbour.Api.dll"]
