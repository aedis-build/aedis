# syntax=docker/dockerfile:1
# Imagem endurecida (base chiseled non-root). As proteções de runtime (read-only FS,
# drop de capabilities, no-new-privileges, seccomp) ficam no deploy — ver deploy/deployment.yaml.
ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/Aedis.App1.Api/Aedis.App1.Api.csproj
RUN dotnet publish src/Aedis.App1.Api/Aedis.App1.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV DOTNET_EnableDiagnostics=0 \
    ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Aedis.App1.Api.dll"]
