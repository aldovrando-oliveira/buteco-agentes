// A JANELA DA PÁGINA DE INSIGHTS: N × 24 H TERMINANDO NO INSTANTE DA CONSULTA.
//
// Mesma razão da `lastSevenDaysWindow` de
// `features/inventory/utils/activityWindow.ts`, e a razão está lá por escrito —
// aqui o que muda é só o período ser escolhido pelo operador em vez de fixo em
// 7 dias.
//
// ROLANTE, E NÃO DE CALENDÁRIO. "Desde a meia-noite de 29 dias atrás" obrigaria
// a decidir de qual fuso é a meia-noite, e a janela mudaria de tamanho com o
// horário de verão. Aqui são N × 24 h de relógio absoluto.
//
// "AGORA" É PARÂMETRO. Nada de `new Date()` aqui dentro: quem chama é o
// `queryFn` do hook, no instante da consulta — e é isso que faz a nova tentativa
// consultar a janela atualizada, e não uma calculada antes (D4).
//
// SEM CONVERSÃO MANUAL PARA UTC: `toISOString()` sempre emite UTC com `Z`.

export const INSIGHTS_PERIODS = ['7d', '30d', '90d'] as const;

export type InsightsPeriod = (typeof INSIGHTS_PERIODS)[number];

export const DEFAULT_INSIGHTS_PERIOD: InsightsPeriod = '30d';

const DAY_MS = 24 * 60 * 60 * 1000;

const PERIOD_DAYS: Record<InsightsPeriod, number> = {
  '7d': 7,
  '30d': 30,
  '90d': 90,
};

export function periodDays(period: InsightsPeriod): number {
  return PERIOD_DAYS[period];
}

export const PERIOD_LABELS: Record<InsightsPeriod, string> = {
  '7d': '7d',
  '30d': '30d',
  '90d': '90d',
};

export function insightsWindow(
  period: InsightsPeriod,
  now: Date,
): { from: string; to: string } {
  return {
    from: new Date(now.getTime() - PERIOD_DAYS[period] * DAY_MS).toISOString(),
    to: now.toISOString(),
  };
}
