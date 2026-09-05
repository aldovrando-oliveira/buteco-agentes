# syntax=docker/dockerfile:1
#
# Build context: raiz do monorepo, por consistência com os outros três
# Dockerfiles (não é estritamente necessário aqui — apps/frontend não tem
# dependência de raiz — mas simplifica o comando de build/CI). Buildar
# com:
#   docker build -f apps/frontend/Dockerfile \
#     --build-arg VITE_API_BASE_URL= --build-arg VITE_INBOX_BASE_URL= .

FROM node:24-alpine AS build
WORKDIR /src/apps/frontend

COPY apps/frontend/package.json apps/frontend/package-lock.json ./
RUN npm ci

COPY apps/frontend/ ./

# VITE_* são build-time (substituídas no bundle, não lidas em runtime) —
# import.meta.env.VITE_X usa `??`, que só cai no default
# (http://localhost:5017/5027) quando a var é `undefined`, não quando é
# string vazia. Sem ARG explícito aqui, o build usaria o default de dev
# silenciosamente, e o defeito só apareceria no browser do usuário final
# (design.md, D2). ARG sem default: falha cedo se `--build-arg` não for
# passado.
ARG VITE_API_BASE_URL
ARG VITE_INBOX_BASE_URL
RUN test -n "${VITE_API_BASE_URL+x}" || (echo "VITE_API_BASE_URL não foi passado ao build (use --build-arg, mesmo que vazio)" && exit 1)
RUN test -n "${VITE_INBOX_BASE_URL+x}" || (echo "VITE_INBOX_BASE_URL não foi passado ao build (use --build-arg, mesmo que vazio)" && exit 1)
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}
ENV VITE_INBOX_BASE_URL=${VITE_INBOX_BASE_URL}

RUN npm run build

# Imagem final — nginx estável corrente (verificado, não assumido:
# 1.30.4 no momento desta change). Alpine aqui não tem o mesmo risco de
# tzdata/ICU do D4: nginx não depende de tz database nem de globalização
# do .NET.
FROM nginx:1.30-alpine AS final

COPY --from=build /src/apps/frontend/dist /usr/share/nginx/html
COPY apps/frontend/deploy/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
# wget do busybox já vem na imagem nginx:alpine (confirmado
# empiricamente) — sem precisar instalar nada.
HEALTHCHECK --interval=10s --timeout=3s --start-period=5s --retries=3 \
    CMD wget -q --spider http://localhost/ || exit 1
