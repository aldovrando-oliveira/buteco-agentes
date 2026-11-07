// A política de senha do apps/api (`PasswordPolicy`, #219 D3), conferida antes de enviar:
// de 12 a 128 caracteres pelo `length`, que conta unidades UTF-16 como o `string.Length`
// do C#, sem `trim` (espaço é caractere de senha). O `400` do servidor continua sendo a
// rede de segurança.

export const SENHA_MINIMA = 12;
export const SENHA_MAXIMA = 128;

export function passwordFitsPolicy(senha: string): boolean {
  return senha.length >= SENHA_MINIMA && senha.length <= SENHA_MAXIMA;
}
