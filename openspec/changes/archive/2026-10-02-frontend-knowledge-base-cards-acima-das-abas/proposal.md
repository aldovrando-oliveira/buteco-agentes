**Issue:** #99

## Why

No detalhe da base, a barra de abas fica acima da Descrição, e o card
"Agentes que consultam esta base" fica dentro do painel da aba `Documentos`,
abaixo da lista. Os dois cards são **da base**, não de uma aba, e por isso
somem quando o operador abre `Diagnóstico do índice` — conferido no `HEAD`:
`apps/frontend/src/features/knowledge-bases/pages/KnowledgeBaseDetailPage.tsx:289-306`
monta `KnowledgeBaseDescriptionCard` e `KnowledgeBaseAgentsCard` dentro de
`<Tabs.Panel value="documentos">`, e o `keepMounted={false}` da linha 269
desmonta o painel inteiro nas outras abas.

A linha de bases sincronizadas vai acrescentar mais informação própria da base
(o card Origem, #107) e uma terceira aba (Histórico, #101). Misturar o que é da
base com o que é de cada aba fica pior a cada acréscimo, então a separação vem
antes deles.

## What Changes

Tudo em `apps/frontend`, no detalhe da base
(`KnowledgeBaseDetailPage` e `KnowledgeBaseAgentsCard`):

- O card de Descrição e o card de agentes saem do painel da aba `Documentos` e
  passam a ficar **entre o cabeçalho e a barra de abas**, nesta ordem:
  Descrição, Agentes. Aparecem com qualquer aba ativa.
- O card de agentes passa de uma linha por agente para **chips que quebram na
  horizontal**, ocupando a largura total do conteúdo: muitos agentes viram
  linhas de chips, não um card alto.
- Cada chip continua sendo link para `/agents/{id}`, com o nome do agente como
  nome acessível.
- O estado do agente **continua visível por texto**: o chip do agente inativo
  carrega `Inativo` (correção de protótipo — ver `design.md`, D2).
- O cabeçalho do card ganha a contagem de agentes vinculados (`7 agentes`),
  exibida **só** com a lista de agentes carregada e não vazia.
- O estado vazio e o estado "agentes indisponíveis" continuam, com o texto de
  hoje.
- **Correção de defeito existente:** o card passa a ter estado de
  carregamento próprio. Hoje ele mostra "Não foi possível carregar os
  agentes…" enquanto `GET /agents` ainda nem respondeu, porque a página
  repassa o mesmo `undefined` no carregamento e na falha — afirma uma falha que
  não aconteceu (convenção 13). É corrigido aqui porque esta change reescreve
  o mesmo requisito e o mesmo componente (`design.md`, D5). O mesmo defeito no
  detalhe do servidor MCP fica na #112.

O que **não** muda: a barra de abas (aba ativa no endereço, `Documentos` como
forma canônica sem parâmetro, valor desconhecido caindo nela sem reescrever o
endereço, contador de documentos só com a listagem respondida); o conteúdo das
abas `Documentos` e `Diagnóstico do índice`; o modal de documento; a
confirmação de exclusão de documento (que nomeia os agentes afetados); a
confirmação de desativar.

Fora desta change, mesmo aparecendo nas pranchas do protótipo: a aba Histórico
(#101); o card Origem, os documentos somente leitura e os arquivos ignorados
(#107); qualquer comportamento de base sincronizada.

**Protótipo.** A referência visual é o canvas "Bases sincronizadas —
protótipo", <https://claude.ai/artifact/XzvXSpPRpqaCjNZ45ooAPY>, pranchas
**3a** (`DetalheManual.dc.html`) e, para o card com muitos agentes, **4a**
(`Detalhe.dc.html`). Como o canvas é editável, a cópia exata lida em
02/10/2026 está em [`design/`](design/README.md). O protótipo é referência
visual, não especificação: a issue e a spec mandam, e regra que o sistema já
tem vence o protótipo (convenção 17).

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `knowledge-base-catalog-ui`: o requisito **Detalhe da base de conhecimento**
  deixa de dizer que a aba `Documentos` reúne a descrição e os agentes, e passa
  a exigir os dois cards acima da barra de abas, presentes em qualquer aba; o
  requisito **Agentes que consultam a base** ganha a forma em chips, a marca
  textual do agente inativo, a contagem no cabeçalho restrita à lista
  carregada e não vazia, e o estado de carregamento distinto da falha.

## Impact

- **Código:** `apps/frontend/src/features/knowledge-bases/pages/KnowledgeBaseDetailPage.tsx`
  e `apps/frontend/src/features/knowledge-bases/components/KnowledgeBaseAgentsCard.tsx`,
  com os dois arquivos de teste correspondentes; um comentário em
  `apps/frontend/src/features/inventory/pages/InventoryPage.tsx` que cita linhas
  do card.
- **API, contratos, dependências:** nenhum. Os dados são os mesmos
  (`GET /knowledge-bases/{id}` e `GET /agents`); nenhuma requisição nova.
- **Outros apps:** nenhum. Nenhuma tarefa em `apps/api`, `apps/workers` ou
  `apps/inbox`.
