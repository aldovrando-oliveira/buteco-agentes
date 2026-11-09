import { describe, expect, it } from 'vitest';
import { papelDaResposta, papelDoClaim, papelParaCorpo, permissoes, rotuloDoPapel } from './roles';

// A tabela da spec `panel-roles` ("Um mapeamento só entre o claim role e o campo papel"),
// escrita aqui à parte do módulo: ler a expectativa do próprio `roles.ts` faria o teste circular.
const TABELA = [
  { claim: 'admin', resposta: 'admin', rotulo: 'Admin' },
  { claim: 'operator', resposta: 'operador', rotulo: 'Operador' },
  { claim: 'reader', resposta: 'leitor', rotulo: 'Leitor' },
] as const;

describe('o papel (panel-roles)', () => {
  it.each(TABELA)(
    'os pares nos dois sentidos: $claim ↔ $resposta',
    ({ claim, resposta, rotulo }) => {
      const doClaim = papelDoClaim(claim);
      const daResposta = papelDaResposta(resposta);
      expect(doClaim).not.toBeNull();
      expect(doClaim).toBe(daResposta);
      expect(rotuloDoPapel(doClaim!)).toBe(rotulo);
      expect(papelParaCorpo(doClaim!)).toBe(resposta);
    },
  );

  it('Valor desconhecido', () => {
    expect(papelDoClaim('superuser')).toBeNull();
    // O valor do claim no lugar do do fio, e o do fio no lugar do do claim.
    expect(papelDaResposta('operator')).toBeNull();
    expect(papelDaResposta('reader')).toBeNull();
    expect(papelDoClaim('operador')).toBeNull();
    expect(papelDoClaim(undefined)).toBeNull();
    expect(papelDoClaim(1)).toBeNull();
    expect(papelDaResposta(null)).toBeNull();
    // Chave herdada do protótipo de objeto não é papel.
    expect(papelDoClaim('toString')).toBeNull();
  });

  it('as permissões de cada papel', () => {
    expect(permissoes('admin')).toEqual({
      gerirAcesso: true,
      escrever: true,
      verHistorico: true,
      usarConectores: true,
    });
    expect(permissoes('operador')).toEqual({
      gerirAcesso: false,
      escrever: true,
      verHistorico: true,
      usarConectores: true,
    });
    expect(permissoes('leitor')).toEqual({
      gerirAcesso: false,
      escrever: false,
      verHistorico: false,
      usarConectores: false,
    });
  });

  it('o papel desconhecido tem as permissões do Leitor', () => {
    expect(permissoes(null)).toEqual(permissoes('leitor'));
  });
});
