## Why

O lado do servidor MCP é hoje a metade cega do painel. O operador
consegue ver o vínculo a partir do agente, mas não a partir do servidor:
não há como saber quais agentes usam um servidor, quais tools de fato
foram permitidas, nem quem quebra se ele for desativado. O diálogo de
desativação avisa genericamente que "agentes vinculados deixarão de usar
suas tools", sem dizer quais — ou se existe algum.

O diagnóstico também é fraco. A resposta do teste de conexão traz um
`failureReason` estruturado (`HostUnreachable`, `CredentialRejected`,
`CredentialDecryptionFailed`, `Unknown`), e a interface descarta esse
campo e mostra só a mensagem crua da API, que embute texto de exceção.
Cada um desses motivos pede uma ação diferente do operador, e nenhum
deles é dito.

Por fim, o teste de conexão no formulário sempre usa a configuração
digitada. Em uma edição em que a credencial fica em branco — o caso mais
comum, já que em branco significa "manter a credencial atual" — o teste
roda sem credencial nenhuma e falha por um motivo que não é o do
servidor salvo.

Esta é a terceira change do redesenho proposto no handoff
`design_handoff_painel_agentes_mcp` (Claude Design), depois das abas no
detalhe do agente.

## What Changes

- **Visão inversa no detalhe do servidor**: um card lista os agentes que
  usam aquele servidor, com as tools permitidas de cada um, o aviso de
  vínculo sem nenhuma tool e o estado do agente. Quando ninguém usa, a
  interface diz isso e diz que desativar não afeta ninguém agora.
- **Coluna "Usado por" na listagem** de servidores, com a contagem de
  agentes e o aviso de quantos deles estão vinculados sem nenhuma tool.
- **Catálogo de tools no detalhe do servidor**: começa ocioso, sem
  disparar rede, com uma ação de atualizar que consulta o servidor ao
  vivo. Cada tool mostra em quantos agentes ela está permitida.
- **Diálogo de desativação nomeia os agentes afetados**, ou diz que não
  há nenhum.
- **Mensagem de falha por motivo**: cada `failureReason` ganha uma
  explicação com a ação correspondente, e o motivo aparece também como
  rótulo técnico. O resultado do teste passa a dizer quando foi feito e
  que não fica guardado.
- **Teste de conexão unificado no formulário**: em edição com a
  credencial em branco, o teste passa a usar o endpoint do servidor
  salvo, avisando que a credencial guardada é a que será usada. Nos
  demais casos segue usando a configuração digitada. Testar sem URL
  preenchida passa a ser barrado antes da requisição.
- **Card de configuração** no detalhe passa a mostrar a linha de
  credencial cifrada quando o tipo de autenticação exige uma, deixando
  explícito que a API nunca devolve o valor.

## Capabilities

### New Capabilities
(nenhuma)

### Modified Capabilities
- `mcp-server-catalog-ui`: listagem ganha a coluna de uso; detalhe ganha
  catálogo de tools e visão inversa; desativação nomeia os agentes
  afetados; teste de conexão ganha mensagem por motivo, marca temporal
  não persistida e unificação no formulário.

## Impact

- **apps/frontend**: nasce um módulo puro que deriva o uso de cada
  servidor a partir do catálogo de agentes, consumido pela listagem, pelo
  detalhe e pelo diálogo de desativação. A listagem e o detalhe do
  servidor passam a buscar também `GET /agents`. Nascem dois componentes
  no detalhe (catálogo de tools e agentes que usam o servidor). São
  alterados a tabela e a página de listagem, a página e o card de detalhe,
  o alerta de resultado de teste e o formulário.
- **apps/api / apps/workers / apps/inbox**: nenhuma mudança. Todos os
  endpoints usados já existem.
- **Fora de escopo**: busca e filtro nas listagens, colunas novas na
  listagem de agentes e o bloco de primeiros passos, todos na change
  seguinte; card do protocolo A2A; densidade.

## Notas de escopo

A coluna "Usado por" é uma coluna de listagem, e o cronograma do
redesenho previa colunas novas para a change de listas. Ela vem aqui
porque depende da mesma derivação de uso que sustenta a visão inversa e o
diálogo de desativação: separá-la significaria construir a derivação em
uma change e o seu primeiro consumidor em outra. A change de listas segue
com busca, filtro e as colunas da listagem de agentes.
