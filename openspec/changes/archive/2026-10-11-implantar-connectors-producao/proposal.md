**Issue:** #119

## Why

O `apps/connectors` existe desde a #103, mas o stack de produção não o sobe: a
implantação foi adiada (D10 da `apps-connectors-google-drive`) até haver
consumidor e até o mantenedor decidir sobre a #117. As duas condições estão
cumpridas. A #104 (validação de pasta no `apps/api`), a #105 (ciclo de
sincronização), a #106 (cadastro no painel) e a #138 (pedido de indexação
durável, que bloqueava o ciclo em produção) estão na `main`. O mantenedor decidiu
implantar com a chave de assinatura compartilhada e aceitar o risco da #117. Sem
esta change, a origem "Sincronizada" continua indisponível no painel de produção,
e nenhuma base sincronizada é criada nem atualizada ali.

## What Changes

- **Serviço `connectors` no `docker-compose.prod.yml`**, construído de
  `apps/connectors/Dockerfile`, sem porta publicada, com o `HEALTHCHECK` da
  imagem, subindo depois do `apps/api` saudável, e com **uma réplica só**: o lock
  do ciclo é em memória (#105).
- **Configuração entregue pelo compose**:
  - `apps/connectors`: `Auth__TokenSigningKey` pela mesma variável do host que o
    `apps/api` e o `apps/inbox` já usam; `GoogleDrive__ServiceAccountKeyBase64`
    **opcional** (sem ela o provedor não aparece); `Api__BaseUrl` fixo, apontando
    para o `apps/api` pela rede interna;
  - `apps/api`: `Connectors__BaseUrl` fixo, apontando para o `apps/connectors`
    pela rede interna;
  - `apps/frontend`: build arg `VITE_CONNECTORS_BASE_URL: ""`, **junto** com o
    bloco do nginx, nunca antes.
- **`.env.prod.example`**: variável nova da chave do Google, comentada, com o
  comando de geração; o comentário da chave de assinatura passa a nomear os três
  processos.
- **Nginx do stack**: prefixo `/connectors` roteado para o `apps/connectors`, com
  o mesmo desvio de navegação (`Sec-Fetch-Mode: navigate` cai no shell do SPA) e
  o mesmo `resolver` dos outros blocos. Limite de tempo do proxy mantido no
  padrão, conferido contra os limites dos apps (`design.md`).
- **`docs/deployment.md`**: variáveis por processo, pré-requisitos do Google
  medidos na etapa 0, `connectors` na sequência de redeploy e na lista de
  serviços que nunca usam `replicas > 1`, a contagem de documentos `Pending`
  antes da migração da #138, verificação pós-deploy do prefixo, da troca de
  token com o Google de dentro do contêiner, do cadastro sincronizado e do
  ciclo, e o roteiro para `access-denied` em pasta de organização (#114).
- **Risco aceito da #117** registrado no `docs/deployment.md` e em comentário na
  própria #117, que continua aberta com `aguardando gatilho`.
- **`docs/configuration.md`**, seção do stack de servidor; `02` e `CHANGELOG.md`.

Nenhum código de app muda. Nenhum servidor é tocado: o deploy é do mantenedor.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `server-deployment`: o compose de servidor passa a ter cinco apps (oito
  serviços), com o `apps/connectors` em réplica única; o nginx do stack roteia
  `/connectors`; a chave de assinatura tem fonte única para três processos; as
  variáveis de endereço interno entre `apps/api` e `apps/connectors` e a
  variável de build do frontend entram no compose.

## Impact

- **Configuração de deploy:** `docker-compose.prod.yml`, `.env.prod.example`,
  `apps/frontend/deploy/nginx.conf`. Nenhum `Dockerfile` muda: o do
  `apps/connectors` (#103) e o `ARG` opcional do frontend (#106) já existem.
- **Documentação:** `docs/deployment.md`, `docs/configuration.md`, `02`,
  `CHANGELOG.md`, em edições curtas e localizadas (a #136 edita os mesmos
  arquivos em paralelo).
- **Produção, no próximo deploy do mantenedor:** um terceiro processo passa a
  guardar `Auth__TokenSigningKey` (#117, risco aceito); a migração da #138 roda
  pelo `migrator`; o frontend é reconstruído com a variável nova.
- **Segurança:** o `apps/connectors` guarda a chave de assinatura e a chave da
  service account do Google, e fala com API externa.
- **Fora de escopo:** chave por serviço (#117), Drive Compartilhado e política de
  compartilhamento de organização (#114), lock distribuído para várias réplicas
  (D10 da #105).
