// A validade do link no rodapé dos quadros `AceitarConviteSemProvedor` e `RedefinirSenha`.

/** `dd/mm/aaaa às hh:mm`, no fuso do navegador (o parâmetro existe para o teste fixar o fuso). */
export function formatLinkExpiry(iso: string, timeZone?: string): string {
  const parts = new Intl.DateTimeFormat('pt-BR', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
    timeZone,
  }).formatToParts(new Date(iso));
  const get = (type: Intl.DateTimeFormatPartTypes) => parts.find((p) => p.type === type)?.value ?? '';
  return `${get('day')}/${get('month')}/${get('year')} às ${get('hour')}:${get('minute')}`;
}
