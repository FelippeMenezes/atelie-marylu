# Imagem da loja. O build roda na arquitetura da máquina que compila e gera a imagem para a
# arquitetura de destino (ex.: compilar num PC x64 para o servidor ARM da Oracle):
#   docker buildx build --platform linux/arm64 -t atelie-marylu .
# No próprio servidor, o docker compose de produção já faz o build: docker compose -f docker-compose.prod.yml up -d --build

# ---- Build ----
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Copia só os .csproj primeiro: o restore fica em cache enquanto as dependências não mudarem
COPY src/AtelieMarylu.Domain/AtelieMarylu.Domain.csproj src/AtelieMarylu.Domain/
COPY src/AtelieMarylu.Web/AtelieMarylu.Web.csproj src/AtelieMarylu.Web/
RUN dotnet restore src/AtelieMarylu.Web/AtelieMarylu.Web.csproj -a $TARGETARCH

COPY src/ src/
# Sem --no-restore: com ele, os arquivos do framework (_framework/blazor.web.js) ficavam fora da
# publicação e o site perdia a interatividade. O restore aqui é rápido, pois reaproveita o de cima.
RUN dotnet publish src/AtelieMarylu.Web/AtelieMarylu.Web.csproj -c Release -a $TARGETARCH -o /app

# ---- Execução ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Pasta das fotos dos produtos (vira volume no compose). Criada aqui para pertencer ao usuário "app",
# que não é root: se alguém explorar uma falha na aplicação, não ganha controle do container.
RUN mkdir -p /app/wwwroot/uploads && chown -R $APP_UID /app/wwwroot/uploads
USER $APP_UID

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8080

ENTRYPOINT ["dotnet", "AtelieMarylu.Web.dll"]
