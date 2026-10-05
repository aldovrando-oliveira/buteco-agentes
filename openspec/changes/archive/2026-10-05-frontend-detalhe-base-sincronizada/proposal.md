**Issue:** #107

## Why

O painel já cria base sincronizada (#106) e o `apps/connectors` já sincroniza a
pasta a cada 5 minutos, gravando o desfecho no `apps/api` (#105), mas o detalhe e
a listagem de base ainda tratam toda base como manual. O operador não vê de onde
vem o conteúdo, se a sincronização está funcionando nem o que fazer quando ela
falha, e o detalhe de uma base sincronizada oferece "Adicionar documento",
"Atualizar" e "Excluir", que o `apps/api` recusa com `409`. O mantenedor viu esses
botões aparecendo na validação da #106.

## What Changes

- **Detalhe de base sincronizada (pranchas 4a e 4b):**
  - card "Origem — pasta sincronizada", entre a descrição e os agentes: nome da
    pasta, "Abrir no Google Drive ↗", provedor, última sincronização concluída e o
    estado do último ciclo. "Nunca sincronizou" aparece como espera, não como
    falha;
  - em falha, o nome da pasta é rotulado "nome na última sincronização concluída",
    e um alerta diz o motivo pelo código, que nenhum documento saiu da base e o
    caminho de recuperação. Em `access-denied`, o alerta mostra o e-mail da conta;
  - documentos somente leitura: sem "Adicionar documento", "Atualizar" e
    "Excluir", com a explicação no cabeçalho do card. "Reindexar documento"
    continua na faixa de falha, porque o `apps/api` o permite em base
    sincronizada;
  - card "Arquivos da pasta que não entraram na base", com nome e motivo. Lista
    nula (nenhuma sincronização concluída) e lista vazia têm textos diferentes.
- **"Sincronizar agora"** no card de Origem, pelo cliente do `apps/connectors` da
  #106: mostra "sincronização solicitada", acompanha `lastFinishedAt` no
  `apps/api` com consulta periódica condicional e limitada, fica desabilitado
  enquanto espera, e só mostra o resultado depois de o estado mudar. Com o
  `apps/connectors` fora do ar, erro explícito. Sem `VITE_CONNECTORS_BASE_URL`,
  o botão fica indisponível com explicação, sem nenhuma chamada ao
  `apps/connectors`, e o resto do estado continua aparecendo.
- **Texto em português para cada código** de falha da base, de arquivo ignorado
  (inclusive os quatro da #120) e de resposta do "Sincronizar agora", ao lado da
  tabela da #106. Nenhum texto manda excluir nada (a exclusão de base é da #136).
- **Listagem (prancha 1):** linha de origem em cada base ("Manual" ou
  "Google Drive › pasta") e linha "Sincronização falhando desde …" quando houver.
- **Filtro "Com falha":** passa a incluir base com sincronização falhando
  (`failingSince` preenchido), inclusive inativa e sem falha de indexação. Sem o
  resumo de indexação, continua desabilitado, mesmo havendo falha de
  sincronização.
- O tipo `KnowledgeBase` do frontend passa a ler `syncState`, que o `apps/api`
  devolve desde a #102.

Base manual: o detalhe não muda; a listagem ganha só a linha "Manual".

Nenhuma mudança em `apps/api`, `apps/connectors`, `apps/workers` ou `apps/inbox`:
a tela usa só rotas e campos que já existem.

Fora desta change: aba Histórico (#101), modal de documento (#131), exclusão de
base e a orientação de excluir (#136).

## Capabilities

### New Capabilities

- `knowledge-base-sync-state-ui`: o detalhe de base sincronizada — card de
  Origem, estado da sincronização, alerta de falha, arquivos ignorados, texto por
  código, "Sincronizar agora" com acompanhamento, e o comportamento sem
  `VITE_CONNECTORS_BASE_URL`.

### Modified Capabilities

- `knowledge-base-catalog-ui`: a listagem ganha a linha de origem e a de falha de
  sincronização; o filtro "Com falha" passa a incluir sincronização falhando.
- `knowledge-document-catalog-ui`: a listagem de documentos de base sincronizada
  é somente leitura (sem adicionar, atualizar e excluir), e o estado vazio dela não
  oferece adicionar.

## Impact

- **`apps/frontend`:** `KnowledgeBaseDetailPage`, `KnowledgeDocumentsCard`,
  `KnowledgeBaseTable`, `KnowledgeBaseListPage`; componentes novos do card de
  Origem e dos arquivos ignorados; `connectorsApi.ts` ganha
  `requestKnowledgeBaseSync`; `connectorErrors.ts` ganha o contexto do pedido de
  sincronização; módulo novo de textos de estado; `indexingSummary.ts` (filtro);
  tipo `KnowledgeBase` com `syncState`.
- **Documentação:** `02-HISTORICO_E_STATUS.md` (entrada e "Correções de
  protótipo"), `CHANGELOG.md`.
- **Sem dependência nova** e sem variável de build nova.
