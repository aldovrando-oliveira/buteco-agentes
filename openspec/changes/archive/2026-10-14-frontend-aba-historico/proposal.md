**Issue:** #101

## Why

A #98 passou a gravar o histórico de mudanças nos documentos de uma base
(`GET /knowledge-bases/{id}/document-events`), mas o operador não tem onde vê-lo.
Na base sincronizada é a única forma de saber o que a sincronização fez sem ter
feito nada; na base manual é a auditoria do que o operador fez. A rota está no ar
desde a #98 e não tem consumidor.

## What Changes

- **Aba `Histórico`** no detalhe da base (`apps/frontend`), entre `Documentos` e
  `Diagnóstico do índice`, com endereço próprio (`?tab=historico`) e montada só
  quando ativa, como as outras duas.
- **Eventos agrupados por dia**, do mais recente para o mais antigo, na ordem que
  a rota devolve: horário, tipo (`Incluído`, `Atualizado`, `Excluído`), título do
  documento como o evento o gravou, detalhe da atualização (`Conteúdo alterado`,
  `Título alterado`, ou os dois) e autor (`Operador`, `Sincronização`, ou um rótulo
  neutro com o valor gravado quando o autor não é reconhecido).
- **"Carregar mais"** pede a página seguinte pelo `nextCursor`, e some quando ele
  é nulo. A falha de uma página seguinte não apaga o que já foi carregado.
- **Carregando, falha e lista vazia** são três estados com três textos; nenhum
  deles é apresentado como outro.
- **Nota** de que o histórico só existe a partir da implantação da #98, e, na
  base sincronizada, de que arquivos ignorados e falhas de acesso não entram nele.
- **O fim de um "Sincronizar agora"** relê o histórico, além dos documentos.
- **A proibição de `exclu`, `remov` e `apag`** dos textos de estado da
  sincronização continua como está e **não** alcança o histórico, que registra
  fatos com o vocabulário da própria issue (`Excluído`). O alcance fica escrito no
  requisito e no comentário da varredura (D5 do `design.md`).

### O que esta change NÃO faz, e é decisão e não esquecimento

- **Nenhuma mudança no `apps/api`.** A aba consome o contrato da #98 como ele
  existe. Nenhum campo é inventado: o título anterior de uma renomeação e o motivo
  de uma exclusão pela sincronização, que o protótipo desenha, não estão no
  contrato e não aparecem (correções de protótipo C1 e C2).
- **Sem contador na aba `Histórico`.** A rota não devolve total, de propósito (D7
  da #98).
- **Sem acompanhamento em intervalo.** O histórico não é processo em andamento
  (convenção 20); ele é relido ao entrar na aba e ao fim de um "Sincronizar agora".
- **Sem retenção.** Segue na #109.

## Capabilities

### New Capabilities

- `knowledge-document-history-ui`: o conteúdo da aba `Histórico` — o que cada
  evento mostra, rótulos de tipo, detalhe e autor (com os desconhecidos), o
  agrupamento por dia no fuso do navegador, a paginação por "Carregar mais", os
  estados de carregamento, falha e vazio, a nota de começo do histórico, a
  releitura e a acessibilidade da lista.

### Modified Capabilities

- `knowledge-base-catalog-ui`: o requisito "Detalhe da base de conhecimento" passa
  de duas para **três** abas, com `Histórico` entre as duas que existem, e o
  parâmetro de endereço dela.
- `knowledge-base-sync-state-ui`: o requisito "Alerta de falha da sincronização
  com o motivo pelo código" ganha o alcance da proibição de `exclu`, `remov` e
  `apag`: ela vale para os textos de estado da sincronização, e não para o
  histórico, com o motivo.

## Impact

- **`apps/frontend`:** `KnowledgeBaseDetailPage.tsx` (terceira aba, consulta só
  com a aba ativa, releitura ao entrar e ao fim do ciclo),
  `api/knowledgeDocumentsApi.ts` e `api/useKnowledgeDocuments.ts` (rota e consulta
  paginada), `types/knowledgeDocumentEvent.ts` (novo),
  `utils/documentHistory.ts` (novo: rótulos e agrupamento),
  `components/KnowledgeDocumentHistoryTab.tsx` (novo, apresentacional), o
  comentário da varredura em `utils/syncStateMessages.test.ts` e os testes.
- **`apps/api`, `apps/workers`, `apps/inbox`, `apps/connectors`, `libs/`:** nada.
- **Documentação:** `02-HISTORICO_E_STATUS.md` (entrada da change e correções de
  protótipo), `CHANGELOG.md`.
- Sem dependência nova, sem variável de build, sem migração. É a primeira
  paginação do painel; ela usa `useInfiniteQuery`, que já está no
  `@tanstack/react-query` instalado.
