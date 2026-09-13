# Marca — peças de exportação

As peças da marca que **não** são usadas pelo produto. O painel consome apenas
três arquivos, e eles não moram aqui:

| No produto | Onde |
| --- | --- |
| `simbolo-duotone.svg`, `simbolo-acento.svg`, `favicon-tinta.svg`, `lockup-vertical-duotone.svg` | `apps/frontend/src/assets/brand/` |
| favicon do site e ícone de aplicativo | `apps/frontend/public/` |

Este diretório existe para material que sai do produto — apresentação, PDF,
timbrado, fornecedor externo. Nada aqui entra no bundle.

`favicon-acento.svg` é a cabeça em `#2a6ecb` — a versão que serve de favicon do
navegador (`apps/frontend/public/favicon.svg`), onde a marca senta sobre uma
barra de cor desconhecida. Dentro do painel a cabeça vai na tinta, não no
acento; ver a decisão D10.

`MANUAL.md` é o manual da marca como entregue, com as regras de tamanho, respiro
e uso de cor. Leia antes de usar qualquer peça.

## Antes de usar os lockups em qualquer material

Os `lockup-*.svg` e os `compacto-*.svg` escrevem o nome do produto com `<text>`
em IBM Plex Sans 600 — texto vivo, não curvas. **Converta o texto em curvas
antes de entregar a fornecedor, gerar PDF ou montar apresentação.** Sem isso a
fonte cai para a padrão do sistema de quem abrir o arquivo. Qualquer editor
vetorial faz em um comando.

O **lockup vertical é exceção**: ele assina a tela de login e mora em
`apps/frontend/src/assets/brand/`, não aqui. Lá o `<text>` resolve, porque o SVG
é inlineado no DOM e alcança as `@font-face` do painel — a conversão em curvas
só é necessária fora do produto. O lockup **horizontal** não é usado: na barra
lateral o nome já é um `<Text>` HTML, selecionável e buscável. Ver as decisões
D5 e D11 em `openspec/changes/archive/*-frontend-marca-visual/design.md`.

## Divergência conhecida no manual

`MANUAL.md` declara `#5b8fd4` como acento do tema escuro. O painel usa `#6495db`
— `butecoBlue[4]` de `apps/frontend/src/theme.ts` — que é o mesmo azul do item
de navegação ativo. **Para uso dentro do produto vale o tema**; os arquivos
`*-escuro.svg` daqui carregam o azul do manual e servem só para material
externo, onde não há item de navegação ao lado para destoar.

## Proveniência

Todos os SVGs mantêm o manifesto C2PA (`<metadata><c2pa:manifest>`) como
entregues. Não edite os arquivos no lugar: substitua-os por uma revisão nova do
pacote, senão a assinatura deixa de conferir.
