import { Box, Group, Stack, Text } from '@mantine/core';
import { Info } from 'lucide-react';
import type { MeasuredDays } from '../utils/measuredDays';
import { formatCount } from '../utils/metricState';

// O QUADRO 1 DO `Estados.dc.html` — "coleta recém-iniciada".
//
// Aparece quando a janela pedida começa ANTES da medição, e diz em números o
// que a hachura diz em desenho: quantos dos dias pedidos têm medida e quantos
// não existem.
//
// **Não estava implementado.** A tela desenhava a hachura e não explicava o
// que ela era em lugar nenhum acima do gráfico — o operador via um bloco
// listrado ocupando dois terços da série e tinha de deduzir. Pego pelo dono na
// sétima rodada.
//
// A FRASE QUE FAZ O TRABALHO é a última: **"não são dias sem uso"**. É a
// diferença que esta change inteira defende, dita em quatro palavras e no lugar
// onde ela é lida primeiro — antes dos números, não depois.
//
// ------------------------------------------------------ AS DUAS FONTES, E POR QUÊ
//
// As CONTAGENS saem da série (`measuredDays`), e o INSTANTE de início sai do
// mapa de regimes. É exatamente o corte da D2: a série decide o que foi medido,
// e o regime serve ao TEXTO. Nenhum dia aqui é classificado pelo regime.
//
// Por isso o componente aceita `regimeStart` nulo e continua funcionando: se o
// regime não vier, as contagens continuam certas e só a data some da frase.

export interface PartialMeasurementNoticeProps {
  measured: MeasuredDays;
  /** Início do regime que governa a série. Só para o texto — nunca para classificar dia. */
  regimeStart: string | null;
  timeZone: string;
}

export function PartialMeasurementNotice({
  measured,
  regimeStart,
  timeZone,
}: PartialMeasurementNoticeProps) {
  const total = measured.days.length;
  const medidos = measured.days.filter((d) => d.state !== 'unmeasured').length;
  const naoMedidos = total - medidos;

  // Só no estado que o quadro 1 descreve: janela maior que a medição.
  if (naoMedidos === 0 || total === 0) {
    return null;
  }

  const inicio =
    regimeStart === null
      ? null
      : new Intl.DateTimeFormat('pt-BR', {
          timeZone,
          day: '2-digit',
          month: '2-digit',
          year: 'numeric',
        }).format(new Date(regimeStart));

  return (
    <Box
      data-testid="aviso-medicao-parcial"
      style={{
        background: 'var(--buteco-surface-subtle)',
        border: '1px solid var(--mantine-primary-color-filled)',
        borderRadius: 'var(--mantine-radius-sm)',
        padding: '12px 14px',
      }}
    >
      <Group align="flex-start" gap={10} wrap="nowrap">
        <Info
          size={16}
          color="var(--mantine-primary-color-filled)"
          style={{ flexShrink: 0, marginTop: 1 }}
          aria-hidden="true"
        />
        <Stack gap={3}>
          <Text size="xs" fw={600}>
            O período escolhido é maior que a medição
          </Text>
          <Text size="xs" c="dimmed" data-testid="aviso-medicao-parcial-contagem">
            {inicio === null ? '' : `A coleta começou em ${inicio}. `}
            Dos {formatCount(total)} dias pedidos, {formatCount(medidos)}{' '}
            {medidos === 1 ? 'tem medida' : 'têm medida'} e {formatCount(naoMedidos)}{' '}
            {naoMedidos === 1 ? 'não existe' : 'não existem'} — não são dias sem uso.
          </Text>
        </Stack>
      </Group>
    </Box>
  );
}
