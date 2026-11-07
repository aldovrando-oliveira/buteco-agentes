// O "Baixar arquivo .txt" dos códigos de recuperação (design.md da change minha-conta-painel,
// D5): o arquivo nasce no navegador, sem pedido ao servidor, e a URL do blob é liberada
// logo depois do clique, para os códigos não ficarem pendurados num `blob:` da página.

export const RECOVERY_CODES_FILE_NAME = 'buteco-agentes-codigos-de-recuperacao.txt';

export function recoveryCodesText(codes: string[], email: string, geradoEm: Date): string {
  const data = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric' }).format(geradoEm);
  return [
    'Buteco Agentes — códigos de recuperação',
    `Conta: ${email}`,
    `Gerados em: ${data}`,
    '',
    'Cada código vale uma vez. Guarde este arquivo fora do celular.',
    '',
    ...codes,
    '',
  ].join('\n');
}

export function downloadRecoveryCodes(codes: string[], email: string, geradoEm = new Date()) {
  const blob = new Blob([recoveryCodesText(codes, email, geradoEm)], { type: 'text/plain;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  try {
    const link = document.createElement('a');
    link.href = url;
    link.download = RECOVERY_CODES_FILE_NAME;
    link.rel = 'noopener';
    document.body.appendChild(link);
    link.click();
    link.remove();
  } finally {
    URL.revokeObjectURL(url);
  }
}
