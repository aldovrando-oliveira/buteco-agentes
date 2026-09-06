## Context

A conferência visual de `frontend-tema-identidade-visual` deixou dez achados
numerados em nota arquivada. Dois eram regressão daquela etapa e foram
corrigidos lá (decisões D12 e D13); o achado 8 era da casca e foi resolvido em
`frontend-shell-navegacao-e-icones`. Restam sete, com arquivo, causa e correção
proposta já levantados:

| # | achado | natureza |
|---|---|---|
| 1 | catálogo de tools sem divisor entre linhas nem faixa no cabeçalho | composição |
| 2 | rótulo de card um tom escuro demais | call site |
| 3 | falta espaçamento entre letras nos rótulos em maiúsculas | tema/componente |
| 4 | coluna direita quebra em duas linhas | **defeito** |
| 5 | badge saturado, em caixa alta | tema/componente |
| 6 | detalhe sem link de volta | composição |
| 9 | campo de busca com rótulo visível | call site |

Mais dois resíduos registrados dentro dos achados 7 e 10, que aquela etapa
corrigiu só na parte que era regressão: as tabelas ficaram com superfície mas
sem faixa de cabeçalho nem divisores, e o controle segmentado ficou com
container mas com os separadores verticais que o protótipo não tem.

Duas decisões de produto foram fechadas antes desta proposta, resolvendo a
divergência entre o protótipo e o documento de ajustes visuais: **caixa de
sentença** nos badges, seguindo o protótipo, que é a identidade acordada; e a
variante clara valendo para **todos** os badges, via padrão no tema.

## Goals / Non-Goals

**Goals:**
- Fechar os sete achados abertos e os dois resíduos.
- Transformar em componente compartilhado tudo que a nota identificou como
  padrão repetido, em vez de corrigir tela a tela.
- Corrigir o único defeito funcional da lista.

**Non-Goals:**
- Preferência de densidade compacta e confortável, e card "Primeiros passos" —
  adiados por decisão de 2026-09-05.
- Card A2A no detalhe do agente: etapa própria, já aprovada.
- Redesenhar telas que o protótipo não cobre. O histórico de sessões é tocado
  só pelo que vier de graça no padrão de badge.

## Decisions

### D1 — Um componente para o card com cabeçalho em faixa e linhas divididas

O achado 1 já apontava: *"o mesmo padrão aparece nas duas listagens e
provavelmente em outros cards de detalhe; vale resolver como um componente
compartilhado em vez de caso a caso"*. A varredura confirmou — o catálogo de
tools, as três tabelas e os cards de detalhe repetem a mesma estrutura.

O Mantine já tem a peça: `Card.Section withBorder` desenha a borda que separa
uma seção da seguinte, e tirar o padding do `Card` para dar a cada seção o seu
resolve a faixa do cabeçalho junto. Hoje o painel não usa `Card.Section` em
lugar nenhum.

O componente compartilhado expõe cabeçalho e linhas; quem usa não decide borda,
fundo nem padding. É o que impede a próxima tela de divergir de novo.

Uma verificação feita antes de escrever refinou o alcance: as tabelas **já têm**
o divisor entre linhas, porque o Mantine liga isso por padrão e a cor que ele
usa é a mesma borda que a identidade visual declara. O que falta nelas é só a
faixa do cabeçalho. E linhas de tabela são `<tr>`, que não podem ser envolvidas
pelo mesmo elemento das linhas em `div`.

Então o componente cobre o container e o cabeçalho, e as linhas vêm de um dos
dois mecanismos: seções explícitas, para conteúdo em `div`, ou a própria
tabela, que já se divide sozinha. O contrato visual é um só; o mecanismo é que
difere.

O componente é montado sobre `Paper`, e não sobre `Card`: no esquema escuro o
`Card` do Mantine usa um tom mais claro que a superfície declarada na etapa da
identidade visual, o que criaria duas cores de superfície convivendo no painel.

*Alternativa descartada:* resolver no tema, com um override de `Card`. O tema
não sabe quais cards têm cabeçalho em faixa e quais são cards simples de
conteúdo — aplicaria a faixa em todos.

### D2 — Um componente para o rótulo de seção; o achado 2 é recusado por contraste

Existem cinco cópias de `<Text size="xs" fw={600} tt="uppercase" c="dimmed">`
em cinco arquivos, uma delas já extraída como helper local. Um componente só
elimina as cinco e dá lugar único para o espaçamento entre letras do achado 3.

**O achado 2 não pode ser implementado como escrito.** Ele pede o tom neutro
terciário do protótipo para o rótulo. Medido sobre a faixa do cabeçalho, onde o
rótulo de fato vive, esse tom dá **3,06:1** — abaixo do mínimo de 4,5:1 que o
requisito de contraste de `frontend-visual-theme` já exige, e cuja redação diz
que combinações abaixo do limite *não devem ser usadas pelo painel*.

Não há meio-termo: o tom mais claro que ainda passa sobre a faixa é
essencialmente o que já usamos hoje.

```
tom do protótipo   3,06:1   reprova
um passo mais escuro  3,48   reprova
dois passos           3,93   reprova
três passos           4,48   reprova por pouco
tom atual             4,96   passa
```

O rótulo mantém o tom atual, e o recuo em relação ao corpo vem do outro lever
que o protótipo também usa e que hoje não temos: o espaçamento entre letras do
achado 3, somado à caixa alta e ao tamanho menor que já existem. É plausível
que boa parte da diferença percebida na captura viesse justamente da ausência
do espaçamento.

Decisão do usuário, tomada com os números na mão: respeitar a regra que nós
mesmos escrevemos, em vez de abrir exceção nela. O achado 2 entra na lista de
pontos do protótipo recusados por conflito com uma garantia do painel — a
mesma categoria dos quatro que a etapa da identidade visual já recusou.

*Alternativa descartada:* isentar rótulos de seção do requisito de contraste.
Enfraqueceria uma garantia que vale para o painel inteiro em troca de fidelidade
num rótulo.

*Alternativa descartada:* escurecer a faixa do cabeçalho até o tom do protótipo
passar. A faixa deixaria de ser a do protótipo, e o problema mudaria de lugar
em vez de ser resolvido.

### D3 — A aparência do badge vai para o tema, não para os call sites

São dezenove badges em oito arquivos. A decisão de produto — variante clara,
caixa de sentença — é sobre o que um badge **é** neste painel, não sobre o que
cada tela quer. Vai como padrão de componente no tema: zero call sites
editados, e a próxima tela herda.

Isto também fecha o risco registrado no design da etapa da identidade visual: a
variante preenchida em verde e âmbar com texto branco mede 3,89:1 e 3,72:1,
abaixo do mínimo de 4,5:1; a variante clara mede 4,81:1 e 5,29:1. A regra de
contraste já está na spec daquela capability — o que faltava era o padrão que a
faz valer.

O `text-transform: uppercase` e o peso em negrito são defaults do Mantine, não
do nosso código: os rótulos já são escritos em caixa de sentença. Desligar o
transform devolve o que o texto sempre disse.

*Alternativa descartada:* editar os onze badges de estado e deixar os de status
de mensagem como estão. Manteria duas aparências de badge convivendo no painel,
e a diferença não teria explicação para quem olha.

### D4 — O link de volta entra como cabeçalho de detalhe compartilhado

As três páginas de detalhe repetem a mesma estrutura: título, badge de estado,
descrição e ações à direita. Nenhuma tem navegação de volta.

O componente carrega o link de volta com o rótulo da listagem de origem, mais
essa estrutura. Sem ele, o link de volta seria copiado três vezes junto de tudo
o que já é copiado três vezes.

### D5 — Os filtros da listagem numa linha, sem rótulo visível

O campo de busca perde o rótulo: o texto de exemplo já diz o que ele faz, e o
rótulo duplica a informação ocupando uma linha. O controle segmentado perde os
separadores verticais — que são default do Mantine e o protótipo não tem —,
ganha borda e tamanho compacto, e sobe para a mesma linha do campo.

O texto de exemplo permanece como nome acessível do campo, para a busca
continuar alcançável sem rótulo visível.

### D6 — O defeito: a coluna direita precisa de largura própria

No catálogo de tools, `<Group wrap="nowrap">` impede o container de quebrar, mas
o texto da direita não declara proteção contra encolhimento e é comprimido pela
descrição ao lado. A correção é dar a ele essa proteção.

É o único item da lista que é defeito e não divergência estética, e é anterior
ao redesenho — só ficou visível na comparação lado a lado.

### D7 — Testes cobrem contrato, não pixel

A suíte é cega a cor e layout. O que dá para afirmar: que os componentes
compartilhados existem e recebem o que prometem, que o link de volta aponta para
a rota da listagem, que o campo de busca continua alcançável pelo nome
acessível mesmo sem rótulo visível, e que o padrão de badge está declarado no
tema — este último no teste de tema que já existe, junto das âncoras.

O resto é conferência manual, como nas duas etapas anteriores.

### D8 — Papéis que trocam de ponta da escala precisam de variável, não de tom (descoberto na conferência)

A faixa de cabeçalho do card e da tabela nasceu lendo `gray[1]` direto. Funciona
no claro e **quebra no escuro**: `gray[1]` é um tom claro fixo, e a faixa ficou
branca sobre a tabela escura.

É o mesmo erro do D13 da etapa da identidade visual, cometido de novo num papel
diferente. A causa é a mesma: no esquema claro a superfície sutil é *mais clara*
que a superfície do card; no escuro é *mais escura*. O papel troca de ponta da
escala, e nenhum tom fixo serve para os dois.

A faixa passa a ler `--buteco-surface-subtle`, declarada pelo
`cssVariablesResolver` nos dois esquemas, como o fundo da página já era.

O teste passou a exigir, para as duas variáveis desse tipo, que estejam
declaradas nos dois esquemas **e que tenham valores diferentes entre eles** —
que é a condição que um tom cravado não satisfaz. Uma varredura confirmou que
não sobrou nenhum outro tom fixo em componente.

### D9 — A volta vale para toda tela que não é listagem, não só para o detalhe (descoberto na conferência)

O achado 6 falava só das páginas de detalhe. A conferência mostrou que
**nenhuma** das seis telas de formulário — três de criação, três de edição —
tinha como voltar: sair delas dependia do botão do navegador ou da barra
lateral, exatamente o problema que o achado 6 descreve.

O link de volta foi extraído do cabeçalho de detalhe para um componente próprio,
que o cabeçalho passou a compor e que as seis telas de formulário passaram a
usar. Não é generalização especulativa: são nove telas com a mesma necessidade.

O destino difere por tipo de tela, e a diferença importa. Criação volta para a
**listagem**, que é de onde se veio. Edição volta para o **registro** que está
sendo editado, e não para a lista inteira — de "editar X" quer-se voltar para
X.

Um efeito colateral apareceu no teste de roteamento: a barra lateral e o link de
volta da tela de criação passaram a ter o mesmo nome acessível, e a asserção que
verificava a permanência do layout ficou ambígua. Ela passou a procurar o item
dentro da navegação, que é o que ela sempre quis dizer.

### D10 — O divisor vira regra de irmão adjacente (descoberto na conferência)

A primeira versão do card seccionado desenhava a borda no topo de cada linha,
por estilo embutido, e o cabeçalho não desenhava a sua. Funcionava para card com
faixa, e produzia uma borda solta no topo quando o card não tinha faixa — que é
o caso das abas Ferramentas e Delegações.

Um componente não sabe a sua posição entre irmãos, e é isso que o seletor de
irmão adjacente resolve: a borda vai para `index.css`, mirando a marcação da
linha, e some sozinha da primeira. A faixa volta a desenhar a própria borda
inferior, que é a separação dela para a primeira linha.

A conferência mostrou também **por que** os divisores nasciam recuados nas abas:
o padding horizontal estava no card, não na linha. Com ele na linha, o divisor
vai de borda a borda.

*Custo:* jsdom não carrega `index.css`, então a borda em si deixa de ser
verificável em teste. O que continua verificável é a marcação que a regra mira,
e o teste passou a afirmar isso, dizendo no comentário que a borda é conferida
no navegador. É uma troca consciente: a alternativa era o componente inspecionar
os próprios filhos para saber qual é o primeiro.

### D11 — A url do servidor vai abaixo do nome (descoberto na conferência)

Na aba Ferramentas a url ficava ao lado do nome, disputando a linha com ele e
com os avisos de inativo e de sem tools. É o dado menos consultado dos três e
o mais longo. Passou para baixo do nome, como no protótipo.

Junto: a busca da aba Delegações perdeu o rótulo visível e a largura máxima,
alinhando-se às buscas das listagens (D5).

### D12 — Um guarda estático contra tom fixo de superfície (descoberto na conferência)

O mesmo defeito apareceu **três** vezes entre esta etapa e a anterior: um tom
fixo da escala neutra usado como fundo de superfície. `gray[n]` é claro nos dois
esquemas e `dark[n]` é escuro nos dois, então o que funciona num quebra no
outro.

- fundo da página, corrigido na etapa da identidade visual (D13 de lá);
- faixa de cabeçalho de card e de tabela (D8 desta);
- sessão selecionada no histórico, que virava uma faixa branca com texto claro
  em cima no tema escuro.

Nos três casos a suíte passou verde: jsdom não enxerga cor. Só a comparação com
o protótipo pegou, e uma vez por rodada de conferência.

Entra um teste que varre os componentes procurando linha que declare fundo e
cite um tom da escala neutra. Não é bonito — é análise de texto sobre código —
mas é a única forma de fechar essa porta sem navegador, e o custo é uma varredura
de arquivos por execução.

**O guarda foi verificado reintroduzindo o defeito de propósito**, porque a
primeira versão dele passava verde com o defeito presente: a expressão não
cobria valor dentro de ternário, que é exatamente a forma do caso da sessão
selecionada. Um guarda não verificado é pior que nenhum, porque dá a impressão
de cobertura.

### D13 — Altura do histórico de sessões acompanha a janela

As duas colunas do histórico tinham altura fixa de 500px, o que deixava metade
da tela vazia em monitor alto. Passam a acompanhar a viewport com um mínimo,
como o card de instruções do agente já fazia.

Não é divergência do protótipo — o histórico é tela que ele nunca desenhou. É
correção de aproveitamento de espaço, encontrada na conferência da tela que a
herança do padrão de badge trouxe para revisão.

### Árvore de arquivos

```
apps/frontend/src/
├── theme.ts                          (M) padrão de badge (D3)
├── theme.test.ts                     (M) asserção do padrão
├── components/
│   ├── layout/
│   │   ├── DetailHeader.tsx          (N) cabeçalho de detalhe + volta (D4)
│   │   └── DetailHeader.test.tsx     (N)
│   └── data/
│       ├── SectionedCard.tsx         (N) cabeçalho em faixa + linhas (D1)
│       ├── SectionedCard.test.tsx    (N)
│       ├── SectionLabel.tsx          (N) rótulo em maiúsculas (D2)
│       └── SectionLabel.test.tsx     (N)
└── features/
    ├── agents/
    │   ├── components/AgentTable.tsx           (M) card seccionado
    │   ├── components/AgentOverviewTab.tsx     (M) rótulo compartilhado
    │   ├── components/AgentSkillsCard.tsx      (M) rótulo compartilhado
    │   ├── pages/AgentListPage.tsx             (M) filtros numa linha (D5)
    │   └── pages/AgentDetailPage.tsx           (M) cabeçalho de detalhe
    ├── mcp-servers/
    │   ├── components/McpServerTable.tsx       (M) card seccionado
    │   ├── components/McpServerToolsCatalog.tsx (M) card seccionado + defeito (D6)
    │   ├── components/McpServerAgentsCard.tsx  (M) rótulo compartilhado
    │   ├── components/McpServerConfigCard.tsx  (M) rótulo compartilhado
    │   ├── pages/McpServerListPage.tsx         (M) busca sem rótulo (D5)
    │   └── pages/McpServerDetailPage.tsx       (M) cabeçalho de detalhe
    └── channels/
        ├── components/ChannelTable.tsx         (M) card seccionado
        └── pages/ChannelDetailPage.tsx         (M) cabeçalho de detalhe
```

Nenhum arquivo em `apps/api` ou `apps/workers`. Nenhuma referência entre apps.

## Risks / Trade-offs

**É a etapa com mais superfície de tela do redesenho** — toca as duas
listagens, as três tabelas, os cards de detalhe e as três páginas de detalhe,
e a suíte não vê nada disso. → *Mitigação:* a maior parte da mudança passa por
três componentes compartilhados, então o erro tende a ser sistemático e visível
na primeira tela conferida, não espalhado. E os testes de contrato do D7 pegam
o que é estrutural.

**O padrão de badge muda telas que ninguém conferiu** — os badges de status de
mensagem no histórico de sessões vão junto, e o protótipo não desenhou aquela
tela. → *Mitigação:* é o efeito desejado da decisão de produto, e a variante
clara tem contraste melhor que a preenchida em todos os casos medidos. Entra na
conferência manual.

**Trocar a composição das tabelas pode mexer em alinhamento de coluna** — o
card seccionado muda onde o padding vive. → *Mitigação:* as tabelas têm teste de
conteúdo, que pega perda de célula; o alinhamento fica para a conferência.

**Componentes compartilhados nascidos de três usos podem generalizar cedo
demais** — → *Mitigação:* os três saem de repetição já observada, não de
previsão: cinco cópias do rótulo, três tabelas mais o catálogo com a mesma
estrutura de card, três detalhes com o mesmo cabeçalho. Nenhum deles inventa
caso de uso que não exista hoje.

## Migration Plan

Uma implantação, sem etapas: frontend estático, o build sai inteiro. Nenhuma
migração de dados, feature flag ou compatibilidade a manter.

**Rollback:** reverter o commit. Nenhum estado persistido depende desta
mudança.

## Open Questions

Nenhuma. As duas decisões que estavam abertas — caixa dos badges e alcance da
variante clara — foram fechadas antes desta proposta.
