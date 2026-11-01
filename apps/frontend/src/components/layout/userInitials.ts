// As iniciais do avatar do rodapé (spec frontend-app-shell): a primeira letra da
// primeira e da última palavra do nome, ou só a primeira com uma palavra. Sem nome,
// a primeira letra do e-mail.
export function userInitials(nome: string | null | undefined, email: string | null | undefined): string {
  const palavras = (nome ?? '').trim().split(/\s+/).filter(Boolean);
  if (palavras.length === 0) return (email ?? '').trim().charAt(0).toUpperCase();
  const primeira = palavras[0].charAt(0);
  const ultima = palavras.length > 1 ? palavras[palavras.length - 1].charAt(0) : '';
  return `${primeira}${ultima}`.toUpperCase();
}
