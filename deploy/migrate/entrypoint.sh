#!/bin/sh
set -eu

# Roda os dois bundles em sequência, um por vez, no mesmo container —
# cada um recebe ConnectionStrings__Postgres com o valor do seu próprio
# banco (buteco_agents para apps/api, buteco_inbox para apps/inbox). Os
# dois valores nunca coexistem na mesma invocação porque --connection não
# funciona neste app (ver comentário no Dockerfile) — a env var é
# reatribuída entre as duas chamadas, não passada por flag.
#
# API_DB_CONNECTION_STRING / INBOX_DB_CONNECTION_STRING são obrigatórias
# (nomes distintos de ConnectionStrings__Postgres de propósito, pra não
# colidir com a env var que apps/api/apps/inbox usam em si).
#
# Achado empírico: o bundle de apps/inbox também precisa de
# API_BASE_URL — Program.cs de apps/inbox valida
# Api:BaseUrl de forma eager (`?? throw`) antes mesmo de o EF conseguir
# descobrir o DbContext, mesmo padrão do ConnectionStrings:Postgres. Não é
# usado pela migration em si, só precisa estar presente e não-vazio; usar
# o mesmo valor real do serviço apps/inbox no compose (nome do serviço
# apps/api na rede), não um placeholder.

: "${API_DB_CONNECTION_STRING:?API_DB_CONNECTION_STRING não configurada}"
: "${INBOX_DB_CONNECTION_STRING:?INBOX_DB_CONNECTION_STRING não configurada}"
: "${API_BASE_URL:?API_BASE_URL não configurada}"

echo "[migrate] Aplicando migrations de apps/api..."
ConnectionStrings__Postgres="$API_DB_CONNECTION_STRING" ./api-migrate

echo "[migrate] Aplicando migrations de apps/inbox..."
ConnectionStrings__Postgres="$INBOX_DB_CONNECTION_STRING" Api__BaseUrl="$API_BASE_URL" ./inbox-migrate

echo "[migrate] Migrations aplicadas com sucesso."
