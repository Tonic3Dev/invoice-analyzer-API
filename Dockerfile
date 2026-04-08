FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

RUN apt-get update && \
    apt-get install -y apt-utils libgdiplus libc6-dev && \
    rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["bringeri-api/bringeri-api.csproj", "bringeri-api/"]
RUN dotnet restore "bringeri-api/bringeri-api.csproj"

COPY . .
WORKDIR "/src/bringeri-api"
RUN dotnet build "bringeri-api.csproj" -c Release --no-restore -o /app/build

FROM build AS publish
RUN dotnet publish "bringeri-api.csproj" -c Release --no-restore -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "bringeri-api.dll"]
