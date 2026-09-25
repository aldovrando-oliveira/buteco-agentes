import { Box, Text } from '@mantine/core';

// A LACUNA DECLARADA — O QUADRO 6 DO `Estados.dc.html`.
//
// Uma linha: o nome do que falta, e o qualificador. Mais a marca
// `data-declared-gap`, que é o que a separa do travessão.
//
// POR QUE ELA EXISTE EM VEZ DE O ELEMENTO SUMIR: elemento ausente é invisível, e
// ninguém volta para procurar o que não aparece. A lacuna declarada é item
// aberto que se vê.
//
// ------------------------------------------------- O QUE ELA *NÃO* FAZ MAIS
//
// **Ela não argumenta.** Nomeia o que falta e para por aí — decisão do dono, em
// 24/09, na terceira rodada de conferência.
//
// A versão anterior carregava um `reason` explicando a causa ("A rota serve o
// cache apenas como total do sistema; reparti-lo por modelo inventaria a
// distribuição"). **O porquê pertence à ISSUE**, e a convenção 23 existe
// exatamente para isso: é a issue que sobrevive ao archive, não o `design.md`
// nem um parágrafo de tela. Quem precisa da causa vai à #51, à #66 ou à #67.
//
// **E ela não fala a língua do backend.** O qualificador foi
// `"não servido por esta rota"`, que é vocabulário de quem escreve o servidor
// numa tela lida por quem OPERA — e que ainda insinuava existir outra rota que
// serviria, o que é falso para a L2 e para a L3.
//
// ------------------------------------------------------------ O QUALIFICADOR
//
// `"não disponível"` nas quatro, decidido pelo dono com o risco na mesa: o
// termo é vizinho de "tente de novo", que é o que o TRAVESSÃO significa. O que
// contém esse risco não é a palavra, é o resto:
//
//   - a marca `data-declared-gap`, e nunca `data-metric-state`;
//   - a moldura tracejada do `block`, que o travessão não tem;
//   - a ausência de qualquer ação de nova tentativa ao lado;
//   - o guarda que proíbe "falhou", "erro" e "tentar de novo" no texto.
//
// Se a confusão aparecer numa conferência futura, é aqui que se mexe — e o
// risco está escrito para que a mudança não pareça capricho.
//
// ------------------------------------------------------------------ VARIANTE
//
// `block` é a moldura tracejada, e vale onde a lacuna substitui um ELEMENTO
// PRÓPRIO do protótipo — uma coluna, um grupo de colunas. Entra no RODAPÉ do
// card, como a D8 especifica para a L3 e a L4.
//
// `inline` é uma linha esmaecida, sem moldura, onde a lacuna substitui um
// SUBTÍTULO. É a L2, e a D8 diz exatamente isso: "o KPI mostra o total; o
// subtítulo vira a lacuna declarada".
//
// **O peso segue o elemento substituído, não o estado.** Usar a moldura no
// subtítulo fez o que falta pesar mais que o que existe.

export interface DeclaredGapProps {
  /** O que o protótipo desenha e não chega a esta tela. */
  label: string;
  /** O que falta. Concorda em número com `label`. */
  qualifier: string;
  /** `block` no rodapé de card; `inline` onde a lacuna substitui um subtítulo. */
  variant?: 'block' | 'inline';
  'data-testid'?: string;
}

export function DeclaredGap({
  label,
  qualifier,
  variant = 'block',
  'data-testid': testId,
}: DeclaredGapProps) {
  const corpo = (
    <Text
      size="xs"
      fw={variant === 'block' ? 500 : 400}
      c={variant === 'block' ? undefined : 'dimmed'}
    >
      {label} —{' '}
      <Text span c="dimmed">
        {qualifier}
      </Text>
    </Text>
  );

  if (variant === 'inline') {
    return (
      <Box data-testid={testId} data-declared-gap="true" data-gap-variant="inline">
        {corpo}
      </Box>
    );
  }

  return (
    <Box
      data-testid={testId}
      data-declared-gap="true"
      data-gap-variant="block"
      style={{
        border: '1px dashed var(--mantine-color-default-border)',
        borderRadius: 'var(--mantine-radius-sm)',
        padding: '10px 12px',
      }}
    >
      {corpo}
    </Box>
  );
}
