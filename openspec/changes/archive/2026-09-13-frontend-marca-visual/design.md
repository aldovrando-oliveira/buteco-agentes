## Context

O pacote de marca está em `~/Downloads/marca`: 21 SVGs, um `README.md` com o
manual de uso e um `INCORPORACAO.md` com um roteiro de seis passos. O roteiro
acerta a estrutura e supõe caminhos `frontend/…` — no monorepo é
`apps/frontend/…`, e todo o resto confere: `src/fonts.ts`, `src/theme.ts`,
`src/components/layout/AppShell.tsx` e `src/features/auth/pages/LoginPage.tsx`
existem exatamente onde ele diz.

### O que os arquivos realmente contêm

O desenho de cada peça é minúsculo — de 429 a 980 bytes. O que engorda os
arquivos para ~8,6 KB é um bloco `<metadata><c2pa:manifest>` em base64:

```
simbolo-duotone.svg   8601 bytes  =   865 de desenho  + 7736 de manifesto C2PA
favicon.svg           8165 bytes  =   429 de desenho  + 7736
app-icon.svg          8601 bytes  =   865 de desenho  + 7736
```

Nenhum gradiente, nenhum filtro, nenhum `<style>`, nenhuma fonte embutida,
`viewBox` + `role="img"` + `aria-label` em todos. Um pacote bem construído.

### O ponto de partida no código

`apps/frontend` não tem `src/assets/`, não tem um único `import` de SVG e não
tem `vite-plugin-svgr` no `package.json` nem no `package-lock.json`. Quando o
`INCORPORACAO.md` diz "o Vite os importa como URL ou como componente React via
`?react`, conforme já for o padrão do projeto", não há padrão a seguir: esta
change cria o primeiro.

Os três pontos de contato hoje:

```
AppShell.tsx:31   ProductMark  → quadrado 24px, raio xs, fundo de destaque, "B" mono
LoginPage.tsx:35  <Title order={2}>Buteco Agentes</Title>
index.html:7      <title>frontend</title>   + favicon roxo #863bff do scaffold Vite
```

### A restrição que define quase tudo

SVG carregado por `<img src>`, `background-image` ou como favicon renderiza em
documento isolado: sem acesso ao CSS da página, sem acesso às `@font-face` que
`src/fonts.ts` registra, sem acesso às variáveis do Mantine. SVG inlineado no
DOM tem acesso aos três.

```
                      ┌───────────────────────────────┐   IBM Plex Sans   var(--mantine-*)
  <img src=svg>    →  │ documento isolado             │        ✗                ✗
  favicon          →  │ sem CSS, sem webfont, sem var │        ✗                ✗
                      └───────────────────────────────┘
  <svg> no JSX     →    mesmo documento, mesmo CSS            ✓                ✓
```

Isso decide D1 e D5 de uma vez.

## Goals / Non-Goals

**Goals:**
- Um componente só desenhando a marca, consumido pelas telas sem decisão de cor,
  de peça ou de tamanho espalhada.
- A marca seguindo o esquema de cor pelo mesmo mecanismo que o resto do painel
  já usa — variável CSS — e não por um `if (isDark)` em componente.
- Os arquivos do pacote entrando no repositório sem edição, para que a próxima
  revisão da marca seja uma substituição de arquivo e nada mais.
- Nenhum requisito de acessibilidade regredindo: o nome do produto continua
  sendo texto HTML real em toda tela onde já é.

**Non-Goals:**
- Usar todas as 21 peças. A change usa três; o resto fica versionado fora do
  bundle ou de fora do repositório.
- Rasterizar qualquer coisa, converter lockup em curvas ou gerar `.icns`/`.ico`.
- Rever a altura do cabeçalho da barra lateral ou qualquer outra medida de
  layout já assentada.

## Decisions

### D1 — `vite-plugin-svgr`, não `<img>`

Alternativa considerada: importar como URL e renderizar em `<img>`. Sem
dependência nova, mas paga caro em três lugares — exige dois arquivos por peça
(claro e escuro), exige um `useComputedColorScheme` dentro do `Logo` para
escolher entre eles, e crava a cor no arquivo, o que transforma D4 num problema
permanente em vez de um que some.

O svgr inlineia o SVG no DOM, onde `currentColor` e `var(--mantine-*)` funcionam.
Isso alinha a marca com o mecanismo que o `theme.ts` inteiro já usa: o comentário
do `cssVariablesResolver` existe justamente para explicar por que papéis que
trocam de ponta entre esquemas viram variável em vez de valor cravado. A marca é
mais um desses papéis.

Custo: uma devDependency. É a única da change.

### D2 — Reescrita de cor em tempo de build, não um arquivo por tema

O svgr aceita `replaceAttrValues`. O mapa fica no `vite.config.ts`:

```ts
svgr({
  svgrOptions: {
    replaceAttrValues: {
      '#191a1c': 'currentColor',
      '#2a6ecb': 'var(--mantine-primary-color-filled)',
    },
  },
})
```

Duas consequências que valem a decisão inteira:

1. **Os seis arquivos `*-escuro.svg` do pacote ficam de fora.** Não existe mais
   "a versão escura": existe uma versão, e ela lê o token. `simbolo-duotone.svg`
   sozinho serve os dois esquemas.
2. **Os arquivos no repositório não são editados.** A reescrita acontece na
   transformação, não no disco. O arquivo versionado continua idêntico ao
   entregue, que é a premissa de D6.

`currentColor` para a tinta, e não uma segunda variável, porque `color` é
herdável: o `Logo` recebe a tinta por `style={{ color: … }}` de quem o usa, e
uma marca sobre fundo colorido — se um dia existir — só precisa herdar outro
`color`, sem prop nova.

### D3 — `--buteco-brand-ink` no resolver, o único desvio do "não mexer no tema"

O `INCORPORACAO.md` diz, no passo 6, que nada muda em `theme.ts`. Quase.

A tinta da marca é `#191a1c` no claro e `#e9eaec` no escuro. Ambas já existem —
são `gray[9]` e `dark[0]` — mas nenhuma variável do Mantine entrega esse par.
`--mantine-color-text` é `theme.black` no claro, e `theme.black` não está
declarado, então vale o default `#000`: preto puro, não a tinta da marca.

O papel troca de ponta entre os esquemas, que é exatamente o critério já usado
para `--buteco-page-bg` e `--buteco-surface-subtle` no `cssVariablesResolver`.
A tinta entra pela mesma porta:

```ts
light: { '--buteco-brand-ink': 'var(--mantine-color-gray-9)' },
dark:  { '--buteco-brand-ink': 'var(--mantine-color-dark-0)' },
```

Nenhuma **cor** nova entra — os dois valores já estão nas rampas. O espírito do
`R-D1` ("ele não pede cor nova") continua de pé; o que faltava era nome para o
papel. `src/theme.test.ts` já guarda âncoras desse tipo, e ganha mais uma.

### D4 — No escuro vale o acento do tema, não o do manual da marca

```
README.md do pacote, tema escuro:   acento #5b8fd4
theme.ts butecoBlue[4], primaryShade.dark: 4:  #6495db
```

Os arquivos `*-escuro.svg` usam `#5b8fd4`. O `theme.ts` deriva o `[4]` de
`color-mix(in oklch, #2a6ecb, white 26%)`, e é ele que pinta
`--mantine-primary-color-filled` no escuro — inclusive o fundo e o texto do item
de navegação ativo, a oito pixels da gravata do logo, na mesma barra lateral.
Dois azuis quase iguais lado a lado lêem como erro, não como escolha.

O tema vence: ele é consumido por 35 telas, o arquivo de marca por uma. Sob D2 a
decisão nem precisa ser executada — a gravata lê o token e o `#5b8fd4` nunca
chega ao bundle. Fica registrada aqui para que a próxima pessoa que abrir o
`README.md` do pacote não "corrija" de volta.

A afirmação do manual de que o `R-D1` usa "só cores do design system" é
verdadeira no claro e falsa no escuro. Vale avisar quem mantém o pacote.

### D5 — Os lockups não entram no produto

O `README.md` do pacote reconhece que os lockups usam `<text>` em IBM Plex Sans
e afirma: *"Dentro da aplicação isso funciona, porque a fonte já está
carregada."* Isso só vale para SVG inlineado no DOM — pela restrição do Context,
via `<img>` ou favicon a fonte não resolve e o texto cai para a sans do sistema.

Mas mesmo inlineado, o lockup é a escolha errada nas duas telas que o
consumiriam, porque **as duas já têm o nome do produto em HTML**:

| Tela | Hoje | O que o roteiro pedia |
|---|---|---|
| `AppShell.tsx:83` | `<Logo/>` + `<Text fw={600}>Buteco Agentes</Text>` | `<Logo variant="horizontal"/>` |
| `LoginPage.tsx:35` | `<Title order={2}>Buteco Agentes</Title>` | lockup vertical acima do form |

Trocar texto HTML — selecionável, buscável pelo Ctrl+F, lido por leitor de tela,
respondendo a `fontSizes` do tema — por `<text>` dentro de um SVG é regressão.
O símbolo entra; o nome continua sendo o texto que já está lá.

Os lockups vão para `docs/marca/`, versionados e fora do bundle, que é onde uma
peça de exportação pertence. O `Logo` nasce sem as variantes `horizontal` e
`vertical`: elas não têm consumidor, e variante sem consumidor é dívida.

> **Revisto em parte por D11**: o lockup vertical passou a ser usado no login.
> O raciocínio acima continua valendo para o lockup *horizontal* na barra
> lateral, onde o `<Text>` ao lado não sai — e a premissa da fonte, que era o
> risco maior, não se aplica a nenhum dos dois quando o SVG é inlineado.

### D6 — C2PA íntegro em tudo, inclusive no bundle

Os manifestos ficam, em todos os caminhos. **Corrigido na implementação**: a
primeira redação desta decisão dizia que o SVGO removeria `<metadata>` do
componente emitido e que `xmlns:c2pa` não sobreviveria à conversão para JSX.
As duas afirmações são falsas, e foram verificadas na fonte:

- `vite-plugin-svgr@5.2.0` passa apenas `[jsx]` como `defaultPlugins`
  (`node_modules/vite-plugin-svgr/dist/index.js:15`). **O SVGO não roda** — o
  `svgo: true` default do `@svgr/core` só tem efeito se `@svgr/plugin-svgo`
  estiver na lista, e ele não é nem instalado como dependência transitiva.
- `xmlns:c2pa` sobrevive limpo: o oxc emite
  `"xmlns:c2pa": "http://c2pa.org/manifest"` sem erro, e o React passa o
  atributo adiante.

O custo real, medido:

```
3 SVGs de src/assets/brand/ no bundle JS
  com C2PA:  25.015 bytes crus  →  3.536 bytes gzip
  sem C2PA:                          457 bytes gzip
  ───────────────────────────────────────────────────
  o manifesto custa            ~3,1 KB gzip
```

Aceito. Duas alternativas foram consideradas e descartadas: instalar
`@svgr/plugin-svgo` (limpa o bundle, mas paga uma segunda devDependency e uma
config de SVGO para não mexer em `viewBox` e ids, por 3,1 KB) e tirar o
manifesto dos três arquivos de `src/` (limpa tudo, mas quebra a premissa de que
o arquivo versionado é idêntico ao entregue).

O que isso significa em cada caminho:

- `public/favicon.svg` e `public/app-icon.svg` são servidos crus: 8,1 KB e
  8,6 KB por visita, contra 429 e 865 bytes sem manifesto. É aqui que a
  proveniência é de fato verificável — o arquivo chega inteiro ao cliente.
- `src/assets/brand/*.svg` viram componente com o manifesto junto. Esses
  3,1 KB não compram proveniência verificável: um fragmento de DOM não carrega
  manifesto que alguém possa checar. É custo puro, aceito em troca de manter os
  arquivos do repositório byte a byte como entregues e a árvore de dependências
  com uma devDependency só.

Consequência a não esquecer: sob `?react` o limiar de inline de 4096 bytes do
Vite não se aplica de forma alguma — o SVG nunca é tratado como asset. Ele vira
componente JavaScript e entra no chunk principal, manifesto junto. Medido no
build: o bundle passou de 889,59 KB / 264,40 KB gzip para 915,08 KB / 269,09 KB
gzip, um acréscimo de **4,7 KB gzip** para as três peças com manifesto.

O `public/` segue outro caminho e não é afetado: `favicon.svg` e `app-icon.svg`
são copiados crus para `dist/`, com os 8.165 e 8.601 bytes intactos.

### D7 — A cabeça a 24px no cabeçalho, não o símbolo a 32px

O `ProductMark` de hoje é 24×24. O símbolo tem proporção 240×320, então a 24px
de altura ele renderiza 18px de largura, com a cabeça em 18×14,4 e a gravata em
~8px. O manual da marca é explícito: *"Abaixo de 32px o duotone não funciona…
Abaixo de 24px, use `favicon.svg`."*

A cabeça sozinha, com os olhos maiores, é a peça desenhada para esse tamanho, e
é quase quadrada, então cai no slot atual sem mexer na altura do cabeçalho da
barra nem no alinhamento com o `<Text>` ao lado. Subir a marca para 32px era a
alternativa; foi descartada por arrastar layout assentado para dentro de uma
change de identidade. Qual das duas versões da cabeça — acento ou tinta — está
em D10.

No `Logo` essa peça é `variant="mark"`. O símbolo completo (`variant="symbol"`)
só aparece no login, onde há espaço vertical.

### D8 — A guarda de tamanho no componente, e o que ela faz

É a única regra do manual que precisa viver em código, porque é a única que uma
tela pode violar sem perceber — um `size` reduzido numa lista densa, meses
depois, quebra o duotone silenciosamente.

```
size < 24   →  variant forçado a 'mark'          (a gravata sai)
size < 32   →  duotone forçado a cor única       (acento)
size >= 32  →  o que foi pedido
```

Implementada por resolução de props dentro do `Logo`, sem lançar erro e sem
exigir nada da chamada. Testável em jsdom sem enxergar pixel: o teste assere
qual peça foi renderizada para cada tamanho.

### D9 — A marca é decorativa onde há texto ao lado

Os SVGs vêm com `role="img"` e `aria-label="Buteco Agentes"`. Inlineados ao lado
do `<Text>Buteco Agentes</Text>` da barra ou do `<Title>Buteco Agentes</Title>`
do login, isso faz um leitor de tela anunciar o nome duas vezes.

O `Logo` renderiza `aria-hidden` e sem `role` por padrão — o mesmo que o
`ProductMark` de hoje já faz com o `aria-hidden` no `<Text>` da letra "B". Não
há, nesta change, nenhum uso da marca desacompanhada de texto; se surgir, a
prop opcional entra junto com o consumidor, não antes (mesmo critério de D5).

### D10 — A cabeça vem da peça de tinta (descoberto na conferência visual)

Na conferência do tema escuro a marca da barra lateral apareceu **azul**. A
causa: o `mark` usava `favicon.svg`, que tem um fill só, `#2a6ecb`. Sob a
reescrita de D2 ele vira `var(--mantine-primary-color-filled)` — acento nos dois
esquemas, `#6495db` no escuro. E o `color: var(--buteco-brand-ink)` que o `Logo`
aplica era código morto nessa variante: a peça não tem nenhum `currentColor`
para herdar.

O `mark` passa a ser `favicon-tinta.svg` — geometria idêntica (mesmo `viewBox`,
mesmo `d`, conferido por hash), fill `#191a1c`, que D2 reescreve para
`currentColor` e o `Logo` alimenta com `--buteco-brand-ink`. Resultado:
`#191a1c` no claro, `#e9eaec` no escuro.

Três razões, além de resolver o sintoma:

1. **É como a marca se constrói.** No `simbolo-duotone` a cabeça é tinta e só a
   gravata é acento. O `mark` *é* a cabeça — pintá-la de acento contrariava o
   próprio desenho.
2. **Devolve exclusividade ao acento na barra.** Com a marca em tinta, o azul
   passa a significar só "você está aqui", no item de navegação ativo. Era o
   problema que D4 tinha previsto pelo lado errado: eu esperava divergência de
   *tom* entre dois azuis, e o que havia era azul demais.
3. **`#e9eaec` é o branco que a marca define para o escuro.** Não `#ffffff`: o
   `simbolo-branco.svg` do pacote é especificado para uso *sobre cor*, não para
   interface escura.

Alternativa considerada: manter o acento no claro e usar a tinta só no escuro,
via uma variável `--buteco-brand-mark` no resolver. Continuaria sendo CSS puro,
sem `if (isDark)` em componente, mas faria o papel da cor da marca mudar entre
esquemas de um jeito que o manual não descreve — e não resolveria (1) nem (2).

`favicon.svg`, a cabeça em acento, continua sendo a peça certa em `public/`: o
favicon do navegador senta sobre uma barra de cor desconhecida e precisa ser uma
mancha reconhecível, que é contexto diferente do interior do painel. Ele sai
apenas de `src/assets/brand/`, e uma cópia fica em `docs/marca/` como
`favicon-acento.svg` para uso externo.

### D11 — O lockup vertical no login (revisão de D5, pedida na conferência visual)

D5 tirou os lockups do produto inteiro. Para o login isso foi longe demais, e a
conferência visual mostrou por quê: símbolo e `<Title>` empilhados por mim não
são a mesma coisa que o lockup. O pacote desenhou o espaçamento óptico entre
símbolo e nome, a escala do texto contra a altura do símbolo e o
`letter-spacing` de -0.63 — nada disso sobrevive a um `<Stack gap="xs">`.

O login passa a usar `lockup-vertical-duotone.svg`. Os dois receios de D5 se
comportam assim aqui:

1. **A fonte resolve.** O receio era o `<text>` cair na sans do sistema. Isso só
   acontece via `<img>` ou favicon; inlineado pelo svgr, o `<text>` alcança as
   `@font-face` de `src/fonts.ts`, que carrega IBM Plex Sans 600 — o peso exato
   que o lockup pede. Por D1 o painel já está no caminho certo.
2. **A cor também resolve.** O `<text>` entra com `fill="#191a1c"`, que D2
   reescreve para `currentColor`. O nome desenhado lê `--buteco-brand-ink` e
   troca de tema junto com o resto, sem arquivo separado.

O que se perde de fato, e é o custo aceito: **o nome deixa de ser texto
selecionável e buscável pelo Ctrl+F** nessa tela. É uma tela de login, com uma
ocorrência do nome e nenhum fluxo que dependa de copiá-lo.

O que **não** se perde é a acessibilidade, porque a composição a preserva de
propósito:

```tsx
<Title order={2}>
  <Logo variant="vertical" size={110} />   {/* aria-hidden, desenha o nome */}
  <VisuallyHidden>Buteco Agentes</VisuallyHidden>
</Title>
```

O `<h2>` continua existindo e continua nomeado — a semântica de cabeçalho não
dependia do texto estar visível. O lockup segue decorativo por D9, então o nome
é anunciado uma vez só. `VisuallyHidden` do Mantine renderiza um `<span>`, e
`<svg>` é phrasing content: os dois são válidos dentro de um `<h2>`, o que um
`<div>` não seria.

A guarda de D8 ganha um degrau. No lockup vertical o símbolo ocupa 120 das 200
unidades de altura, então a 54px de altura total a gravata cruza o limiar do
duotone. Abaixo disso o lockup **dá lugar ao símbolo**, em vez de trocar de
peça: os `lockup-*-mono-*` do pacote têm `#000` e `#fff` cravados e não leem os
tokens do tema, então não servem de degradação. O símbolo, sim, tem a cadeia
completa.

## Risks / Trade-offs

- **O `replaceAttrValues` é global, não por arquivo** → Aplica-se a todo SVG
  transformado por `?react`. Hoje os únicos SVGs do projeto são os da marca, e
  `public/` não passa pelo plugin. Se um dia entrar um SVG que precise manter
  `#2a6ecb` literal, a saída é `?react&…` com config por query ou um segundo
  padrão de import — sinalizado aqui para não virar surpresa.

- **`app-icon.svg` tem o acento cravado no `<rect>` de fundo** → Ele vive em
  `public/`, fora do svgr, e é assim que deve ser: ícone de aplicativo não segue
  o tema do painel, segue o sistema operacional. Se o acento da marca mudar, o
  arquivo é substituído. Não é dívida, é a natureza da peça.

- **Nenhum teste do projeto enxerga aparência** → jsdom não renderiza SVG nem
  calcula contraste. Os testes garantem qual peça foi escolhida, que a guarda
  disparou e que a marca está fora da árvore de acessibilidade. Que o azul da
  gravata bate com o do item ativo no escuro é conferência visual manual, nos
  dois esquemas, como em toda change visual deste projeto.

- **O favicon do produto muda para quem já tem o roxo em cache** → Navegador
  costuma reter favicon agressivamente. É cosmético e se resolve sozinho; não
  justifica versionar o nome do arquivo.

- **O `tsc -b` quebra sem os tipos do svgr** → `?react` não é conhecido pelo
  TypeScript. `src/vite-env.d.ts` precisa da referência a
  `vite-plugin-svgr/client` na mesma tarefa que instala o plugin, senão o build
  falha e o teste passa (o Vitest transforma, o `tsc` não roda).

## Open Questions

- O `README.md` do pacote declara `#5b8fd4` como acento escuro, divergindo do
  `#6495db` do tema (D4). A change resolve em favor do tema, mas **quem mantém o
  pacote deveria ser avisado** — ou o manual se alinha ao `theme.ts`, ou a
  próxima entrega reintroduz a divergência.
- Os lockups em `docs/marca/` continuam com o texto em fonte, não em curvas.
  Quem for usá-los em apresentação ou material impresso precisa converter antes.
  Vale um aviso no `README.md` de `docs/marca/`; não é tarefa de código.
- O `compacto-*.svg` (símbolo + "BA") não tem consumidor hoje. Fica de fora por
  D5; se aparecer uma lista densa que precise dele, entra como variante nova.
