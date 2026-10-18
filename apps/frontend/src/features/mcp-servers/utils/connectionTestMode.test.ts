import { describe, expect, it } from 'vitest';
import { connectionTestMode, type ConnectionTestModeInput } from './connectionTestMode';

function editing(overrides: Partial<ConnectionTestModeInput>): ConnectionTestModeInput {
  return {
    isEditing: true,
    hasServerId: true,
    savedAuthType: 'BearerToken',
    authType: 'BearerToken',
    credential: '',
    ...overrides,
  };
}

// Uma linha por linha da tabela da D4 do design.md
// (frontend-mcp-autenticacao-header-customizado), mais as negativas.
describe('connectionTestMode', () => {
  it('edição com o tipo mantido e credencial em branco testa a configuração salva', () => {
    expect(connectionTestMode(editing({}))).toBe('saved');
    expect(
      connectionTestMode(editing({ savedAuthType: 'CustomHeader', authType: 'CustomHeader' })),
    ).toBe('saved');
  });

  // Guarda G2, metade determinística: a regra anterior devolvia `saved` aqui e
  // testava o servidor com o tipo antigo.
  it('edição com o tipo trocado para um tipo com credencial e credencial em branco fica bloqueada', () => {
    expect(connectionTestMode(editing({ authType: 'CustomHeader' }))).toBe('blocked');
    expect(
      connectionTestMode(editing({ savedAuthType: 'CustomHeader', authType: 'BearerToken' })),
    ).toBe('blocked');
    expect(connectionTestMode(editing({ savedAuthType: 'None', authType: 'CustomHeader' }))).toBe(
      'blocked',
    );
  });

  it('credencial digitada testa a configuração digitada, com o tipo mantido ou trocado', () => {
    expect(connectionTestMode(editing({ credential: 'segredo' }))).toBe('unsaved');
    expect(connectionTestMode(editing({ authType: 'CustomHeader', credential: 'segredo' }))).toBe(
      'unsaved',
    );
  });

  it('None mantido em edição não testa a configuração salva', () => {
    expect(connectionTestMode(editing({ savedAuthType: 'None', authType: 'None' }))).toBe(
      'unsaved',
    );
  });

  it('troca para None em edição testa a configuração digitada', () => {
    expect(connectionTestMode(editing({ authType: 'None' }))).toBe('unsaved');
  });

  it('no cadastro testa sempre a configuração digitada, mesmo com credencial em branco', () => {
    expect(connectionTestMode(editing({ isEditing: false, hasServerId: false }))).toBe('unsaved');
    expect(
      connectionTestMode(
        editing({
          isEditing: false,
          hasServerId: false,
          savedAuthType: 'None',
          authType: 'CustomHeader',
        }),
      ),
    ).toBe('unsaved');
  });

  it('edição sem id do servidor não testa a configuração salva', () => {
    expect(connectionTestMode(editing({ hasServerId: false }))).toBe('unsaved');
  });

  it('credencial só com espaços conta como em branco', () => {
    expect(connectionTestMode(editing({ credential: '   ' }))).toBe('saved');
    expect(connectionTestMode(editing({ authType: 'CustomHeader', credential: '   ' }))).toBe(
      'blocked',
    );
  });
});
