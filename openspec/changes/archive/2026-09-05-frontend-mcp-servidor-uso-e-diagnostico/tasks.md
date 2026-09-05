Todas as tarefas desta change rodam em **apps/frontend**. Nenhuma toca
`apps/api`, `apps/workers` ou `apps/inbox`.

## 1. Derivação de uso (apps/frontend)

- [x] 1.1 Criar `features/mcp-servers/utils/agentUsage.ts` como módulo
  puro, sem hooks nem componentes, expondo sobre uma lista de agentes:
  quem usa um servidor (agente mais as tools permitidas naquele vínculo),
  a contagem de agentes que usam, a contagem dos que usam sem nenhuma
  tool, e em quantos agentes uma tool específica está permitida
  (Decision 1 do design.md).
- [x] 1.2 Criar `agentUsage.test.ts` cobrindo: servidor sem nenhum
  agente; agente vinculado com tools; agente vinculado sem nenhuma tool;
  vários agentes no mesmo servidor; tool permitida em nenhum, em um e em
  vários agentes; agente vinculado a outro servidor não contaminando o
  resultado.

## 2. Listagem de servidores MCP (apps/frontend)

- [x] 2.1 Em `McpServerListPage.tsx`, chamar `useAgentsQuery()` além da
  query de servidores e repassar o catálogo por propriedade para a
  tabela, tratando a falha dessa query sem quebrar a listagem
  (Decision 2).
- [x] 2.2 Em `McpServerTable.tsx`, acrescentar a coluna "Usado por" com a
  contagem de agentes (ou a indicação de que nenhum agente usa) e, quando
  aplicável, o aviso de quantos estão vinculados sem nenhuma tool.
- [x] 2.3 Atualizar `McpServerTable.test.tsx` e `McpServerListPage.test.tsx`
  cobrindo: contagem exibida; indicação de nenhum agente; aviso de
  vinculados sem tools; catálogo de agentes indisponível mantendo as
  demais colunas.

## 3. Resultado do teste de conexão (apps/frontend)

- [x] 3.1 Em `ConnectionTestResultAlert.tsx`, passar a receber o
  resultado completo do teste (incluindo `failureReason`) e a marca
  temporal em memória do momento do teste.
- [x] 3.2 Exibir, para cada `failureReason`, a explicação com a ação
  correspondente descrita na Decision 3 do design.md, mais o motivo como
  rótulo técnico, mais a mensagem da API como detalhe secundário quando
  existir.
- [x] 3.3 Exibir, em sucesso e em falha, quando o teste foi feito e que o
  resultado não é persistido. Em sucesso, não afirmar quantidade de tools
  (Decision 4).
- [x] 3.4 Atualizar `ConnectionTestResultAlert.test.tsx` cobrindo os
  quatro motivos, o caso de falha sem motivo, o sucesso, a mensagem
  secundária e a indicação de não persistência.

## 4. Detalhe do servidor MCP (apps/frontend)

- [x] 4.1 Renomear `McpServerDetailCard.tsx` para `McpServerConfigCard.tsx`
  e reorganizá-lo como card de configuração (url, autenticação,
  credencial, datas), acrescentando a linha de credencial cifrada e
  mascarada quando o tipo de autenticação exigir credencial (Decision 7).
  Migrar o teste correspondente.
- [x] 4.2 Criar `McpServerToolsCatalog.tsx`: começa ocioso sem disparar
  requisição, com ação de atualizar; percorre carregando, erro com nova
  tentativa, vazio e lista; cada tool exibe nome, descrição e em quantos
  agentes está permitida. Reusa o hook de descoberta já existente, com a
  mesma chave de cache (Decision 5). Teste ao lado.
- [x] 4.3 Criar `McpServerAgentsCard.tsx`: relação dos agentes que usam o
  servidor, com nome linkando para o detalhe do agente, tools permitidas,
  aviso de vínculo sem nenhuma tool e estado do agente; estado vazio
  dizendo que desativar não afeta ninguém agora. Teste ao lado.
- [x] 4.4 Em `McpServerDetailPage.tsx`, reorganizar a página: cabeçalho
  com nome, estado, descrição e as ações (editar, testar conexão,
  ativar/desativar); resultado do teste; grade com o card de configuração
  e o catálogo de tools; e, abaixo, o card de agentes que usam o
  servidor. Buscar o catálogo de agentes e repassá-lo por propriedade.
- [x] 4.5 No diálogo de desativação, nomear os agentes afetados com a
  contagem, ou informar que nenhum agente usa aquele servidor.
- [x] 4.6 Atualizar `McpServerDetailPage.test.tsx` cobrindo: cabeçalho e
  ações; linha de credencial presente e ausente conforme o tipo de
  autenticação; catálogo ocioso sem requisição e populado após atualizar;
  card de agentes com e sem uso; diálogo nomeando agentes e diálogo sem
  agentes; falha do catálogo de agentes não quebrando a página.

## 5. Teste de conexão no formulário (apps/frontend)

- [x] 5.1 Em `McpServerForm.tsx`, receber opcionalmente o id do servidor
  em edição e escolher o endpoint do teste pelo estado da credencial: em
  branco durante a edição usa o endpoint do servidor salvo; nos demais
  casos usa o endpoint que recebe a configuração digitada (Decision 6).
- [x] 5.2 Exibir, no caso do servidor salvo, a nota de que o teste usa a
  credencial salva daquele servidor.
- [x] 5.3 Barrar o teste quando a URL está vazia, indicando que ela
  precisa ser informada, sem enviar requisição.
- [x] 5.4 Ajustar as dicas do campo de credencial: no cadastro, que ela é
  enviada cifrada e nunca retorna na API; na edição, que deixá-la em
  branco mantém a credencial atual.
- [x] 5.5 Em `McpServerEditPage.tsx`, repassar o id do servidor ao
  formulário.
- [x] 5.6 Atualizar `McpServerForm.test.tsx`, `McpServerEditPage.test.tsx`
  e `McpServerCreatePage.test.tsx` cobrindo: cadastro testando a
  configuração digitada; edição sem credencial testando o servidor salvo,
  com a nota exibida; edição com credencial digitada testando a
  configuração; teste barrado sem URL; resultado invalidado ao alterar
  url, tipo de autenticação ou credencial.

## 6. Verificação (apps/frontend)

- [x] 6.1 Rodar a suíte completa e confirmar que tudo passa.
- [x] 6.2 Rodar lint e typecheck e confirmar que não há erros
  introduzidos.
- [x] 6.3 Rodar o build de produção e confirmar que compila.
- [x] 6.4 Subir a aplicação e conferir manualmente: a coluna de uso na
  listagem, o catálogo de tools indo de ocioso a populado, a visão
  inversa em um servidor com e sem agentes, o diálogo de desativação
  nomeando agentes, e o teste de conexão na edição sem digitar credencial.
