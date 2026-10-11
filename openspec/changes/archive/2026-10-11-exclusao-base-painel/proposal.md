**Issue:** #136

## Why

A #108 abriu `DELETE /knowledge-bases/{id}` no `apps/api`, mas o painel não tem
como chamá-la. O fluxo de recuperação de uma base sincronizada (excluir a base
antiga e criar outra com a mesma pasta) continua exigindo chamada direta à API, e
a mensagem do `409` de pasta em uso segue neutra de propósito, porque mandar
excluir orientaria o operador a uma ação que a tela não oferece (D8 da #108). Com
o botão, as duas metades chegam juntas: a ação e a orientação que leva a ela.

## What Changes

- **Ação "Excluir" no detalhe da base** (`apps/frontend`), ao lado de
  `Editar` e `Ativar`/`Desativar`:
  - base **ativa**: o botão aparece indisponível, e a explicação de que é preciso
    desativar antes fica ao alcance do mouse e do teclado. Nenhuma requisição;
  - base **inativa**: o botão abre a confirmação.
- **Confirmação no padrão da exclusão de documento:** nomeia a base, os agentes
  que perdem o vínculo (de `GET /agents`, `agentsConsultingBase`), a quantidade de
  documentos que vão junto e que a ação não tem volta. Em base sincronizada, diz
  que a pasta fica livre para outra base e que os arquivos no Drive não são
  tocados. Contagem e agentes que o painel não conseguiu ler aparecem como
  desconhecidos, nunca como zero. Sem digitar o nome da base.
- **Respostas da rota, pelo código, nunca pelo `detail`:** `204` volta para a
  listagem com notificação; `409` `knowledge-base-active` (a base foi ativada em
  outra aba), `404` (já excluída) e `503` mostram cada um o seu texto na
  confirmação e não navegam.
- **Mensagem do `409` `folder-in-use` no `apps/api`:** o `detail` passa a orientar
  desativar e excluir a base que usa a pasta. **Muda de propósito** o requisito
  "Pasta em uso recusada no cadastro com a base que a usa" (#104) e troca o teste
  que proíbe `exclu`, `remov` e `apag` por um que afirma a orientação.
- **Texto de `folder-in-use` no cadastro do painel (#106):** a mesma orientação,
  e a mesma troca de teste.
- **Textos de estado da sincronização (#107):** a proibição de `exclu` continua,
  com outro motivo registrado: uma falha de sincronização nunca se resolve
  excluindo a base. Os textos não mudam.
- Comentários que afirmam que a base não tem exclusão, ou que a orientação
  espera a #136, são corrigidos (convenção 13).

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `knowledge-base-catalog-ui`: o detalhe ganha a ação de excluir, a confirmação e
  o tratamento das respostas da rota.
- `knowledge-base-catalog`: o `409` `folder-in-use` passa a orientar desativar e
  excluir a base que usa a pasta (antes: proibido de falar em excluir).
- `knowledge-base-sync-source-ui`: o texto de `folder-in-use` no cadastro passa a
  orientar desativar e excluir a base (antes: proibido de falar em excluir).
- `knowledge-base-sync-state-ui`: a proibição de `exclu` nos textos de estado
  continua, com o motivo novo escrito no requisito.

## Impact

- **`apps/frontend`:** `KnowledgeBaseDetailPage.tsx` (botão, confirmação,
  respostas), `knowledgeBasesApi.ts` (`deleteKnowledgeBase` e o comentário "Não
  há DELETE"), `useKnowledgeBases.ts` (mutação e caches), `connectorErrors.ts`
  (texto de `folder-in-use`), comentários de `syncStateMessages.ts` e testes.
- **`apps/api`:** só o `detail` e o comentário de `FolderInUse` em
  `KnowledgeBaseEndpoints.cs`, e o teste de pasta em uso. Nenhuma rota, código,
  status ou extensão muda.
- **Documentação:** `02-HISTORICO_E_STATUS.md` (entrada e correções de
  protótipo), `CHANGELOG.md`, e uma linha cada no `01` e no `docs/architecture.md`
  que hoje dizem que a mensagem espera a #136.
- Sem dependência nova, sem migração, sem variável de build.
- Fora: compose, nginx e `deployment.md` (são da #119); exclusão pela listagem;
  `apps/connectors` e `apps/workers`.
