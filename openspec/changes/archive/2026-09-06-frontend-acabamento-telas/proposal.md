## Why

A conferência visual da etapa da identidade visual encontrou dez divergências
contra o protótipo do handoff. Duas eram regressão daquela etapa e foram
corrigidas lá; uma era da casca e foi resolvida na etapa seguinte. **Sete
continuam abertas**, todas registradas em nota arquivada com o motivo de não
caberem nem no tema nem na casca: são composição de componente e escolha de
call site.

Não há descoberta a fazer — o escopo já está levantado, com arquivo, causa e
correção proposta para cada item. O que falta é executá-lo.

Um dos sete é defeito, não divergência estética, e é anterior ao redesenho: numa
linha com descrição longa, a coluna da direita quebra em duas linhas por falta
de proteção contra encolhimento.

## What Changes

- **Cards e tabelas ganham cabeçalho em faixa e linhas divididas.** O padrão
  se repete no catálogo de tools, nas listagens e nos cards de detalhe; entra
  como componente compartilhado em vez de ser resolvido caso a caso.
- **Os rótulos de card em maiúsculas recuam um tom e ganham espaçamento entre
  letras.** Hoje usam o semântico de texto secundário, que o tema resolve para
  o tom mais escuro dos dois neutros médios — o protótipo usa o terciário para
  esse papel. As cinco cópias locais do mesmo rótulo viram um componente só.
- **Os badges passam à variante clara, em caixa de sentença.** Vai como padrão
  no tema, não em cada chamada: cobre os dezenove badges do painel de uma vez e
  garante que um badge tenha a mesma aparência em qualquer tela. Resolve
  também o contraste — a variante preenchida em verde e âmbar com texto branco
  não atinge o mínimo exigido, e a clara atinge folgado.
- **As páginas de detalhe ganham link de volta para a listagem.** Hoje não há
  nenhum: voltar depende do botão do navegador ou da barra lateral.
- **Os filtros da listagem ficam numa linha só, sem rótulo visível.** O campo de
  busca perde o rótulo — o texto de exemplo já diz o que ele faz — e o controle
  segmentado perde os separadores verticais, ganha borda e fica compacto ao
  lado do campo.
- **Corrigido o defeito de layout** da coluna direita que quebra em duas linhas
  quando a descrição ao lado é longa.

Fora de escopo: a preferência de densidade compacta e confortável, o card
"Primeiros passos", e o card A2A no detalhe do agente — que é etapa própria,
já aprovada e ainda não proposta.

## Capabilities

### New Capabilities
- `frontend-visual-patterns`: os padrões de interface recorrentes do painel
  como contrato — o card com cabeçalho em faixa e linhas divididas, o rótulo de
  seção em maiúsculas, o cabeçalho de página de detalhe com volta para a
  listagem, e o campo de busca sem rótulo visível. São decisões que hoje seriam
  repetidas em cada tela e que passam a ter um lugar único, para que a próxima
  tela não precise redecidir nem divergir.

### Modified Capabilities
- `frontend-visual-theme`: ganha um requisito novo — a aparência padrão do
  badge é declarada no tema, não em cada chamada. Nenhum requisito existente
  muda; é o que faz valer a regra de contraste que já está lá e que a variante
  preenchida em verde e âmbar não atende.

- `inbox-channel-catalog-ui`: ganha busca na listagem, por nome, tipo e agente
  responsável, mais a contagem de canais cadastrados. **Escopo estendido durante
  a conferência visual**: a listagem de canais era a única das três sem busca, e
  a diferença aparece lado a lado. É comportamento novo, não acabamento, e por
  isso vira requisito em vez de ficar só no código.

As capabilities `agent-catalog-ui` e `mcp-server-catalog-ui` **não** mudam.
Conferido: nenhuma delas especifica rótulo de campo, composição de card ou
navegação de volta; o que muda ali é apresentação, e as garantias novas ficam
nos padrões compartilhados, onde valem para as três de uma vez em vez de virarem
requisitos quase idênticos.

## Impact

Afeta **apenas `apps/frontend`**. Nenhuma mudança em `apps/api`,
`apps/workers` ou em contrato de rota HTTP.

- Nascem componentes compartilhados para o card seccionado, o rótulo de seção e
  o cabeçalho de página de detalhe com link de volta — as três telas de detalhe
  repetem hoje a mesma estrutura de título, badge, descrição e ações.
- O tema ganha o padrão de badge; nenhum dos dezenove call sites é editado por
  causa disso.
- As duas listagens, as três tabelas, o catálogo de tools, os cards de detalhe
  e as três páginas de detalhe são tocados.
- É a etapa com mais superfície de tela de todo o redesenho, e a suíte continua
  cega ao que ela muda. A conferência é manual, como nas duas anteriores.
