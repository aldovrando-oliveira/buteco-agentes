// O segredo do TOTP em grupos de 4 caracteres, como no quadro (`JBSW Y3DP …`). "Copiar" copia o
// valor sem os espaços, como veio da API.
export function groupSecret(segredo: string): string {
  return segredo.replace(/(.{4})(?=.)/g, '$1 ');
}
