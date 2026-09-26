import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { AgentModelsCard } from './AgentModelsCard';
import type { ModelTokens } from '../types/agentInsights';
import type { QueryState } from '../utils/metricState';

function renderCard(byModel: ModelTokens[], queryState: QueryState = 'ok') {
  const { container } = render(
    <MantineProvider theme={theme}>
      <AgentModelsCard
        byModel={byModel}
        queryState={queryState}
        reason="A consulta não respondeu."
      />
    </MantineProvider>,
  );
  // As linhas são buscadas por `data-model-row`, que é a marca do próprio
  // componente — regex sobre `data-testid` quebra quando o nome do modelo tem
  // hífen, que é a regra e não a exceção.
  const linhas = () => [...container.querySelectorAll('[data-model-row]')];
  return { container, linhas };
}

const dois: ModelTokens[] = [
  { provider: 'anthropic', model: 'claude-opus-5', totalTokens: 5_000_000, callCount: 142 },
  { provider: 'anthropic', model: 'claude-sonnet-5', totalTokens: 1_200_000, callCount: 96 },
];

const um: ModelTokens[] = [
  { provider: 'openai', model: 'gpt-5.6-sol', totalTokens: 1_900_000, callCount: 76 },
];

describe('AgentModelsCard — a tabela', () => {
  it('lista os modelos com chamadas e tokens', () => {
    renderCard(dois);

    expect(screen.getByTestId('modelo-do-agente-claude-opus-5-chamadas')).toHaveTextContent(
      '142',
    );
    expect(screen.getByTestId('modelo-do-agente-claude-opus-5-tokens')).toHaveTextContent(
      '5 M',
    );
  });

  it('ordena por tokens, como o artboard', () => {
    const { linhas } = renderCard([...dois].reverse());

    expect(linhas()[0]).toHaveTextContent('claude-opus-5');
  });

  it('período sem chamada nenhuma tem estado próprio, não tabela vazia', () => {
    renderCard([]);

    expect(screen.getByTestId('modelos-do-agente-vazio')).toBeInTheDocument();
    expect(screen.queryByTestId('tabela-modelos-do-agente')).not.toBeInTheDocument();
  });
});

describe('AgentModelsCard — o rótulo que muda de significado neste escopo', () => {
  it('duas linhas são MUDANÇA DE CONFIGURAÇÃO do agente, não comparação', () => {
    renderCard(dois);

    const nota = screen.getByTestId('nota-configuracao-do-agente');
    expect(nota).toHaveTextContent('a configuração deste agente mudou dentro do período');
    // A asserção negativa: copiar o rótulo da tela do sistema faria a aba
    // afirmar uma comparação entre agentes que ela não pode fazer.
    expect(nota).not.toHaveTextContent(/entre agentes|por agente|comparação/i);
  });

  it('uma linha só NÃO afirma que a configuração nunca mudou', () => {
    renderCard(um);

    const nota = screen.getByTestId('nota-configuracao-do-agente');
    expect(nota).toHaveTextContent('Um modelo só no período');
    // "no período" é a metade que importa: a nota não diz nada sobre fora da
    // janela, e não afirma imutabilidade.
    expect(nota).not.toHaveTextContent(/sempre|nunca mudou|imut/i);
  });

  it('a nota some quando a consulta não respondeu', () => {
    renderCard(dois, 'failed');

    expect(screen.queryByTestId('nota-configuracao-do-agente')).not.toBeInTheDocument();
  });
});

describe('AgentModelsCard — as duas lacunas', () => {
  it('NENHUMA coluna de cache é renderizada (L3)', () => {
    renderCard(dois);

    const card = screen.getByTestId('card-modelos-do-agente');
    expect(card).not.toHaveTextContent(/cache/i);
    // Coluna sem fonte sai SEM deixar quadro no lugar: nenhum elemento novo é
    // acrescentado ao card para anunciá-la.
    expect(screen.getAllByRole('columnheader').map((h) => h.textContent)).toEqual([
      'Modelo',
      'Provedor',
      'Chamadas',
      'Tokens',
    ]);
  });

  it('a separação turno × compactação é lacuna declarada, e diz "não devolvida"', () => {
    renderCard(dois);

    const lacuna = screen.getByTestId('modelos-do-agente-lacuna');
    expect(lacuna).toHaveAttribute('data-declared-gap', 'true');
    expect(lacuna).toHaveTextContent('não devolvida por esta rota');
    // `ProviderCall.Purpose` É GRAVADO. Dizer "não coletada" mandaria alguém
    // abrir change de coleta para um campo que já está na tabela — o mesmo
    // erro com o sinal trocado que a página do sistema corrigiu.
    expect(lacuna).not.toHaveTextContent(/não coletad/i);
  });

  it('a lacuna não é confundida com falha nem com dado desconhecido', () => {
    renderCard(dois);

    const lacuna = screen.getByTestId('modelos-do-agente-lacuna');
    expect(lacuna).not.toHaveAttribute('data-metric-state');
    expect(lacuna).not.toHaveTextContent(/falhou|erro|tentar de novo/i);
  });

  it('a lacuna é INLINE: ela substitui um subtítulo, não uma coluna', () => {
    // O peso segue o elemento substituído, não o estado. Moldura num subtítulo
    // faz o que falta pesar mais que o que existe — medido na página do
    // sistema, na terceira rodada de conferência.
    renderCard(dois);

    expect(screen.getByTestId('modelos-do-agente-lacuna')).toHaveAttribute(
      'data-gap-variant',
      'inline',
    );
  });

  it('a nota e a lacuna ficam na MESMA linha, como no artboard', () => {
    // O artboard tem uma frase única que carrega as duas coisas: por que há
    // mais de um modelo, e quantas chamadas são de compactação. A segunda
    // metade não tem fonte, então entra como lacuna — mas ao lado, não num
    // parágrafo próprio abaixo. Decisão do dono na conferência de 26/09.
    const { container } = renderCard(dois);

    const nota = screen.getByTestId('nota-configuracao-do-agente');
    const lacuna = screen.getByTestId('modelos-do-agente-lacuna');
    expect(nota.parentElement).toBe(lacuna.parentElement);
    // E o rodapé inteiro é um bloco só: dois irmãos diretos no mesmo container.
    expect(container.querySelectorAll('[data-declared-gap]')).toHaveLength(1);
  });
});

describe('AgentModelsCard — a gramática, com as asserções negativas', () => {
  it('tokens nulo vira célula VAZIA, e o callCount ao lado continua contando', () => {
    // É a demonstração da célula vazia que herdou o papel da coluna de cache:
    // o par na MESMA LINHA separa "não reportou" de "contei e deu zero".
    renderCard([{ provider: 'google', model: 'gemini-2.5-pro', totalTokens: null, callCount: 12 }]);

    const tokens = screen.getByTestId('modelo-do-agente-gemini-2.5-pro-tokens');
    expect(tokens).toHaveAttribute('data-metric-state', 'empty');
    expect(tokens).not.toHaveTextContent('0');

    const chamadas = screen.getByTestId('modelo-do-agente-gemini-2.5-pro-chamadas');
    expect(chamadas).toHaveAttribute('data-metric-state', 'value');
    expect(chamadas).toHaveTextContent('12');
  });

  it('callCount zero é zero MEDIDO, não célula vazia', () => {
    renderCard([{ provider: 'google', model: 'gemini-2.5-pro', totalTokens: 500, callCount: 0 }]);

    expect(screen.getByTestId('modelo-do-agente-gemini-2.5-pro-chamadas')).toHaveAttribute(
      'data-metric-state',
      'zero',
    );
  });

  it('o modelo sem tokens reportados vai para o FIM, não para o começo', () => {
    // Consumo DESCONHECIDO não é o menor consumo: ele não pode disputar
    // posição com quem reportou.
    const { linhas } = renderCard([
      { provider: 'google', model: 'gemini-2.5-pro', totalTokens: null, callCount: 12 },
      ...um,
    ]);

    expect(linhas()[linhas().length - 1]).toHaveTextContent('gemini-2.5-pro');
  });

  it('a consulta em curso NÃO produz 0 em nenhuma célula', () => {
    renderCard(dois, 'loading');

    for (const el of screen.getAllByTestId(/-(chamadas|tokens)$/)) {
      expect(el).toHaveAttribute('data-metric-state', 'unknown');
      expect(el).not.toHaveTextContent('0');
    }
  });
});
