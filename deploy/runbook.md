# Runbook de deploy — stack de servidor

Este documento cobre como subir e atualizar `docker-compose.prod.yml`
numa VM na cloud. Para o desenho e as decisões por trás disso, ver
`openspec/changes/containerizacao-stack-servidor/design.md`.

> **Testando localmente com o compose de dev também rodando?** O nome de
> projeto do compose (Docker/Podman) é derivado do nome do diretório por
> padrão — `docker-compose.yml` (dev) e `docker-compose.prod.yml`, rodados
> da mesma pasta sem `-p`, geram os MESMOS nomes de container
> (`buteco-agents_postgres_1` etc.) e colidem: subir o prod pode parar/
> recriar os containers do dev (achado real durante a verificação desta
> change — os volumes de dev não foram perdidos, só os containers foram
> recriados apontando pro volume errado). Sempre use um nome de projeto
> isolado ao testar localmente: `docker compose -p buteco-prod-verify -f
> docker-compose.prod.yml ...`. Numa VM de servidor de verdade, sem o
> compose de dev rodando ao lado, isso não se aplica.

> **Testando com `podman`/`podman-compose` em vez de `docker`?**
> Confirmado empiricamente: `podman build`/`podman-compose build` geram
> imagem em formato OCI por padrão, que **ignora silenciosamente** a
> instrução `HEALTHCHECK` (aviso explícito: "HEALTHCHECK is not
> supported for OCI image format and will be ignored"). O mecanismo
> funciona de verdade (`podman build --format docker` embute e executa o
> healthcheck normalmente, verificado rodando o container e observando
> `State.Health` real) — é só uma particularidade do formato default do
> podman. `docker build`/`docker compose` (o alvo real de produção) usam
> o formato nativo e não têm esse problema.

## 1. Primeiro deploy (VM do zero)

1. Copie `.env.prod.example` para `.env.prod` na VM e preencha todos os
   valores `changeme` (ver seção "Variáveis por processo" abaixo). Nunca
   versionar `.env.prod`.
2. `docker compose -f docker-compose.prod.yml up -d`
   - `postgres`/`rabbitmq` sobem primeiro (healthcheck).
   - `migrator` roda depois de `postgres` saudável, aplica as migrations
     de `apps/api` e `apps/inbox` (cria `buteco_agents`/`buteco_inbox` se
     não existirem), e termina.
   - `apps/api`/`apps/inbox`/`apps/workers` sobem só depois do
     `migrator` completar com sucesso (`depends_on:
     service_completed_successfully`).
   - `frontend` (nginx do stack) sobe por último, publicando só a porta
     interna do host (`STACK_HTTP_PORT`, nunca 80/443).
3. Confirme os healthchecks: `docker compose -f docker-compose.prod.yml
   ps` — todos os serviços de longa duração devem estar `healthy`/`Up`.
4. Configure o nginx externo (fora deste stack, já existente) pra fazer
   `proxy_pass` pra `127.0.0.1:${STACK_HTTP_PORT}`.
5. Verificação manual (não automatizada — ver Non-Goals): acesse o
   domínio público, confirme o SPA carrega, faça login, e rode um
   round-trip real de webhook → resposta no canal.

## 2. Redeploy (atualização de imagem em produção)

**A sequência é a garantia, não um detalhe.** O índice único parcial de
`Session` (`sessions."ContactId" WHERE "ClosedAt" IS NULL`,
`inbox-session-indice-unico`) rejeita a rotação normal de sessão por
inatividade se o código antigo de `apps/inbox` continuar respondendo
tráfego enquanto uma migration nova já rodou contra o banco —
`DbUpdateException` não tratada no caminho mais comum do app (recepção de
webhook). `depends_on: service_completed_successfully` cobre só o
primeiro boot do stack a partir do zero — **não** cobre um redeploy, que
precisa ser feito manualmente nesta ordem:

```
1. docker compose -f docker-compose.prod.yml build          # rebuild das imagens alteradas
2. docker compose -f docker-compose.prod.yml stop inbox      # para apps/inbox ANTES de migrar
3. docker compose -f docker-compose.prod.yml run --rm migrator
4. docker compose -f docker-compose.prod.yml up -d inbox api workers frontend
```

`apps/workers`/`apps/api`/`frontend` não têm essa restrição — só
`apps/inbox` precisa estar parado durante a migration, porque só ele tem
o índice único envolvido nesse caminho.

## 3. Variáveis por processo

| Variável (host, `.env.prod`) | Chave de configuração | Processo(s) | Obrigatória | Idêntica entre processos? |
|---|---|---|---|---|
| `POSTGRES_USER`/`POSTGRES_PASSWORD` | (usada só na connection string, montada por serviço) | postgres, migrator, api, inbox, workers | sim | N/A — mesmo servidor, `Database=` muda por app |
| `RABBITMQ_USER`/`RABBITMQ_PASSWORD` | (usada só na connection string) | rabbitmq, api, workers | sim | N/A |
| `PUBLIC_DOMAIN` | `PublicUrl__BaseUrl` | api (externa — `AgentCard.SupportedInterfaces`), inbox (D8 — mesmo domínio, ver nota abaixo) | sim | mesmo valor nos dois, mas por motivos diferentes (não é o mesmo requisito, é coincidência desta change) |
| `TZ` | `TZ` (env var direta, não `IConfiguration`) | workers | sim, fail-fast | N/A |
| `OPENAI_BASE_URL`/`OPENAI_API_KEY` | `OpenAI__BaseUrl`/`OpenAI__ApiKey` | api (só checa presença), workers (usa de verdade) | sim (workers) | precisa ser a mesma chave configurada nos dois |
| `MCP_CREDENTIAL_ENCRYPTION_KEY` | `Mcp__CredentialEncryptionKey` | api (cifra), workers (decifra) | sim | **sim, byte-idêntica** |
| `INBOX_CREDENTIAL_ENCRYPTION_KEY` | `Inbox__CredentialEncryptionKey` | inbox | sim | N/A — só um processo |
| `AUTH_TOKEN_SIGNING_KEY` | `Auth__TokenSigningKey` | api, inbox | sim, fail-fast | **sim, byte-idêntica** |
| `AUTH_OPERATOR_USERNAME`/`AUTH_OPERATOR_PASSWORD_HASH` | `Auth__OperatorUsername`/`Auth__OperatorPasswordHash` | api | sim, fail-fast | N/A |
| `STACK_HTTP_PORT` | (porta do host, não config do app) | frontend (nginx do stack) | sim | N/A |

O compose garante os valores idênticos via interpolação `${VAR}` — a
mesma variável do host referenciada nos dois serviços, nunca copiada à
mão (`docker-compose.prod.yml`, design.md D7).

### Risco aceito: placeholder `changeme` não é rejeitado no boot

Nenhuma das chaves acima rejeita especificamente o valor `changeme*` do
`.env.prod.example` — a checagem de startup existente só cobre
ausência/vazio (`Auth__TokenSigningKey`, `Auth__OperatorUsername`/
`PasswordHash`), não o conteúdo. Se `.env.prod` for copiado sem editar,
o stack sobe normalmente com segredos públicos e previsíveis (estão no
repo). **Confira manualmente, antes de cada deploy, que nenhum valor em
`.env.prod` começa com `changeme`** — decisão consciente de escopo desta
change, não mitigada em código.

### Item futuro: `PublicUrl__BaseUrl` de `apps/inbox` com semântica dupla

Hoje `apps/inbox` usa o mesmo `PUBLIC_DOMAIN` de `apps/api`, porque o
requisito mais restritivo (o `setWebhook` do Telegram, que exige URL
pública HTTPS) domina. Na prática, isso faz `apps/workers` entregar push
notification pra `apps/inbox` saindo pela internet pública e voltando
pelo mesmo domínio, em vez de usar o nome de serviço interno
(`http://inbox:8080`) — funciona, mas depende de egress/DNS/Cloudflare
saudáveis para uma comunicação que poderia ser interna. Corrigir isso
exigiria separar `PublicUrl__BaseUrl` em duas variáveis (uma pro
provisionamento de webhook externo, outra pra entrega de push
notification interna) — reconhecido como a correção certa, mas **não
implementado nesta change**. Gatilho: latência perceptível ou instância
de falha de entrega de push notification atribuível a esse ir-e-volta.

## 4. Non-Goals explícitos desta change

- **CI/CD** — build/push de imagem é manual.
- **Orquestrador além do compose** (Kubernetes, Swarm, etc.).
- **Observabilidade/métricas** (Prometheus, dashboards, etc.).
- **Múltiplas réplicas de `apps/workers`/`apps/inbox`** — o lock hoje é
  `pg_advisory_lock`/`xmin`, não distribuído. Nunca usar `replicas > 1`
  pra esses dois serviços neste compose sem resolver isso primeiro.
- **TLS/certificado** — resolvido por um nginx/Cloudflare já existentes,
  fora deste stack. `docker-compose.prod.yml` nunca publica 80/443
  diretamente.
- **WAHA no compose** — instância externa já existente, tratada como
  dependência, não como serviço deste stack.
- **Correção do setup desprotegido de `TaskJobConsumer`** (RabbitMQ) —
  item em aberto já registrado em `02-HISTORICO_E_STATUS.md`; deve virar
  change própria sequenciada antes desta. Esta change assume que ela já
  rodou.
