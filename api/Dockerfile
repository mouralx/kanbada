FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY api/Kanbada.Api.csproj api/packages.lock.json ./api/
RUN dotnet restore api/Kanbada.Api.csproj --locked-mode
COPY api/ ./api/
RUN dotnet publish api/Kanbada.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
COPY api/docker-entrypoint.sh /usr/local/bin/kanbada-entrypoint
ENV ASPNETCORE_URLS=http://0.0.0.0:5180
EXPOSE 5180
ENTRYPOINT ["/bin/sh", "/usr/local/bin/kanbada-entrypoint"]
CMD ["dotnet", "Kanbada.Api.dll"]
