import { describe, expect, it } from 'vitest';
import { KNOWN_REJECTION_REASONS, rejectionReasonLabel } from './rejectionReasonLabels';

describe('rejectionReasonLabel', () => {
  it('cobre os QUATRO motivos de RejectionMetricsValues.Reason', () => {
    // O número é contrato com `apps/api`, e a ORDEM é a de `Reason.All` — que é
    // a lista que o guarda de lá usa para afirmar que nenhum outro valor aparece
    // na coluna. Se um motivo entrar lá e não aqui, este caso não pega; o caso do
    // desconhecido abaixo é o que garante que ele apareça na tela em vez de sumir.
    expect(KNOWN_REJECTION_REASONS).toEqual([
      'AgentNotFound',
      'AgentInactive',
      'ProviderOrModelMissing',
      'ProviderNotConfigured',
    ]);
  });

  it.each(KNOWN_REJECTION_REASONS)('%s tem rótulo de operador em português', (reason) => {
    const label = rejectionReasonLabel(reason);

    expect(label.unknown).toBe(false);
    expect(label.text).not.toBe(reason);
    // Nada de PascalCase sobrando: um rótulo que ainda carrega o valor do fio
    // passaria o caso acima e continuaria ilegível para quem opera.
    expect(label.text).not.toMatch(/[a-z][A-Z]/);
  });

  it('AgentNotFound e AgentInactive NÃO compartilham rótulo', () => {
    // A distinção é o achado da #51: o sítio de agente inativo carregava as duas
    // causas, e elas se resolvem de formas diferentes — reativar o agente, ou
    // descobrir que o cliente chama o endereço A2A de um agente que não existe.
    // Um rótulo só as recolapsaria na tela depois de `apps/api` as ter separado.
    expect(rejectionReasonLabel('AgentNotFound').text).not.toBe(
      rejectionReasonLabel('AgentInactive').text,
    );
  });

  it('o rótulo de ProviderOrModelMissing é o LITERAL do protótipo', () => {
    // `Main.dc.html`, card de Motivos, segunda linha: a única das quatro que o
    // artboard desenha, porque ele foi feito quando a recusa era um número sem
    // motivo. Onde o protótipo escreveu o rótulo, ele manda.
    expect(rejectionReasonLabel('ProviderOrModelMissing').text).toBe(
      'Agente sem provider ou modelo configurado',
    );
  });

  it('motivo DESCONHECIDO devolve o valor recebido, marcado como cru', () => {
    const label = rejectionReasonLabel('QuotaExceeded');

    expect(label).toEqual({ text: 'QuotaExceeded', unknown: true });
  });

  it('NEGATIVO: o desconhecido não pega o rótulo de nenhum dos quatro', () => {
    // A pior das duas alternativas erradas: omitir quebra a soma sem sintoma,
    // mas cair no rótulo de outro faz a tela AFIRMAR uma causa errada — e é a
    // convenção 13, que prefere não afirmar nada a afirmar o que não se sabe.
    const desconhecido = rejectionReasonLabel('QuotaExceeded');
    const conhecidos = KNOWN_REJECTION_REASONS.map((r) => rejectionReasonLabel(r).text);

    expect(conhecidos).not.toContain(desconhecido.text);
  });

  it('NEGATIVO: o desconhecido não é silenciado num texto vazio', () => {
    // Um `text: ''` passaria o caso acima e sumiria da tela do mesmo jeito que a
    // omissão — sem o sintoma que a omissão pelo menos daria no `length` da lista.
    expect(rejectionReasonLabel('QuotaExceeded').text).not.toBe('');
  });
});
