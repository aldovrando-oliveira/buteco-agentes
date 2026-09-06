## Why

As duas listagens do painel são as telas por onde o operador entra, e
ambas mostram menos do que ele precisa para decidir onde clicar.

A listagem de agentes tem quatro colunas — nome, provider, model e estado
— e nenhuma delas responde as perguntas que levam alguém a abrir um
agente: quantas ferramentas ele tem, se algum servidor está vinculado sem
tool nenhuma, para quem ele delega. A descrição do agente, que existe
desde a primeira change deste redesenho, não aparece em lugar nenhum da
lista. E com dezenas de agentes não há como achar um: não existe busca
nem filtro, então o operador rola a página.

A listagem de servidores MCP tem o mesmo problema de busca, e nenhuma das
duas distingue "não há nada cadastrado" de "nada corresponde ao que você
procurou" — as duas situações mostram a mesma mensagem, ou nenhuma.

Esta é a quarta change do redesenho proposto no handoff
`design_handoff_painel_agentes_mcp` (Claude Design).

## What Changes

- **Busca nas duas listagens**: por nome ou descrição na de agentes, por
  nome ou url na de servidores. A comparação ignora maiúsculas e
  acentuação, para que procurar por "cobranca" encontre "Cobrança".
- **Filtro por estado na listagem de agentes**: todos, ativos, inativos e
  os que precisam de reconfiguração, combinável com a busca.
- **Colunas novas na listagem de agentes**: a coluna de agente passa a
  mostrar nome e descrição; provedor e modelo se juntam em uma coluna; e
  entram duas colunas novas, uma de ferramentas (quantos servidores e
  quantas tools, com aviso de quantos servidores estão vinculados sem
  nenhuma tool) e uma de delegação (para quais agentes ele delega).
- **Subtítulo com a contagem** em cada listagem, dizendo quantos itens
  existem e para que servem.
- **Estados vazios distintos**: a mensagem de catálogo vazio deixa de ser
  usada quando o que aconteceu foi a busca não encontrar nada.

## Capabilities

### New Capabilities
(nenhuma)

### Modified Capabilities
- `agent-catalog-ui`: a listagem ganha busca, filtro por estado, colunas
  de descrição, ferramentas e delegação, e estados vazios distintos.
- `mcp-server-catalog-ui`: a listagem ganha busca e estados vazios
  distintos.

## Impact

- **apps/frontend**: nasce um utilitário compartilhado de normalização de
  texto para busca. As duas tabelas e as duas páginas de listagem são
  alteradas. Nenhuma query nova: as colunas novas saem de campos que
  `GET /agents` já devolve.
- **apps/api / apps/workers / apps/inbox**: nenhuma mudança.
- **Fora de escopo**: o bloco de primeiros passos e a preferência de
  densidade, ambos adiados por decisão explícita; o card do protocolo
  A2A, que depende de mudança na API; e qualquer busca ou paginação no
  backend.

## Dívida assumida

Busca e filtro são feitos no cliente, sobre a lista inteira que a API já
devolve, porque não existe busca nem paginação em `GET /agents` nem em
`GET /mcp-servers`. Funciona bem na ordem de grandeza atual e deixa de
funcionar quando o catálogo crescer o bastante para a resposta ficar
pesada. A saída, nesse momento, é paginação e busca no backend — não uma
otimização no cliente.
