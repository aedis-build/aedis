# syntax=docker/dockerfile:1
# Imagem endurecida (base chiseled non-root) para o worker. O endpoint de health usa o runtime ASP.NET.
# As proteções de runtime ficam no deploy — ver deploy/deployment.yaml.
ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/Aedis.App1.Worker/Aedis.App1.Worker.csproj --locked-mode
RUN dotnet publish src/Aedis.App1.Worker/Aedis.App1.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-noble-chiseled@sha256:84def277556c2843a44fdeefbd3145d0a2e32d8f736d0c1a7b0a96b2880b0187 AS final
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV DOTNET_EnableDiagnostics=0 \
    ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Aedis.App1.Worker.dll"]
