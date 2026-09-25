import { Box, Group, Stack, Text } from '@mantine/core';
import { CircleAlert } from 'lucide-react';
import type { ErrorInsights } from '../types/systemInsights';
import { formatCount } from '../utils/metricState';
import { caveatsFor } from '../utils/caveatLabels';

// O AVISO DE TASKS SEM ESTADO TERMINAL.
//
// ------------------------------------------------- A FORMA É A DO ARTBOARD
//
// Caixa DISCRETA, no rodapé da página: fundo da superfície sutil, borda fina em
// `yellow[8]`, ícone de círculo com exclamação e título em `yellow[4]`, corpo
// esmaecido em prosa. É o que o `Main.dc.html` desenha, medido nele.
//
// **A primeira versão era um `Alert` preenchido do Mantine, NO TOPO DA PÁGINA,
// com lista de marcadores.** Três divergências, e nenhuma delas tinha decisão
// por trás — foi julgamento do agente de que aviso vai em cima e pesa. O
// protótipo diz o contrário nas três: o aviso fecha a página, e a forma dele é
// de nota de rodapé, não de alarme. Pego pelo dono na sexta rodada.
//
// Um bloco preenchido no topo domina a primeira leitura da tela e empurra os
// números para baixo — e o que ele relata são **8 tasks entre as 14 do
// período**, não uma interrupção de serviço.
//
// ------------------------------------------------ O QUE CONTINUA DIVERGINDO
//
// O CONTEÚDO diverge do protótipo, e isso é decisão registrada (D11), não
// descuido. As três:
//
// 1. O protótipo escreve "2 tasks sem estado terminal **há mais de 30 dias**".
//    A resposta NÃO mede idade: `NonTerminalTasksResponse` traz duas contagens
//    e os estados observados, e nada sobre quando entraram neles. É o que
//    `point-in-time-only` declara.
//
// 2. O protótipo **soma** as duas em "2 tasks". Elas têm causas diferentes:
//    execução aberta aponta para worker travado, nunca consumida para o broker
//    ou para a ausência de consumidor. Somá-las apaga qual dos dois problemas
//    existe — mesma razão pela qual falha e recusa não se somam.
//
// 3. O protótipo oferece **"Ver as tasks"**. Não existe tela que liste
//    exatamente essas tasks; um link para uma listagem que não filtra por isso
//    faria o operador concluir que o aviso mentiu. Entra quando a tela existir.
//
// A prosa substitui a lista de marcadores — a separação das duas populações é
// do texto, como no artboard, e não de uma estrutura visual que o artboard não
// tem.

export interface NonTerminalBannerProps {
  errors: ErrorInsights;
}

export function NonTerminalBanner({ errors }: NonTerminalBannerProps) {
  const { openExecutionCount, neverConsumedCount, observedStates } = errors.nonTerminal;

  // Só aparece quando há o que avisar: dois zeros medidos são uma boa notícia,
  // e um aviso permanente dizendo "0 tasks travadas" vira ruído que ninguém lê.
  if (openExecutionCount === 0 && neverConsumedCount === 0) {
    return null;
  }

  const caveats = caveatsFor(errors.caveats, 'non-terminal');

  // As duas populações em prosa, cada uma com a SUA causa, e sem somar.
  const populacoes = [
    openExecutionCount > 0
      ? `${formatCount(openExecutionCount)} com execução aberta — começaram e não chegaram a um estado final`
      : null,
    neverConsumedCount > 0
      ? `${formatCount(neverConsumedCount)} nunca consumidas — nenhum worker chegou a pegá-las`
      : null,
  ].filter((p): p is string => p !== null);

  return (
    <Box
      data-testid="banner-nao-terminais"
      style={{
        background: 'var(--buteco-surface-subtle)',
        border: '1px solid var(--mantine-color-yellow-filled)',
        borderRadius: 'var(--mantine-radius-md)',
        padding: '14px 16px',
      }}
    >
      <Group align="flex-start" gap={12} wrap="nowrap">
        <CircleAlert
          size={16}
          color="var(--mantine-color-yellow-filled)"
          style={{ flexShrink: 0, marginTop: 1 }}
          aria-hidden="true"
        />
        <Stack gap={3}>
          <Text size="xs" fw={600} c="var(--mantine-color-yellow-filled)">
            Tasks sem estado terminal
          </Text>
          <Text size="xs" c="dimmed" data-testid="nao-terminais-populacoes">
            {populacoes.join('. ')}. Não entram em nenhuma média desta página.
          </Text>
          {observedStates.length > 0 ? (
            <Text size="xs" c="dimmed" data-testid="nao-terminais-estados">
              Estados observados: {observedStates.join(', ')}.
            </Text>
          ) : null}
          {caveats.map((c) => (
            <Text key={c.code} size="xs" c="dimmed" data-testid="nao-terminais-caveat">
              {c.text}
            </Text>
          ))}
        </Stack>
      </Group>
    </Box>
  );
}
