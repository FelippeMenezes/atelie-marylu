#!/usr/bin/env bash
# Backup diário do banco e das fotos dos produtos.
# Uso (na pasta do projeto, no servidor): ./deploy/backup.sh
# Agendado pelo cron: veja docs/DEPLOY.md.
#
# Guarda os últimos 14 dias em ./backups. Se a variável RCLONE_DESTINO estiver definida
# (ex.: "oci:atelie-backups"), copia também para fora do servidor com o rclone:
# backup que fica só na mesma máquina não protege contra perder a máquina.
set -euo pipefail
umask 077   # arquivos de backup legíveis só pelo dono (têm dados pessoais de clientes)

cd "$(dirname "$0")/.."
ARQUIVO_ENV="${ARQUIVO_ENV:-.env.producao}"
COMPOSE=(docker compose -f docker-compose.prod.yml --env-file "$ARQUIVO_ENV")
PASTA="backups"
DATA="$(date +%Y-%m-%d_%H%M)"
DIAS=14

mkdir -p "$PASTA"
chmod 700 "$PASTA"   # backup tem dados pessoais de clientes: só o dono lê

# Banco: formato "custom" do pg_dump (já comprimido; restaura com pg_restore)
"${COMPOSE[@]}" exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' > "$PASTA/banco_$DATA.dump"

# Fotos: copia o volume via container temporário, sem parar a loja
"${COMPOSE[@]}" run --rm --no-deps -T --entrypoint tar app -czf - -C /app/wwwroot uploads > "$PASTA/fotos_$DATA.tar.gz"

# Um backup vazio indica falha silenciosa
for arquivo in "$PASTA/banco_$DATA.dump" "$PASTA/fotos_$DATA.tar.gz"; do
    if [ ! -s "$arquivo" ]; then
        echo "ERRO: $arquivo ficou vazio" >&2
        exit 1
    fi
done

find "$PASTA" -name 'banco_*.dump' -mtime +$DIAS -delete
find "$PASTA" -name 'fotos_*.tar.gz' -mtime +$DIAS -delete

if [ -n "${RCLONE_DESTINO:-}" ]; then
    rclone copy "$PASTA" "$RCLONE_DESTINO" --max-age 2d
fi

echo "Backup concluído: $PASTA/banco_$DATA.dump e $PASTA/fotos_$DATA.tar.gz"
