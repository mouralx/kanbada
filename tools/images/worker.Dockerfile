FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY api/Kanbada.Api.csproj api/packages.lock.json ./api/
COPY worker/Kanbada.Worker.csproj worker/packages.lock.json ./worker/
RUN dotnet restore worker/Kanbada.Worker.csproj --locked-mode
COPY api/ ./api/
COPY worker/ ./worker/
RUN dotnet publish worker/Kanbada.Worker.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends fonts-dejavu-core && rm -r /var/lib/apt/lists
WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "Kanbada.Worker.dll"]
