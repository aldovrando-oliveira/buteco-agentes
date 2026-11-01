// PKCE e state do login pelo emissor (design.md da change
// painel-entra-pelo-emissor, D6). Tudo gerado e conferido no próprio SPA:
// 32 bytes aleatórios viram 43 caracteres em base64url, dentro dos 43–128 da
// RFC 7636 (4.1), e o challenge é o base64url do SHA-256 do verifier (S256).

function base64url(bytes: Uint8Array): string {
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function randomBase64url(byteLength: number): string {
  const bytes = new Uint8Array(byteLength);
  crypto.getRandomValues(bytes);
  return base64url(bytes);
}

export function createCodeVerifier(): string {
  return randomBase64url(32);
}

export function createState(): string {
  return randomBase64url(32);
}

export async function codeChallengeS256(verifier: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier));
  return base64url(new Uint8Array(digest));
}
