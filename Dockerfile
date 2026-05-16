FROM mcr.microsoft.com/dotnet/sdk:10.0.107 AS build
WORKDIR /src
COPY src/IpamService/IpamService.csproj .
RUN dotnet restore
COPY src/IpamService/ .
RUN dotnet build IpamService.csproj -c Release --no-restore

FROM build AS publish
RUN dotnet publish IpamService.csproj -c Release -o /app/publish --no-build

FROM mcr.microsoft.com/dotnet/aspnet:10.0.8 AS runtime
WORKDIR /app
RUN useradd --no-create-home --shell /bin/false appuser && chown -R appuser /app
COPY --from=publish /app/publish .
USER appuser
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "IpamService.dll"]
