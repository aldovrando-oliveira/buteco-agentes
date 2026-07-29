## Context

`apps/frontend` (React + TypeScript + Vite + Mantine) tem hoje três telas de agente
implementadas em mudanças anteriores (`frontend-cadastro-agentes`,
`frontend-atualizacao-ativacao-agentes`): `AgentForm` (compartilhado por criação e
edição), `AgentDetailPage` + `AgentDetailCard` (visualização e ações), e
`AgentListPage`/`AgentTable` (fora de escopo aqui).

Feedback visual real de uso apontou três problemas:

1. `AgentForm` não tem botão de cancelar — só o botão de submit existe.
2. `AgentDetailPage` renderiza o bloco de ações (Editar, Ativar/Desativar) *depois*
   do `AgentDetailCard`; com um prompt de instruções longo, o card estica a página
   e o usuário precisa rolar para encontrar os botões.
3. `AgentDetailCard` renderiza `instructions` como `<Text>` (texto corrido), mas o
   conteúdo real já é escrito em markdown pelos usuários (`#` títulos, `**negrito**`,
   listas com `-`, `---` como separador, e também tabelas e strikethrough,
   confirmados como presentes em uso real).

Os problemas #2 e #3 interagem diretamente: renderizar markdown formatado (títulos,
listas) ocupa mais altura vertical que o texto corrido atual, então corrigir #3 sem
revisitar #2 pioraria a rolagem que #2 já descreve. As duas mudanças precisam ser
desenhadas em conjunto.

Verificado por leitura direta do código e do pacote instalado (não assumido de
memória de treinamento):

- `@mantine/core@9.4.2` está instalado. O componente de tipografia hoje se chama
  `Typography` — confirmado em `node_modules/@mantine/core/lib/components/`, sem
  nenhuma referência a `TypographyStylesProvider` (nome anterior à v9.0.0, já
  removido).
- `ScrollArea` já é parte de `@mantine/core` (nenhuma dependência nova necessária
  para o container com rolagem).
- Não há nenhum uso de `ScrollArea`, CSS Modules, ou `mah`/`max-height` em nenhum
  lugar de `apps/frontend/src` hoje — não existe precedente de nenhuma das duas
  abordagens de container com altura limitada.
- O app tem dark mode funcional (`MantineProvider defaultColorScheme="light"` em
  `main.tsx` + toggle via `useMantineColorScheme` em `AppShell.tsx`).
- O `Modal` de confirmação de desativação em `AgentDetailPage.tsx` já tem um botão
  "Cancelar" com `variant="default"` — esse modal fica fora de escopo (não muda),
  mas estabelece um precedente visual dentro da mesma feature.
- `npm view react-markdown dist-tags` e `npm view remark-gfm dist-tags` confirmam
  `latest: 10.1.0` e `latest: 4.0.1`, respectivamente, no momento desta proposta —
  não pré-lançamentos.

## Goals / Non-Goals

**Goals:**
- `AgentForm` sempre oferece um caminho de volta explícito (Cancelar), tanto em
  criação quanto edição.
- O bloco de ações em `AgentDetailPage` fica visível sem rolagem, independente do
  tamanho do prompt.
- O conteúdo de `instructions` é renderizado com a formatação markdown que os
  usuários já escrevem, sem deixar a página crescer sem limite.

**Non-Goals:**
- Preview de markdown no formulário de criação/edição — o `Textarea` de
  `instructions` continua sendo edição de texto puro; markdown é só na
  visualização (`AgentDetailCard`).
- Confirmação de "descartar alterações" ao clicar em Cancelar com o formulário
  sujo — cancelar sempre volta direto, sem diálogo.
- Qualquer mudança em `apps/api` ou `apps/workers` — o campo `instructions` já é
  uma string simples na API; só a renderização no frontend muda.
- Qualquer mudança em `AgentTable`/`AgentListPage`.
- Qualquer mudança no `Modal` de confirmação de desativação já existente em
  `AgentDetailPage` (já resolve o problema que deveria resolver).

## Decisions

### Decision 1 — Reposicionar os botões de ação E limitar a altura do bloco de instruções (interação #2/#3)

Mover o `Group` com Editar/Ativar/Desativar em `AgentDetailPage` para **antes** do
`AgentDetailCard` no JSX resolve a visibilidade imediata dos botões — mas sozinho
não evita que a página fique muito longa com um prompt grande, porque o card ainda
cresce sem limite abaixo dos botões.

Por isso, dentro de `AgentDetailCard`, o bloco de `instructions` renderizado como
markdown também recebe um container de altura máxima com rolagem interna via
`ScrollArea` do `@mantine/core` (não `max-height` + `overflow-y: auto` em CSS
puro). Razões:

- `ScrollArea` já é parte de `@mantine/core`, já instalado — zero dependência
  nova.
- Dá uma scrollbar com estilo próprio, consistente com o resto do app **inclusive
  em dark mode** (confirmado que o app tem dark mode funcional) — CSS puro usaria
  a scrollbar nativa do SO/navegador, que destoa visualmente entre plataformas e
  entre os dois temas.
- Não há precedente de nenhuma das duas abordagens no projeto ainda, então a
  escolha é livre; dado que o app já é construído inteiramente com primitivas
  Mantine, `ScrollArea` é a opção consistente com o restante da stack de UI.

**Revisão pós-implementação (feedback visual real, com o agente já rodando):**
a primeira versão desta decisão usava uma altura máxima fixa (`mah={400}`, 400px).
Em uso real, isso se mostrou pequeno demais em viewports maiores — parte do
prompt ficava fora da área visível sem indicação clara de que havia rolagem
(o `ScrollArea` do Mantine só mostra a scrollbar ao passar o mouse por padrão),
dando a impressão de que o conteúdo estava sendo cortado. A altura foi trocada de
um valor fixo em pixels para uma altura que usa o espaço disponível da viewport:

```tsx
<ScrollArea h="calc(100vh - 320px)" mih={220}>
```

- `h="calc(100vh - 320px)"` — altura explícita que acompanha o espaço vertical
  disponível abaixo do cabeçalho fixo do `AppShell` (60px) e do restante do
  chrome da página (bloco de ações, título/badge, datas, paddings do `Card` e
  do `AppShell.Main`); 320px é uma estimativa somada desse chrome, não um valor
  exato calculado pixel a pixel.
- `mih={220}` — altura mínima (piso), garante uma área de leitura utilizável
  mesmo em viewports muito baixas (ex.: janela redimensionada, zoom alto), caso
  o `calc()` acima resolva para um valor pequeno demais.
- Confirmado por leitura do resolver de style props do Mantine
  (`spacing-resolver.mjs` → `rem()`) que uma string começando com `calc(` passa
  para o `style` inline sem nenhuma transformação — tanto `h` quanto `mah`
  aceitam `calc()` livremente, não só números/tokens de tema.

**Segunda revisão pós-implementação (bug real encontrado via browser real, não
só jsdom):** a versão anterior desta decisão usava `mah="calc(100vh - 320px)"`
(altura máxima) em vez de `h` (altura explícita) — os testes automatizados
(jsdom) passavam, mas verificação manual com Playwright dirigindo um Chromium
real (via `chromium.launch` + interceptação de rede da API, já que docker não
estava disponível para subir o stack completo) mostrou que a rolagem
continuava travada: o conteúdo era cortado visualmente (`overflow: hidden` no
container correto), mas o mouse wheel não movia o scroll. Causa raiz confirmada
inspecionando estilos computados no browser: o `ScrollArea` do Mantine renderiza
internamente um `Viewport` (`.mantine-ScrollArea-viewport`) estilizado com
`height: 100%` — e uma altura em porcentagem só resolve contra um pai com altura
**explícita**; um pai com apenas `max-height` (altura própria efetivamente
`auto`, só com um teto visual) não conta, então o `Viewport` crescia para caber
todo o conteúdo (`height` igual ao `scrollHeight` do conteúdo) em vez de ser
limitado à altura do `Root` — `scrollHeight === clientHeight` no `Viewport`,
logo não havia nada para rolar, mesmo com o `Root` cortando visualmente o
excedente. Trocar `mah` por `h` corrige isso porque dá ao `Root` uma altura
explícita, contra a qual o `height: 100%` do `Viewport` resolve corretamente
(confirmado depois da troca: `Viewport` com `height` igual à do `Root`, `scroll
Top` responde ao wheel). Esse é o comportamento documentado nos próprios
exemplos do Mantine para `ScrollArea` — todos usam `h`, nunca só `mah`, por
este motivo exato.

Efeito colateral aceito: com `h` (não `mah`), o container sempre ocupa a altura
calculada, mesmo para prompts curtos que caberiam em bem menos espaço — sobra
área vazia com scrollbar inativa nesses casos. Isso é, na prática, uma leitura
ainda mais literal do pedido original ("utilize a altura disponível") do que a
versão anterior com `mah`, e é a única forma correta de manter `ScrollArea` do
Mantine funcional — a alternativa seria abandonar `ScrollArea` em favor de
`overflow-y: auto` em CSS puro (que não tem essa limitação, já que não separa
"container" de "viewport interno"), o que reabriria a Decision 1 original já
descartada por consistência visual com o resto do app.

Assim como o valor original de 400px, o offset de 320px é um ajuste inicial
razoável, não um valor contratual do design — revisão visual pode reajustá-lo
sem precisar de outra Decision.

Alternativas consideradas:
- **Só mover os botões, sem limitar altura do card**: rejeitada — não resolve o
  problema descrito no proposal de página ficando muito longa com prompt grande,
  contraria a orientação explícita de desenhar #2 e #3 juntos.
- **`max-height` + `overflow-y: auto` (CSS puro)**: rejeitada — funciona, mas usa
  scrollbar nativa inconsistente entre SO/navegador/tema, enquanto o app já é 100%
  Mantine.
- **Colapsar/expandir ("mostrar mais") em vez de rolagem**: rejeitada — introduz
  um padrão de interação novo e um estado (`expanded`/`collapsed`) não pedido no
  escopo; rolagem interna resolve o mesmo problema com menos código e é o padrão
  já sugerido no proposal.
- **Altura máxima fixa em pixels (`mah={400}`)**: era a decisão original;
  superada pelo feedback visual real descrito acima — não escala com o tamanho
  real da viewport do usuário, então tanto corta conteúdo cedo demais em telas
  grandes quanto pode sobrar espaço vazio em telas pequenas.
- **`mah` (altura máxima) em vez de `h` (altura explícita) para o espaço
  calculado via viewport**: era a segunda versão desta decisão; superada pelo
  bug real descrito acima (`Viewport` interno do `ScrollArea` não se limita
  corretamente sem uma altura explícita no `Root`) — só descoberta ao testar em
  um browser real, não nos testes automatizados em jsdom.
- **Layout flex ocupando 100% da altura restante da viewport, com
  `AppShell.Main`/página inteira reestruturados como flex column (em vez de
  `h="calc(100vh - Npx)"` com offset estimado)**: rejeitada por ora — resolveria
  o mesmo problema de forma mais "exata" (sem estimar um offset em pixels), mas
  exige tornar `AppShell.Main` e o layout da página um container flex com altura
  100% amarrada à viewport, o que é uma mudança estrutural no layout
  compartilhado (`AppShell.tsx`) fora do escopo desta change; `calc(100vh -
  Npx)` resolve o problema relatado sem essa reestruturação.

### Decision 2 — Biblioteca de markdown: react-markdown + remark-gfm, dentro de Typography

`react-markdown@10.1.0` (confirmado como `latest` via `npm view react-markdown
dist-tags`, não pré-lançamento) é a biblioteca usada para renderizar
`instructions`. Ela renderiza para elementos React reais (via
`hast-util-to-jsx-runtime`), sem `dangerouslySetInnerHTML` — confirmado lendo a
árvore de `dependencies` do pacote (`unified`/`remark-parse`/`remark-rehype`
padrão, nada de HTML bruto) — e é a opção mais adotada do ecossistema para esse
caso de uso.

`remark-gfm@4.0.1` (confirmado como `latest` via `npm view remark-gfm dist-tags`)
entra como plugin porque tabelas e strikethrough foram confirmados como presentes
em prompts reais em uso — não é uma adição especulativa; sem essa confirmação, a
decisão teria sido não adicionar (mesmo raciocínio de não adicionar uma
biblioteca de ícones sem uso real já aplicado em decisões anteriores deste
projeto).

A saída do `react-markdown` é envolvida em `Typography` do `@mantine/core` para
herdar a tipografia do resto do app (espaçamento de títulos, listas, negrito,
etc. consistentes com o design system). Confirmado por leitura direta do pacote
instalado (`node_modules/@mantine/core/lib/components/Typography`, sem nenhuma
referência a `TypographyStylesProvider`) que na v9.4.2 instalada o componente já
se chama `Typography` — o nome antigo (`TypographyStylesProvider`, anterior à
v9.0.0) não existe mais nessa versão.

Estrutura de composição em `AgentDetailCard`:

```tsx
<ScrollArea h="calc(100vh - 320px)" mih={220}>
  <Typography>
    <ReactMarkdown remarkPlugins={[remarkGfm]}>
      {agent.instructions}
    </ReactMarkdown>
  </Typography>
</ScrollArea>
```

Alternativas consideradas:
- **`marked` + `dangerouslySetInnerHTML`**: rejeitada — abre superfície de XSS
  desnecessária (o campo `instructions` é escrito pelo próprio usuário, mas ainda
  assim renderizar HTML bruto é uma prática insegura por padrão); `react-markdown`
  resolve o mesmo problema com risco zero e sem sanitização adicional necessária.
- **`react-markdown` sem `remark-gfm`**: era a opção default antes da
  confirmação; descartada porque tabelas/strikethrough foram confirmados em uso
  real.
- **CSS customizado em vez de `Typography`**: rejeitada — reinventaria estilos que
  o Mantine já define e mantém consistentes com o resto do app (incluindo dark
  mode).

### Decision 3 — `onCancel` como prop obrigatória em AgentForm

`AgentForm` ganha `onCancel: () => void` como prop **obrigatória** (não opcional),
com o botão "Cancelar" sempre renderizado ao lado do botão de submit — nunca
condicional. Os dois callers atuais (`AgentCreatePage`, `AgentEditPage`) sempre
fornecem um destino de volta determinístico; não existe cenário de uso do
`AgentForm` sem um "voltar" que faça sentido. Prop obrigatória evita checagem
defensiva de `undefined` e renderização condicional do botão que nunca seria
exercitada na prática.

- `AgentCreatePage` passa `onCancel={() => navigate('/agents')}`.
- `AgentEditPage` passa `onCancel={() => navigate(`/agents/${id}`)}`.

Ambos usam destino explícito via `useNavigate` (já usado nos dois componentes),
não `navigate(-1)`/histórico do navegador — mesma preferência por comportamento
determinístico já usada nas decisões de navegação existentes nesses dois
componentes (redirecionamento após sucesso do submit já segue esse padrão).

Alternativa considerada:
- **`onCancel` opcional, botão condicional**: rejeitada — nenhum caller real
  precisaria omitir o callback, então a opcionalidade só adicionaria um branch de
  renderização morto e a possibilidade (nunca usada) de um formulário sem
  Cancelar.

### Decision 4 — Variant do botão Cancelar: `default`, por consistência com o Cancelar já existente

O botão "Cancelar" novo em `AgentForm` usa `variant="default"`, o mesmo variant já
usado pelo botão "Cancelar" existente no `Modal` de confirmação de desativação em
`AgentDetailPage.tsx`. Essa é uma divergência intencional da sugestão inicial
(`subtle`/`outline`) — feita para manter os dois "Cancelar" da mesma feature
visualmente consistentes, já que ambos aparecem em fluxos próximos (detalhe →
edição) e um usuário pode ver os dois na mesma sessão de uso.

### Decision 5 — Estrutura de pastas final

Nenhuma pasta nova. Todos os arquivos afetados já existem em
`apps/frontend/src/features/agents/`, exceto o teste novo de
`AgentDetailCard`:

```
apps/frontend/
├── package.json                        (MODIFICADO — + react-markdown@10.1.0, remark-gfm@4.0.1)
└── src/
    └── features/
        └── agents/
            ├── components/
            │   ├── AgentForm.tsx        (MODIFICADO — prop onCancel obrigatória + botão Cancelar)
            │   ├── AgentForm.test.tsx   (MODIFICADO — novo caso: clicar em Cancelar chama onCancel)
            │   ├── AgentDetailCard.tsx  (MODIFICADO — instructions via react-markdown + remark-gfm, dentro de Typography + ScrollArea)
            │   └── AgentDetailCard.test.tsx (NOVO)
            └── pages/
                ├── AgentCreatePage.tsx  (MODIFICADO — passa onCancel navegando para /agents)
                ├── AgentEditPage.tsx    (MODIFICADO — passa onCancel navegando para /agents/{id})
                ├── AgentDetailPage.tsx  (MODIFICADO — bloco de ações antes do AgentDetailCard)
                └── AgentDetailPage.test.tsx (MODIFICADO — novo caso: ordem no DOM)
```

## Risks / Trade-offs

- [Conteúdo de `instructions` com markdown malformado ou muito aninhado pode
  gerar layout inesperado dentro do `ScrollArea`] → Mitigação: `react-markdown`
  já degrada de forma previsível (trata sintaxe inválida como texto literal, não
  quebra o parse); o `ScrollArea` contém qualquer overflow de altura, e overflow
  horizontal de blocos de código/tabelas largas é tratado pelo `overflow-x` padrão
  do `Typography`/`ScrollArea` do Mantine.
- [Testes existentes em `AgentDetailPage.test.tsx` usam
  `screen.getByText(activeAgent.instructions)` contra um texto de fixture sem
  sintaxe markdown (`'Você é um atendente simpático.'`)] → Mitigação: texto puro
  sem marcação renderiza como um único nó de texto dentro do `<p>` gerado pelo
  `react-markdown`, então o matcher por texto exato deve continuar funcionando
  sem alteração; validar ao rodar a suíte durante a implementação (tasks.md
  inclui esse item).
- [Duas novas dependências de terceiros (`react-markdown`, `remark-gfm`) no
  bundle do frontend] → Mitigação: ambas são bibliotecas maduras, amplamente
  adotadas, sem `dangerouslySetInnerHTML`; sem alternativa mais simples que
  cubra markdown real com a mesma segurança por padrão.

## Migration Plan

Não aplicável — mudança é puramente de frontend, sem estado persistido, sem
migração de dados, sem contrato de API alterado. Deploy é o build normal de
`apps/frontend`; rollback é reverter o commit/build anterior.

## Open Questions

Nenhuma incerteza de negócio/produto em aberto — as três decisões que exigiam
input do usuário (presença real de tabelas/strikethrough nos prompts, variant do
botão Cancelar, e escolha entre `ScrollArea` e CSS puro) foram confirmadas durante
a exploração desta mudança.
