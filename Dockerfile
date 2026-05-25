FROM node:20-alpine AS node-deps
WORKDIR /src/frontend
COPY frontend/package.json frontend/pnpm-lock.yaml ./
RUN corepack enable pnpm && pnpm install --frozen-lockfile

FROM node-deps AS node-build
COPY frontend/ ./
RUN pnpm build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/IpamService.slnx .
COPY backend/src/IpamService.csproj .
RUN dotnet restore IpamService.csproj
COPY backend/src/ .
RUN dotnet build IpamService.csproj -c Release --no-restore

FROM build AS publish
RUN dotnet publish IpamService.csproj -c Release -o /app/publish --no-build

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN useradd --no-create-home --shell /bin/false appuser \
    && mkdir /data \
    && chown appuser /app /data
COPY --from=publish /app/publish .
COPY --from=node-build /src/frontend/dist ./wwwroot/
USER appuser
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "IpamService.dll"]
