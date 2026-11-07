// As datas de Minha conta (design.md da change minha-conta-painel, D9; requisito "Datas de
// Minha conta"). "Hoje" e "ontem" são decididos pelo calendário do fuso, e não pelo UTC: às
// 01:00 de São Paulo, 23:30 da véspera é "ontem" mesmo sendo o mesmo dia em UTC. Os
// parâmetros existem para o teste fixar o agora e o fuso.
//
// Comum a Minha conta e a Usuários, que usa o mesmo formato relativo (decisão do dono Q3 da
// change usuarios-convites-identidade-painel) e o formato curto do vencimento do convite.

interface Parts {
  day: string;
  month: string;
  year: string;
  hour: string;
  minute: string;
}

function partsOf(date: Date, timeZone?: string): Parts {
  const parts = new Intl.DateTimeFormat('pt-BR', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
    timeZone,
  }).formatToParts(date);
  const get = (type: Intl.DateTimeFormatPartTypes) => parts.find((p) => p.type === type)?.value ?? '';
  return { day: get('day'), month: get('month'), year: get('year'), hour: get('hour'), minute: get('minute') };
}

const dayKey = (p: Parts) => `${p.year}-${p.month}-${p.day}`;

/** `dd/mm/aaaa`: "No painel desde" e o "desde" das 2 etapas. */
export function formatDate(iso: string, timeZone?: string): string {
  const p = partsOf(new Date(iso), timeZone);
  return `${p.day}/${p.month}/${p.year}`;
}

/** "hoje, HH:MM", "ontem, HH:MM" ou "dd/mm/aaaa, HH:MM". */
export function formatRelative(iso: string, now: Date = new Date(), timeZone?: string): string {
  const p = partsOf(new Date(iso), timeZone);
  const hora = `${p.hour}:${p.minute}`;
  const hoje = partsOf(now, timeZone);
  // 24 h antes do agora, no calendário do fuso (o Brasil não tem horário de verão desde 2019).
  const ontem = partsOf(new Date(now.getTime() - 24 * 60 * 60 * 1000), timeZone);
  if (dayKey(p) === dayKey(hoje)) return `hoje, ${hora}`;
  if (dayKey(p) === dayKey(ontem)) return `ontem, ${hora}`;
  return `${p.day}/${p.month}/${p.year}, ${hora}`;
}

/** `dd/mm, HH:MM`, sem o ano: o vencimento do convite na lista de Usuários ("vence em 23/10, 18:02"). */
export function formatShortDateTime(iso: string, timeZone?: string): string {
  const p = partsOf(new Date(iso), timeZone);
  return `${p.day}/${p.month}, ${p.hour}:${p.minute}`;
}
