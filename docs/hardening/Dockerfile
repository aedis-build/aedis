# syntax=docker/dockerfile:1
# Baseline de imagem endurecida para um serviço Aedis (API ou worker).
# Substitua MyAedisApp pelo nome do seu projeto/assembly.
#
# Hardening embutido aqui: base chiseled (sem shell/gerenciador de pacotes), usuário non-root,
# diagnósticos do runtime desligados. As proteções que dependem do runtime (read-only FS, drop de
# capabilities, no-new-privileges, seccomp) são aplicadas no deploy — ver pod-security.yaml.

ARG DOTNET_VERSION=10.0

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app --no-restore

# ---- runtime (chiseled: Ubuntu mínima, non-root por padrão, sem shell) ----
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app .

# Reforça a execução como usuário non-root (a imagem chiseled já define $APP_UID).
USER $APP_UID

# Desliga o socket de diagnóstico do runtime e fixa a porta non-privileged.
ENV DOTNET_EnableDiagnostics=0 \
    ASPNETCORE_URLS=http://+:8080

EXPOSE 8080
ENTRYPOINT ["dotnet", "MyAedisApp.dll"]
