Todas as tarefas desta change rodam em **apps/frontend**. Nenhuma toca
`apps/api`, `apps/workers` ou `apps/inbox`.

## 1. Normalização de texto para busca (apps/frontend)

- [x] 1.1 Criar `src/utils/searchText.ts` com uma função pura que
  normaliza um texto para comparação de busca: minúsculas e remoção de
  sinais diacríticos, decompondo o texto e descartando as marcas de
  acento (Decision 2 do design.md). Fica fora das features porque as duas
  listagens a usam e ela não sabe nada de domínio.
- [x] 1.2 Criar `src/utils/searchText.test.ts` cobrindo: caixa diferente;
  acentuação no texto e não no termo; acentuação no termo e não no texto;
  cedilha; texto vazio; termo vazio casando com tudo.

## 2. Listagem de agentes: colunas (apps/frontend)

- [x] 2.1 Em `AgentTable.tsx`, reorganizar as colunas para: agente (nome
  com link mais a descrição, truncada quando longa), provedor e modelo
  em uma única coluna, ferramentas, delegação e estado.
- [x] 2.2 Na coluna de ferramentas, exibir quantos servidores estão
  vinculados e quantas tools estão permitidas no total, a indicação de
  nenhum servidor vinculado quando for o caso, e o aviso de quantos
  servidores estão vinculados sem nenhuma tool (Decision 4). O cálculo
  sai de `agent.mcpServers` e vive na própria tabela.
- [x] 2.3 Na coluna de delegação, exibir os nomes dos agentes-alvo, ou a
  indicação de que não há delegação.
- [x] 2.4 Atualizar `AgentTable.test.tsx` cobrindo: descrição exibida e
  ausente; resumo de ferramentas com um e com vários servidores; agente
  sem servidor vinculado; aviso de servidores sem tools; delegação com um
  e com vários alvos; agente sem delegação; indicadores de estado e de
  reconfiguração preservados.

## 3. Listagem de agentes: busca e filtro (apps/frontend)

- [x] 3.1 Em `AgentListPage.tsx`, acrescentar o subtítulo com a
  quantidade de agentes cadastrados (Decision 5), sem afirmar quais
  canais são atendidos.
- [x] 3.2 Acrescentar o campo de busca por nome ou descrição, com estado
  local (Decision 3), filtrando no cliente com a normalização da seção 1.
- [x] 3.3 Acrescentar o controle de filtro por estado — todos, ativos,
  inativos e precisa de reconfiguração — também em estado local, aplicado
  em conjunto com a busca.
- [x] 3.4 Distinguir os dois estados vazios: catálogo sem nenhum agente
  mantém a mensagem atual e a ação de cadastrar; lista filtrada sem
  resultado exibe mensagem própria sobre a busca (Decision 6).
- [x] 3.5 Atualizar `AgentListPage.test.tsx` cobrindo: subtítulo com a
  contagem; busca por nome; busca por descrição; busca ignorando
  acentuação e caixa; cada opção do filtro por estado; busca e filtro
  combinados; mensagem de nenhum resultado distinta da de catálogo vazio.

## 4. Listagem de servidores MCP: busca (apps/frontend)

- [x] 4.1 Em `McpServerListPage.tsx`, acrescentar o subtítulo com a
  quantidade de servidores cadastrados.
- [x] 4.2 Acrescentar o campo de busca por nome ou url, com estado local,
  usando a mesma normalização.
- [x] 4.3 Distinguir os dois estados vazios, como na listagem de agentes.
- [x] 4.4 Atualizar `McpServerListPage.test.tsx` cobrindo: subtítulo com
  a contagem; busca por nome; busca por url; busca ignorando acentuação e
  caixa; mensagem de nenhum resultado distinta da de catálogo vazio.

## 5. Verificação (apps/frontend)

- [x] 5.1 Rodar a suíte completa e confirmar que tudo passa.
- [x] 5.2 Rodar lint e typecheck e confirmar que não há erros
  introduzidos.
- [x] 5.3 Rodar o build de produção e confirmar que compila.
- [x] 5.4 Subir a aplicação e conferir manualmente as duas listagens:
  colunas novas com dados reais, busca com e sem acentuação, cada opção
  do filtro, e as duas mensagens de lista vazia.
