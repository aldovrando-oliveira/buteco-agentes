import { Box, Card, Group, Stack, Text } from '@mantine/core';
import type { MeasuredDays } from '../utils/measuredDays';
import { METRIC_SIZE, formatCount, type QueryState } from '../utils/metricState';

// "Tasks por dia" — SVG INLINE, SEM BIBLIOTECA DE GRÁFICOS (D13).
//
// A LINHA QUEBRA NO TRECHO NÃO MEDIDO, E NÃO CAI A ZERO.
//
// É a diferença que o quadro 1 do `Estados.dc.html` desenha: a faixa hachurada
// é o trecho sem medição, e uma linha que descesse até o eixo ali afirmaria
// "zero tasks naquele dia" — inventando um período de inatividade que nunca
// existiu. O dia medido e vazio, esse SIM entra como ponto em zero, porque
// alguém contou.
//
// O SVG é desenhado em coordenadas de viewBox e escalado por CSS: sem medir o
// DOM, sem ResizeObserver, e portanto testável em jsdom — que não faz layout.

const VIEW_W = 600;
const VIEW_H = 120;
const PAD = 4;

// A HACHURA É CSS, E NÃO `<pattern>` DE SVG — como o protótipo faz.
//
// A primeira versão usava um `<pattern>` com `patternTransform="rotate(135)"`
// dentro de um SVG com `viewBox="0 0 600 120"` e `preserveAspectRatio="none"`.
// O SVG é esticado ~2× na horizontal e 1× na vertical, e a escala NÃO UNIFORME
// deforma o padrão junto: as listras saíam rarefeitas e quase invisíveis.
//
// A faixa passa a ser uma camada absoluta atrás do desenho, com o mesmo
// `repeating-linear-gradient` do `Estados.dc.html` — 135°, período de 10px com
// 5px de traço. Fora do SVG, nada a estica.
const HACHURA =
  'repeating-linear-gradient(135deg, transparent, transparent 5px, var(--buteco-surface-subtle) 5px, var(--buteco-surface-subtle) 10px)';

export interface DailyTasksCardProps {
  measured: MeasuredDays;
  queryState: QueryState;
}

export function DailyTasksCard({ measured, queryState }: DailyTasksCardProps) {
  const { days, maxMeasuredCount } = measured;
  const max = maxMeasuredCount ?? 0;

  const x = (i: number) =>
    days.length <= 1 ? VIEW_W / 2 : PAD + (i * (VIEW_W - 2 * PAD)) / (days.length - 1);
  const y = (count: number) =>
    max <= 0 ? VIEW_H - PAD : VIEW_H - PAD - (count / max) * (VIEW_H - 2 * PAD);

  // Os segmentos são cortados nos dias não medidos: cada corrida contígua de
  // dias medidos vira um `polyline` próprio. É isso que faz a linha QUEBRAR em
  // vez de atravessar o buraco.
  const segmentos: { i: number; count: number }[][] = [];
  let atual: { i: number; count: number }[] = [];
  days.forEach((dia, i) => {
    if (dia.state === 'unmeasured') {
      if (atual.length > 0) segmentos.push(atual);
      atual = [];
      return;
    }
    atual.push({ i, count: dia.taskCount as number });
  });
  if (atual.length > 0) segmentos.push(atual);

  // As faixas hachuradas: cada corrida contígua de dias NÃO medidos.
  const faixas: { inicio: number; fim: number }[] = [];
  let inicio: number | null = null;
  days.forEach((dia, i) => {
    if (dia.state === 'unmeasured') {
      if (inicio === null) inicio = i;
      if (i === days.length - 1) faixas.push({ inicio, fim: i });
      return;
    }
    if (inicio !== null) {
      faixas.push({ inicio, fim: i - 1 });
      inicio = null;
    }
  });

  const medidos = days.filter((d) => d.state !== 'unmeasured');
  const contagens = medidos.map((d) => d.taskCount as number);
  const minMeasuredCount = contagens.length === 0 ? null : Math.min(...contagens);

  // O EIXO DE DATAS DO PROTÓTIPO: três marcas — primeira, do meio e última.
  // Datas são as do fuso DA RESPOSTA, porque `measured.days` já nasceu nele.
  // `Set` porque em janela curta as três colapsam, e repetir a mesma data seria
  // ruído.
  const marcas = [...new Set(
    days.length === 0
      ? []
      : [days[0], days[Math.floor((days.length - 1) / 2)], days[days.length - 1]].map(
          (d) => d.day,
        ),
  )].map((day) => {
    const [ano, mes, dia] = day.split('-');
    return { day, texto: `${dia}/${mes}`, ano };
  });

  // A LEGENDA DA HACHURA É CONDICIONAL, E O PROTÓTIPO DECIDE ISSO POR ESTADO.
  //
  // A frase está no quadro 1 do `Estados.dc.html` — "coleta recém-iniciada" —,
  // e NÃO no `Main.dc.html`: ali a série cobre a janela inteira, não há hachura,
  // e um texto explicando uma faixa que não aparece não tem o que explicar.
  //
  // A primeira versão a renderizava SEMPRE, e ainda parafraseada, com um
  // "a linha quebra ali em vez de cair a zero" que é argumento meu e não do
  // autor. O texto abaixo é o do artboard, literal.
  const temTrechoNaoMedido = medidos.length < days.length;

  const rotulo =
    queryState !== 'ok'
      ? `Tasks por dia: ${queryState === 'loading' ? 'consultando.' : 'a consulta não respondeu.'}`
      : `Tasks por dia: ${medidos.length} dias medidos de ${days.length} pedidos, máximo ${
          maxMeasuredCount === null ? 'sem medição' : formatCount(maxMeasuredCount)
        }.`;

  return (
    <Card withBorder padding={0} data-testid="card-tasks-por-dia">
      <Box px="md" py="sm" bg="var(--buteco-surface-subtle)">
        <Group justify="space-between" align="center" wrap="nowrap">
          <Text size="sm" fw={600}>
            Tasks por dia
          </Text>
          {/* Máximo e mínimo da faixa MEDIDA, no canto direito do cabeçalho,
              como o `Main.dc.html` desenha. Ausentes quando não há dia medido —
              não há o que dizer, e "máximo 0 · mínimo 0" afirmaria medição.
              Antes isto vivia só no `aria-label`, invisível para quem enxerga. */}
          {queryState === 'ok' && maxMeasuredCount !== null && minMeasuredCount !== null ? (
            <Text size="xs" c="dimmed" data-testid="tasks-por-dia-extremos">
              máximo {formatCount(maxMeasuredCount)} · mínimo {formatCount(minMeasuredCount)}
            </Text>
          ) : null}
        </Group>
      </Box>
      <Stack gap="xs" p="md">
        {queryState !== 'ok' ? (
          <Text
            ff="monospace"
            size={METRIC_SIZE.card}
            c="dimmed"
            data-metric-state="unknown"
            data-testid="tasks-por-dia-travessao"
          >
            —
          </Text>
        ) : (
          <Box style={{ position: 'relative', width: '100%', height: 120 }}>
            {/* AS FAIXAS SEM MEDIÇÃO, em camada própria atrás do desenho.
                Percentuais do domínio de dias: cada faixa cobre da borda
                esquerda do primeiro dia não medido à borda direita do último. */}
            {faixas.map((faixa) => {
              const passo = 100 / days.length;
              return (
                <Box
                  key={`faixa-${faixa.inicio}`}
                  data-testid={`faixa-nao-medida-${faixa.inicio}`}
                  style={{
                    position: 'absolute',
                    top: 0,
                    bottom: 0,
                    left: `${faixa.inicio * passo}%`,
                    width: `${(faixa.fim - faixa.inicio + 1) * passo}%`,
                    background: HACHURA,
                    border: '1px dashed var(--mantine-color-default-border)',
                    borderRadius: 'var(--mantine-radius-xs)',
                    boxSizing: 'border-box',
                  }}
                />
              );
            })}
            <Box
              component="svg"
              role="img"
              aria-label={rotulo}
              viewBox={`0 0 ${VIEW_W} ${VIEW_H}`}
              preserveAspectRatio="none"
              style={{ position: 'relative', width: '100%', height: 120 }}
              data-testid="tasks-por-dia-svg"
            >
            {segmentos.map((segmento) => (
              <polyline
                key={`segmento-${segmento[0].i}`}
                data-testid={`segmento-${segmento[0].i}`}
                points={segmento.map((p) => `${x(p.i)},${y(p.count)}`).join(' ')}
                fill="none"
                stroke="var(--mantine-primary-color-filled)"
                strokeWidth="2"
              />
            ))}

            {days.map((dia, i) =>
              dia.state === 'unmeasured' ? null : (
                <circle
                  key={dia.day}
                  data-testid={`ponto-${dia.day}`}
                  data-day-state={dia.state}
                  cx={x(i)}
                  cy={y(dia.taskCount as number)}
                  r={2.5}
                  fill="var(--mantine-primary-color-filled)"
                />
                ),
              )}
            </Box>
          </Box>
        )}
        {/* O EIXO DE DATAS, como o `Main.dc.html` desenha: primeira, meio e
            última, alinhadas às extremidades e ao centro. Fora do SVG de
            propósito — dentro dele o `preserveAspectRatio="none"` esticaria a
            tipografia junto com o desenho. */}
        {queryState === 'ok' && marcas.length > 0 ? (
          <Group
            justify={marcas.length === 1 ? 'center' : 'space-between'}
            data-testid="tasks-por-dia-eixo"
          >
            {marcas.map((marca) => (
              <Text key={marca.day} size="xs" c="dimmed" data-testid={`eixo-${marca.day}`}>
                {marca.texto}
              </Text>
            ))}
          </Group>
        ) : null}

        {/* Literal do quadro 1 do `Estados.dc.html`, e SÓ quando há o trecho que
            ela explica. */}
        {queryState === 'ok' && temTrechoNaoMedido ? (
          <Text size="xs" c="dimmed" data-testid="tasks-por-dia-legenda-hachura">
            A faixa hachurada é o trecho sem medição. Preencher com zero faria a série inventar
            um período de inatividade que nunca existiu.
          </Text>
        ) : null}
      </Stack>
    </Card>
  );
}
