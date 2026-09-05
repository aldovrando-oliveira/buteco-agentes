## Why

Hoje o painel trata duas relações do agente de formas inconsistentes: o
vínculo com servidores MCP mora em uma página própria
(`/agents/:id/mcp-servers`), e as delegações moram em uma seção dentro do
próprio detalhe. Quem gerencia um agente sai da tela para ligar tools e
não sai para ligar delegações, sem que nada no domínio justifique a
diferença.

O redesenho proposto no handoff `design_handoff_painel_agentes_mcp`
(Claude Design) resolve isso colocando as duas relações como abas do
detalhe do agente, e aproveita para cobrir três buracos que a interface
atual tem hoje:

- **Vínculo sem nenhuma tool passa despercebido.** Um servidor vinculado
  com `allowedTools` vazio não oferece ferramenta nenhuma ao modelo em
  runtime, e a interface não distingue esse estado de um vínculo saudável.
- **Rascunho não salvo se perde em silêncio.** Sair da página de vínculo
  com seleção pendente descarta tudo sem aviso.
- **Salvar o vínculo joga o operador para outra tela**, mesmo quando ele
  quer continuar ajustando o mesmo agente.

A change anterior (`frontend-roteamento-data-router`) já deixou o
roteamento em data mode, que é o que torna o aviso de rascunho não salvo
possível.

## What Changes

- **Detalhe do agente vira um host de abas**: cabeçalho com nome, estado,
  descrição e ações, e abaixo três abas — **Visão geral**, **Ferramentas**
  e **Delegações**, as duas últimas com contador. A aba ativa vive na URL
  como query param (`?tab=ferramentas`), então é linkável e sobrevive a
  refresh.
- **A rota `/agents/:id/mcp-servers` é removida** e passa a redirecionar
  para a aba Ferramentas do detalhe, por um `loader` de rota, para não
  quebrar links salvos.
- **Aba Visão geral**: instruções em Markdown de um lado; do outro, cards
  de modelo, skills e datas. O card único de detalhe que existe hoje é
  desmontado nessas partes, e o resumo textual de servidores vinculados
  sai, porque a aba Ferramentas passa a ser a fonte dessa informação.
- **Aba Ferramentas**: o conteúdo da página de vínculo, com o que o
  handoff acrescenta — resumo de servidores e tools, aviso destacado
  quando algum servidor está vinculado sem nenhuma tool, aviso quando um
  servidor vinculado está inativo, contador de tools por servidor, e
  marcar o servidor passa a vincular, expandir e disparar a descoberta em
  um gesto só.
- **Aba Delegações**: a seção atual deixa de ser um campo de seleção
  múltipla e vira lista de agentes com busca por nome, exibindo o modelo
  de cada agente-alvo ou a marca de inativo.
- **Barra de alterações não salvas**, fixa no rodapé, aparecendo só quando
  há diferença real entre o rascunho e o estado salvo, com ações de
  descartar e salvar. Salvar o vínculo passa a manter o operador na aba,
  com notificação de sucesso, em vez de navegar para outro lugar.
- **Aviso ao sair com rascunho não salvo**, cobrindo troca de aba, saída
  da rota e fechamento da janela.

## Capabilities

### New Capabilities
(nenhuma)

### Modified Capabilities
- `agent-mcp-binding-ui`: a gestão do vínculo deixa de ser página e vira
  aba; ganha aviso de vínculo sem tools, barra de alterações não salvas,
  guarda de rascunho e permanência na tela após salvar; a rota antiga
  passa a redirecionar.
- `agent-delegation-binding-ui`: a seção vira aba, com busca por nome e
  a mesma barra de alterações não salvas.
- `agent-catalog-ui`: o requisito de detalhe de agente passa a descrever
  as abas e deixa de exigir link para a rota removida.

## Impact

- **apps/frontend**: o detalhe do agente vira host de abas; três
  componentes de aba nascem; o card de detalhe e a seção de delegações
  atuais são substituídos; a linha de servidor MCP é reescrita; a página
  de vínculo é removida junto com sua rota. Nascem também uma barra de
  alterações não salvas, um diálogo de descarte e um hook de guarda de
  navegação, todos genéricos e fora da pasta de agentes. O hook de
  catálogo de servidores MCP ganha a opção de só buscar quando a aba
  estiver ativa.
- **apps/api / apps/workers / apps/inbox**: nenhuma mudança. Os endpoints
  são exatamente os mesmos.
- **Fora de escopo**: o lado do servidor MCP (visão inversa, catálogo de
  tools no detalhe, cópia por motivo de falha), busca e filtros nas
  listagens, card do protocolo A2A, densidade e o bloco de primeiros
  passos. Cada um tem sua própria change no cronograma.
