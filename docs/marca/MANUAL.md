# Marca — Buteco Agentes

Símbolo **Robô-garçom** (direção `3a`), versão de cor **`R-D1`**: cabeça em tinta neutra, gravata no acento. Só cores do design system; nenhuma cor nova.

## Arquivos

| Arquivo | Uso |
| --- | --- |
| `simbolo-duotone.svg` | Símbolo isolado, tema claro. Peça principal. |
| `simbolo-duotone-escuro.svg` | Mesmo símbolo no tema escuro. |
| `simbolo-acento.svg` / `simbolo-tinta.svg` | Uma cor só. Para tamanhos pequenos e onde o duotone não cabe. |
| `simbolo-branco.svg` / `simbolo-preto.svg` | Sobre cor e para impressão P&B. |
| `lockup-horizontal-duotone.svg` | Cabeçalho da aplicação, assinatura padrão. |
| `lockup-horizontal-escuro.svg` | Mesmo lockup no tema escuro. |
| `lockup-horizontal-mono-preto.svg` / `-mono-branco.svg` | Timbrado, documento, fundo colorido. |
| `lockup-vertical-*.svg` | Tela de login, capa de apresentação, materiais centralizados. |
| `compacto-*.svg` | Símbolo + `BA`. Cartões e listas com espaço curto. |
| `app-icon.svg` | 1024×1024, quadrado de canto 232 (22,7%). Ícone de aplicativo desktop. |
| `favicon.svg` / `favicon-tinta.svg` | Só a cabeça, olhos maiores. Para 16 e 32px. |

## Regras de uso

- **Abaixo de 32px o duotone não funciona.** A gravata fica com menos de 3px e as duas cores deixam de construir forma. Use `simbolo-acento` ou `simbolo-tinta`.
- **Abaixo de 24px**, use `favicon.svg`: a gravata sai e os olhos crescem.
- **Respiro mínimo** ao redor do símbolo: metade da largura da cabeça.
- **Não** recolorir a gravata fora do acento, não aplicar sombra, gradiente ou contorno, não separar a cabeça da gravata, não girar.
- Cores: tinta `#191a1c`, acento `#2a6ecb`. Tema escuro: tinta `#e9eaec`, acento `#5b8fd4`.

## Ponto de atenção — tipografia

Os lockups usam `<text>` em IBM Plex Sans 600. Dentro da aplicação isso funciona, porque a fonte já está carregada. **Para qualquer uso fora do produto** — apresentação, PDF, material impresso, fornecedor externo — o texto precisa ser convertido em curvas antes, senão a fonte cai para a padrão do sistema. Qualquer editor vetorial faz isso em um comando.

## Construção

Caixa ótica do símbolo: 240 × 320 dentro de um artboard de 512, com 96px de respiro vertical. Raio da cabeça 52px sobre 240 (22%), entre os 25% do quadrado de ícone da navegação e os 23% do ícone de aplicativo. Massa mínima da forma portante: 42px no artboard de 512. Olhos: 56px de diâmetro.
