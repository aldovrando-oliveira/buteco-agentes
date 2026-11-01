import { describe, expect, it } from 'vitest';
import { codeChallengeS256, createCodeVerifier, createState } from './pkce';

const BASE64URL = /^[A-Za-z0-9_-]+$/;

describe('pkce', () => {
  it('o challenge do vetor do apêndice B da RFC 7636 é o esperado', async () => {
    expect(await codeChallengeS256('dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk')).toBe(
      'E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM',
    );
  });

  it('o verifier tem 43 caracteres do alfabeto base64url, e muda a cada chamada', () => {
    const a = createCodeVerifier();
    const b = createCodeVerifier();

    expect(a).toHaveLength(43);
    expect(a).toMatch(BASE64URL);
    expect(a).not.toBe(b);
  });

  it('o state tem 43 caracteres do alfabeto base64url, e muda a cada chamada', () => {
    const a = createState();

    expect(a).toHaveLength(43);
    expect(a).toMatch(BASE64URL);
    expect(a).not.toBe(createState());
  });

  it('o challenge de um verifier gerado também é base64url sem preenchimento', async () => {
    const challenge = await codeChallengeS256(createCodeVerifier());

    expect(challenge).toHaveLength(43);
    expect(challenge).toMatch(BASE64URL);
  });
});
