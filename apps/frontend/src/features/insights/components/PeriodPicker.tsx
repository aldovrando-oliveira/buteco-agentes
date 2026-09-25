import { SegmentedControl } from '@mantine/core';
import {
  INSIGHTS_PERIODS,
  PERIOD_LABELS,
  type InsightsPeriod,
} from '../utils/insightsWindow';

// 7d / 30d / 90d, com 30d como padrão — a escolha do protótipo, que desenha o
// 30d marcado.
//
// `SegmentedControl` e não três botões: é o que o painel já usa para escolha
// única entre poucas opções, e o que lê a superfície sutil do tema sem
// declarar cor.

export interface PeriodPickerProps {
  value: InsightsPeriod;
  onChange: (period: InsightsPeriod) => void;
}

export function PeriodPicker({ value, onChange }: PeriodPickerProps) {
  return (
    <SegmentedControl
      value={value}
      onChange={(next) => onChange(next as InsightsPeriod)}
      data={INSIGHTS_PERIODS.map((period) => ({
        value: period,
        label: PERIOD_LABELS[period],
      }))}
      size="xs"
      aria-label="Período"
      data-testid="seletor-de-periodo"
    />
  );
}
