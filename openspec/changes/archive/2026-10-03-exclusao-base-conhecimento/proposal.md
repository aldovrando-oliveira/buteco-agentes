**Issue:** #108

## Why

Base de conhecimento não pode ser excluída: é catálogo, e catálogo neste projeto só
se desativa. Na linha de bases sincronizadas isso prende a pasta para sempre à base
que a usou — a unicidade de provedor e pasta vale para base inativa, a pasta é
imutável, e o fluxo de recuperação aprovado quando a pasta é perdida (excluir a base
antiga e criar outra) é impossível. A mensagem do `409` de pasta em uso (#104) e o
alerta de falta de acesso (#107) dependem dessa exclusão para fazer sentido.

## What Changes

- **Exceção explícita à regra "catálogo só se desativa"**, restrita a
  `KnowledgeBase`. As outras entidades de catálogo (`Agent`, `McpServer`,
  `Channel`) continuam só se desativando. A regra muda no `01` e no
  `docs/architecture.md`.
- **`DELETE /knowledge-bases/{id}`** no `apps/api`, do operador:
  - `204` quando exclui; `404` para base inexistente;
  - **só base inativa**: base ativa é recusada com `409` e
    `code: "knowledge-base-active"`, sem apagar nada;
  - apaga junto os documentos (na aplicação, com a FK documento → base mantida em
    `Restrict`), e pelas cascatas que já existem os fragmentos, os eventos de
    histórico e os vínculos com agentes;
  - vale para base `Manual` e `Synced`; numa `Synced`, a pasta fica livre para
    outra base.
- **Escritas de `/sync` que encontram a base excluída no meio respondem `404`,
  nunca `500`.** O upsert, a exclusão por referência e a gravação do resultado de
  ciclo passam a tratar a base que sumiu entre a leitura e a gravação.
- **BREAKING** (contrato interno da API): `DELETE /knowledge-bases/{id}` deixa de
  responder `405`.
- **Fora desta change:**
  - a tela: a confirmação no painel vai para a **#136** (bloqueada pela #108);
  - a mensagem do `409` de pasta em uso (#104): continua neutra, e a mudança para
    orientar a exclusão é da **#136**, junto com o botão;
  - as corridas do operador contra a exclusão (inclusão de documento e
    substituição de vínculos): risco aceito, registrado no `design.md`.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `knowledge-base-catalog`: exclusão de base (requisito novo), e o requisito de
  ativação e desativação deixa de proibir a rota de exclusão.
- `knowledge-sync-service-api`: base excluída durante uma escrita da sincronização
  responde `404`, e o `503` de contenção deixa de cobrir esse caso.
- `knowledge-document-history`: os eventos morrem com a base também pela rota de
  exclusão, e não só por SQL.
- `agent-knowledge-binding`: excluir a base remove os vínculos dela.

## Impact

- **`apps/api`**: rota e handler novos em `KnowledgeBases/`; tratamento da base
  sumida em `KnowledgeSync/Commands/*`; em `KnowledgeBaseEndpoints`, só o
  comentário de `FolderInUse` (a frase do `409` não muda). **Sem migração**: o
  schema já tem as cascatas necessárias, e a FK de documento continua `Restrict`.
- **`apps/workers`**: nenhuma mudança de código. O consumidor de indexação já
  descarta documento que sumiu (conferido no `design.md`, D7).
- **`apps/connectors`**: nenhuma mudança nesta change. A #105 trata `404` na base
  como fim do ciclo daquela base; esta change garante que o `404` chega.
- **`apps/frontend`**: nenhuma mudança (#136).
- **Documentação**: `01-ARQUITETURA_E_CONVENCOES.md`, `docs/architecture.md`,
  `02-HISTORICO_E_STATUS.md`, `CHANGELOG.md` — edições localizadas, porque a #105
  edita os mesmos arquivos em paralelo.
