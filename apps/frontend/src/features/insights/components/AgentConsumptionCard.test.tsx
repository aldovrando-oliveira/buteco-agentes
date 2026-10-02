import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { AgentConsumptionCard } from './AgentConsumptionCard';
import type { AgentFailures, AgentTokens } from '../types/systemInsights';
import type { QueryState } from '../utils/metricState';
import { DEFAULT_INSIGHTS_PERIOD, type InsightsPeriod } from '../utils/insightsWindow';

const ATENDENTE = '55555555-5555-5555-5555-555555555555';
const FANTASMA = '99999999-9999-9999-9999-999999999999';

const byAgent: AgentTokens[] = [
  { agentId: ATENDENTE, inputTokens: 5_000_000, outputTokens: 1_200_000 },
  { agentId: FANTASMA, inputTokens: 900_000, outputTokens: 100_000 },
];

const falhas: AgentFailures[] = [
  { agentId: ATENDENTE, provider: 'anthropic', model: 'claude-opus-5', failedCount: 5 },
];

// ===========================================================================
// ARRANJO DAS DUAS POPULAÇÕES QUE A TABELA CRUZA (#84 e #86)
// ===========================================================================
//
// TODA CONTAGEM FICA ABAIXO DE 1.000, de propósito: `formatCount` é
// `Intl.NumberFormat('pt-BR')` e o separador de milhar é o PONTO. Um `1.234` no
// texto renderizado quebraria a asserção por formatação, e não por defeito — e o
// guarda passaria a falar de outra coisa.

/**
 * O agente da **#84**: falhou e NÃO chamou o provedor, então não tem linha em
 * `tokens.byAgent`. No banco de dev é o `4ab9739f`, com provedor e modelo nulos —
 * o defeito esconde preferencialmente a falha de CONFIGURAÇÃO.
 */
const SEM_CONSUMO = '77777777-7777-7777-7777-777777777777';

/**
 * O caso da **#86**: o MESMO agente em duas linhas, porque a rota agrupa por
 * `(AgentId, Provider, Model)` — três colunas, não uma.
 *
 * **A ordem é a da rota, e ela importa:** `order by 4 desc, 1` põe a de MAIOR
 * contagem primeiro, então a de MENOR é a ÚLTIMA — e `new Map` com chave repetida
 * guarda a última. É por isso que o defeito é viés para baixo, sempre.
 *
 * As duas contagens são as medidas na `Triagem` (5 + 2 = 7), que o recorte de
 * regime remove do banco de dev. Daí o caso ser de fixture: ele não é
 * reproduzível contra o dado atual, e o defeito é.
 */
const falhasEmDuasLinhas: AgentFailures[] = [
  { agentId: ATENDENTE, provider: 'gemini', model: 'gemini-3.6-flash', failedCount: 5 },
  { agentId: ATENDENTE, provider: 'openai', model: 'llama3.2:3b', failedCount: 2 },
];

/** A **#84**: a falha existe e o agente não está na agregação de consumo. */
const falhasSemConsumo: AgentFailures[] = [
  { agentId: SEM_CONSUMO, provider: null, model: null, failedCount: 2 },
];

/**
 * O PERÍODO PADRÃO DESTE ARRANJO É `90d`, E NÃO O PADRÃO DO SISTEMA — de propósito.
 *
 * `DEFAULT_INSIGHTS_PERIOD` é `30d`. Se o arranjo usasse ele, um `30d` fixado à mão
 * na produção passaria por TODAS as asserções de `href` deste arquivo, e o defeito
 * que a #85 fecha — o link levando a janela errada — ficaria invisível aqui.
 *
 * Com `90d`, qualquer período que não venha da prop reprova.
 */
const PERIODO_DO_ARRANJO: InsightsPeriod = '90d';

function renderCard(
  overrides: Partial<Parameters<typeof AgentConsumptionCard>[0]> = {},
  queryState: QueryState = 'ok',
) {
  const onRetryCatalog = vi.fn();
  render(
    <MantineProvider theme={theme}>
      <MemoryRouter>
        <AgentConsumptionCard
          byAgent={byAgent}
          failuresByAgent={falhas}
          agentNames={new Map([[ATENDENTE, 'Atendente']])}
          catalogLoading={false}
          catalogFailed={false}
          onRetryCatalog={onRetryCatalog}
          period={PERIODO_DO_ARRANJO}
          queryState={queryState}
          reason="A consulta não respondeu."
          {...overrides}
        />
      </MemoryRouter>
    </MantineProvider>,
  );
  return onRetryCatalog;
}

describe('AgentConsumptionCard', () => {
  it('cada agente aparece uma vez, com nome e números', () => {
    renderCard();

    expect(screen.getByTestId(`agente-${ATENDENTE}-nome`)).toHaveTextContent('Atendente');
    expect(screen.getByTestId(`agente-${ATENDENTE}-tokens`)).toHaveTextContent('6,2 M');
    expect(screen.getByTestId(`agente-${ATENDENTE}-falhas`)).toHaveTextContent('5');
  });

  it('agente FORA do catálogo mantém a linha, com o identificador no lugar do nome', () => {
    // O consumo dele é real; o que falta é o rótulo. Sumir com a linha faria o
    // total da tabela não fechar com o KPI, sem sintoma nenhum.
    renderCard();

    const nome = screen.getByTestId(`agente-${FANTASMA}-nome`);
    expect(nome).toHaveTextContent('99999999');
    expect(screen.getByTestId(`agente-${FANTASMA}-tokens`)).toHaveTextContent('1 M');
  });

  it('o nome leva DIRETO à aba de Insights do agente, não à visão geral — e com o período', () => {
    // A #67 fechou pelo caminho (2): tasks, tokens por task e duração p95 por
    // agente ficam SÓ na aba. O argumento que sustenta a decisão é que elas
    // estão a UM clique — então o link tem de abrir a aba, e não o detalhe.
    //
    // Sem o parâmetro, `parseTab(null)` cai em "Visão geral" por contrato
    // declarado (AgentDetailPage), e o clique vira dois.
    //
    // E DESDE A #85 O LINK CARREGA O PERÍODO. O clique já era um; a janela, não —
    // a aba abria no padrão, e comparar o número dela com o do ranking passava a
    // ser errado sem que nada avisasse. Era a limitação declarada na D7 da
    // `fechamento-da-l4`.
    renderCard();

    expect(screen.getByTestId(`agente-${ATENDENTE}-nome`)).toHaveAttribute(
      'href',
      `/agents/${ATENDENTE}?tab=insights&period=${PERIODO_DO_ARRANJO}`,
    );
  });

  it('NEGATIVO: o destino NÃO é o detalhe sem aba, e a aba é a de Insights', () => {
    // O par do caso acima, e ele pega dois defeitos diferentes: o parâmetro
    // sumir (volta a ser dois cliques) e o parâmetro passar a valer OUTRA aba
    // reconhecida por `parseTab` — "ferramentas" abriria uma tela válida, com
    // o link parecendo certo.
    renderCard();

    const href = screen.getByTestId(`agente-${ATENDENTE}-nome`).getAttribute('href') ?? '';

    expect(href).not.toBe(`/agents/${ATENDENTE}`);
    expect(new URL(href, 'http://localhost').searchParams.get('tab')).toBe('insights');
  });

  it('NEGATIVO: o link NÃO carrega o período padrão quando o período em vigor é outro', () => {
    // O modo de falha mais silencioso desta linha, e o único que a asserção
    // positiva acima não pega: `30d` FIXADO à mão na produção faria o link
    // parecer certo — ele tem `tab=insights`, tem `period=`, e aponta para o
    // agente certo — e desfaria a travessia exatamente quando o operador
    // escolheu outra janela.
    //
    // `7d` escolhido no arranjo, e `DEFAULT_INSIGHTS_PERIOD` LIDO em vez de
    // escrito `'30d'` à mão: fixar o número aqui faria este guarda mentir no dia
    // em que o padrão do sistema mudar.
    renderCard({ period: '7d' });

    const href = screen.getByTestId(`agente-${ATENDENTE}-nome`).getAttribute('href') ?? '';
    const period = new URL(href, 'http://localhost').searchParams.get('period');

    expect(period).toBe('7d');
    expect(period).not.toBe(DEFAULT_INSIGHTS_PERIOD);
  });

  it('agente sem falhas registradas tem ZERO MEDIDO, e a razão está no código', () => {
    // `errors.byAgent` é um group by sobre as execuções que falharam: o agente
    // está na agregação de tokens (logo foi medido) e não está nas falhas, o
    // que significa que foram contadas e deram zero.
    renderCard();

    const falhasFantasma = screen.getByTestId(`agente-${FANTASMA}-falhas`);
    expect(falhasFantasma).toHaveAttribute('data-metric-state', 'zero');
    // POR EXTENSO, como o `Main.dc.html` escreve — não o algarismo.
    expect(falhasFantasma).toHaveTextContent('Nenhuma');
    expect(falhasFantasma.textContent).not.toContain('0');
  });

  it('o zero por extenso continua sendo o ESTADO de zero, não o de vazio', () => {
    // A palavra muda; a classificação não. Sem isto, "Nenhuma" seria
    // indistinguível de célula vazia para os guardas negativos — e é
    // justamente a distinção que a página defende.
    renderCard();

    const zero = screen.getByTestId(`agente-${FANTASMA}-falhas`);
    expect(zero).toHaveAttribute('data-metric-state', 'zero');
    expect(zero).not.toHaveAttribute('data-metric-state', 'empty');
  });

  it('NEGATIVO: tokens nulos NÃO viram "Nenhuma"', () => {
    // A palavra é reservada ao zero CONTADO. Um agente sem relato de tokens
    // tem célula vazia — "Nenhuma" ali afirmaria que se contou e deu zero.
    renderCard({
      byAgent: [{ agentId: ATENDENTE, inputTokens: null, outputTokens: null }],
    });

    const tokens = screen.getByTestId(`agente-${ATENDENTE}-tokens`);
    expect(tokens).toHaveAttribute('data-metric-state', 'empty');
    expect(tokens.textContent).not.toContain('Nenhuma');
  });

  it('NEGATIVO: tokens com todas as parcelas nulas fica vazio, e não 0', () => {
    renderCard({
      byAgent: [{ agentId: ATENDENTE, inputTokens: null, outputTokens: null }],
    });

    const tokens = screen.getByTestId(`agente-${ATENDENTE}-tokens`);
    expect(tokens).toHaveAttribute('data-metric-state', 'empty');
    expect(tokens.textContent).not.toContain('0');
  });

  it('ordena por tokens, com o desconhecido no fim', () => {
    renderCard();

    const linhas = screen.getAllByTestId(/^agente-[0-9a-f-]+$/);
    expect(linhas[0]).toHaveAttribute('data-testid', `agente-${ATENDENTE}`);
  });

  // ====================================================================
  // A POPULAÇÃO DA TABELA E A SOMA DAS FALHAS (#84 e #86)
  // ====================================================================
  //
  // Os dois defeitos moram na costura entre as duas listas: as LINHAS nasciam só
  // de `tokens.byAgent`, e a COLUNA era lida de `errors.byAgent` por um `Map`.
  // Um perdia a linha inteira; o outro perdia as linhas repetidas do agente.

  it('a contagem de falhas de um agente é a SOMA das linhas que a rota serve para ele', () => {
    // #86. A rota agrupa por (AgentId, Provider, Model), então trocar de provedor
    // dentro da janela faz o agente chegar em mais de uma linha. Somar é a única
    // leitura que não descarta medição.
    renderCard({ failuresByAgent: falhasEmDuasLinhas });

    expect(screen.getByTestId(`agente-${ATENDENTE}-falhas`)).toHaveTextContent('7');
  });

  it('NEGATIVO: NÃO é a contagem de uma das linhas — nem a última, nem a primeira', () => {
    // O defeito guardava a ÚLTIMA, que pela ordenação da rota é a de MENOR
    // contagem. Afirmar só o 7 deixaria passar uma correção que trocasse o viés
    // de lado; estes dois fecham as duas saídas erradas.
    renderCard({ failuresByAgent: falhasEmDuasLinhas });

    const texto = screen.getByTestId(`agente-${ATENDENTE}-falhas`).textContent;
    expect(texto).not.toContain('2');
    expect(texto).not.toContain('5');
  });

  it('agente que falhou SEM chamar o provedor TEM linha, e as falhas dele aparecem', () => {
    // #84. A linha não nasce mais da agregação de tokens: ela nasce da UNIÃO.
    // Ausência de linha é o sintoma mais difícil de notar — ninguém repara numa
    // linha que não existe.
    renderCard({ failuresByAgent: falhasSemConsumo });

    expect(screen.getByTestId(`agente-${SEM_CONSUMO}`)).toBeInTheDocument();
    expect(screen.getByTestId(`agente-${SEM_CONSUMO}-falhas`)).toHaveTextContent('2');
  });

  it('NEGATIVO: a célula de Tokens desse agente fica VAZIA, e não em zero', () => {
    // A PRECONDIÇÃO DO GUARDA É O ESTADO OBSERVÁVEL MAIS PRÓXIMO do que se nega,
    // e não o dado de origem: o que se afirma aqui é "não há consumo MEDIDO para
    // ele", e o observável disso é o estado da célula — `empty`, nunca `zero`.
    //
    // `0` ali afirmaria que o provedor foi chamado e reportou zero token, que é
    // outra coisa; "Nenhuma" é palavra reservada ao zero CONTADO da coluna
    // Falhas.
    renderCard({ failuresByAgent: falhasSemConsumo });

    const tokens = screen.getByTestId(`agente-${SEM_CONSUMO}-tokens`);
    expect(tokens).toHaveAttribute('data-metric-state', 'empty');
    expect(tokens).not.toHaveAttribute('data-metric-state', 'zero');
    expect(tokens.textContent).not.toContain('0');
    expect(tokens.textContent).not.toContain('Nenhuma');
  });

  it('sem consumo nenhum, mas COM falha, a tabela NÃO diz que não há nada no período', () => {
    // O par do guarda do vazio: a tabela só está vazia quando as DUAS listas
    // estão. Com falha medida e consumo nenhum, dizer "Nenhum consumo por agente
    // neste período" esconderia a falha atrás de uma frase correta sobre a outra
    // população.
    renderCard({ byAgent: [], failuresByAgent: falhasSemConsumo });

    expect(screen.queryByTestId('consumo-por-agente-vazio')).toBeNull();
    expect(screen.getByTestId(`agente-${SEM_CONSUMO}-falhas`)).toHaveTextContent('2');
  });

  it('a união não duplica quem está nas duas listas, nem perde quem está só numa', () => {
    // VACUIDADE: a fixture é povoada e a precondição está afirmada — dois agentes
    // na agregação de consumo (um deles também nas falhas) e um só nas falhas.
    // São TRÊS agentes distintos, logo três linhas; quatro significaria chave
    // duplicada, duas significaria população perdida.
    expect(byAgent).toHaveLength(2);
    expect(falhas.map((f) => f.agentId)).toContain(ATENDENTE);
    expect(byAgent.map((a) => a.agentId)).not.toContain(SEM_CONSUMO);

    renderCard({ failuresByAgent: [...falhas, ...falhasSemConsumo] });

    expect(screen.getAllByTestId(/^agente-[0-9a-f-]+$/)).toHaveLength(3);
  });

  // ------------------------------------------- O DESTAQUE DA COLUNA FALHAS

  it('a linha de MAIOR contagem de falhas sai em vermelho', () => {
    renderCard({
      failuresByAgent: [
        { agentId: ATENDENTE, provider: 'anthropic', model: 'claude-opus-5', failedCount: 5 },
        { agentId: FANTASMA, provider: null, model: null, failedCount: 9 },
      ],
    });

    expect(screen.getByTestId(`agente-${FANTASMA}-falhas`)).toHaveStyle({
      color: 'var(--mantine-color-red-filled)',
    });
  });

  it('as demais linhas NÃO saem em vermelho', () => {
    renderCard({
      failuresByAgent: [
        { agentId: ATENDENTE, provider: 'anthropic', model: 'claude-opus-5', failedCount: 5 },
        { agentId: FANTASMA, provider: null, model: null, failedCount: 9 },
      ],
    });

    expect(screen.getByTestId(`agente-${ATENDENTE}-falhas`)).not.toHaveStyle({
      color: 'var(--mantine-color-red-filled)',
    });
  });

  it('NEGATIVO: com todas as contagens em zero, NENHUMA linha é destacada', () => {
    // Zero é o maior valor quando não há falha nenhuma, e destacar "Nenhuma"
    // em vermelho afirmaria um problema onde não houve nenhum.
    renderCard({ failuresByAgent: [] });

    for (const id of [ATENDENTE, FANTASMA]) {
      expect(screen.getByTestId(`agente-${id}-falhas`)).not.toHaveStyle({
        color: 'var(--mantine-color-red-filled)',
      });
    }
  });

  it('empate destaca TODOS os empatados', () => {
    // Escolher um seria arbitrário — a tabela não tem critério de desempate,
    // e inventar um (o primeiro, o de mais tokens) afirmaria uma ordem.
    renderCard({
      failuresByAgent: [
        { agentId: ATENDENTE, provider: 'anthropic', model: 'claude-opus-5', failedCount: 4 },
        { agentId: FANTASMA, provider: null, model: null, failedCount: 4 },
      ],
    });

    for (const id of [ATENDENTE, FANTASMA]) {
      expect(screen.getByTestId(`agente-${id}-falhas`)).toHaveStyle({
        color: 'var(--mantine-color-red-filled)',
      });
    }
  });

  it('o destaque ordena por CONTAGEM, e a limitação está escrita', () => {
    // Documenta o que o destaque NÃO diz: ele ranqueia por contagem absoluta,
    // não por taxa. Aqui o Atendente tem 9 falhas e o outro tem 3 — o
    // destaque vai para o Atendente, mesmo que a taxa dele possa ser menor.
    //
    // A taxa exigiria tasks por agente, que a rota do sistema não serve — e a
    // #67 decidiu que ela NÃO vai entrar. O critério fica como está de
    // propósito: destaque por taxa com a taxa fora da tela seria critério
    // invisível.
    //
    // O cenário do "pior que não leva o destaque" é HIPÓTESE: no dado de 26/09
    // ele não ocorre.
    //
    // *(Dizia a seguir: "O que ocorre é outra coisa, e tem issue — agente sem
    // chamada de provedor não tem linha nenhuma aqui (#84)". Era verdade quando
    // foi escrito; a #84 FOI CORRIGIDA nesta change, e agora esse agente tem
    // linha. Corrigido com a causa, não apagado.)*
    //
    // GATILHO para este caso mudar: o primeiro pedido do dono por taxa de falha
    // no ranking. A POPULAÇÃO já foi feita — era o que vinha primeiro.
    renderCard({
      failuresByAgent: [
        { agentId: ATENDENTE, provider: 'anthropic', model: 'claude-opus-5', failedCount: 9 },
        { agentId: FANTASMA, provider: null, model: null, failedCount: 3 },
      ],
    });

    expect(screen.getByTestId(`agente-${ATENDENTE}-falhas`)).toHaveStyle({
      color: 'var(--mantine-color-red-filled)',
    });
  });

  it('o destaque segue a POPULAÇÃO: a linha que entrou por falha pode ser a maior', () => {
    // A correção da população sem esta é defeito NOVO, que o HEAD não tem: a
    // linha que a #84 traz pode ter a maior contagem da coluna e não sair
    // destacada, porque `maiorFalha` era calculado sobre a lista de TOKENS.
    //
    // No dado de dev isto EMPATA (2 contra 2), e o empate destaca todos — então o
    // banco não reprovaria. Daí o guarda ser de fixture, com contagens diferentes.
    renderCard({
      failuresByAgent: [...falhas, { agentId: SEM_CONSUMO, provider: null, model: null, failedCount: 9 }],
    });

    expect(screen.getByTestId(`agente-${SEM_CONSUMO}-falhas`)).toHaveStyle({
      color: 'var(--mantine-color-red-filled)',
    });
  });

  it('NEGATIVO: e a linha de consumo com contagem MENOR não sai em vermelho', () => {
    // Sem este, o guarda acima passaria com a coluna inteira em vermelho — e é
    // exatamente o que a correção PARCIAL produz: `maiorFalha` sobre a lista de
    // tokens daria 5, destacando o Atendente e deixando o 9 em branco.
    renderCard({
      failuresByAgent: [...falhas, { agentId: SEM_CONSUMO, provider: null, model: null, failedCount: 9 }],
    });

    expect(screen.getByTestId(`agente-${ATENDENTE}-falhas`)).not.toHaveStyle({
      color: 'var(--mantine-color-red-filled)',
    });
  });

  // ------------- AS TRÊS COLUNAS FICAM FORA POR DECISÃO (#67, fechada)
  //
  // Não é lacuna: as três têm fonte, e vivem na aba de Insights do agente. Os
  // guardas abaixo afirmam que a tela não as inventa E que não ganha elemento
  // nenhum para falar da ausência delas.

  it('NEGATIVO: as três colunas saem, e NADA entra no lugar delas', () => {
    renderCard();

    const card = screen.getByTestId('card-consumo-por-agente');
    expect(screen.queryByTestId('agentes-lacuna-colunas')).toBeNull();
    expect(card.querySelector('[data-declared-gap]')).toBeNull();
    // Só as três colunas que TÊM fonte.
    expect(screen.getByTestId('tabela-agentes').querySelectorAll('thead th')).toHaveLength(3);
  });

  it('NEGATIVO: NENHUMA das três colunas aparece preenchida', () => {
    renderCard();

    // Nem cabeçalho, nem valor. O protótipo as desenha; a rota não as serve; e
    // preenchê-las com o número do escopo do agente misturaria níveis de
    // agregação.
    const tabela = screen.getByTestId('tabela-agentes');
    for (const coluna of [/^Tasks$/, /tokens por task/i, /duração/i]) {
      expect(
        screen.queryByRole('columnheader', { name: coluna }),
      ).toBeNull();
    }
    // Só as três colunas que TÊM fonte.
    expect(tabela.querySelectorAll('thead th')).toHaveLength(3);
  });


  // ----------------------------------------- A INDEPENDÊNCIA DAS CONSULTAS

  it('catálogo em carregamento NÃO segura os números', () => {
    renderCard({ agentNames: undefined, catalogLoading: true });

    // Os tokens já estão na tela; só a coluna de nome está carregando.
    expect(screen.getByTestId(`agente-${ATENDENTE}-tokens`)).toHaveTextContent('6,2 M');
    expect(screen.getByTestId(`agente-${ATENDENTE}-carregando`)).toBeInTheDocument();
  });

  it('catálogo que falhou não derruba a tabela, e oferece nova tentativa só para ele', async () => {
    const onRetryCatalog = renderCard({ agentNames: undefined, catalogFailed: true });

    expect(screen.getByTestId(`agente-${ATENDENTE}-tokens`)).toHaveTextContent('6,2 M');
    expect(screen.getByTestId('catalogo-falhou')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Tentar de novo' }));
    expect(onRetryCatalog).toHaveBeenCalledTimes(1);
  });

  it('sem catálogo, o identificador aparece para todos, e as linhas permanecem', () => {
    renderCard({ agentNames: undefined, catalogFailed: true });

    expect(screen.getAllByTestId(/^agente-[0-9a-f-]+$/)).toHaveLength(2);
    expect(screen.getByTestId(`agente-${ATENDENTE}-nome`)).toHaveTextContent('55555555');
  });

  it('sem nenhum agente, diz o zero medido por extenso', () => {
    // AS DUAS LISTAS VAZIAS, e não só a de consumo: desde a #84 a população da
    // tabela é a UNIÃO delas, então `byAgent: []` sozinho descreve uma tabela que
    // AINDA TEM linha — a do agente que falhou sem chamar o provedor.
    //
    // A frase "Nenhum consumo por agente neste período" só é verdadeira quando
    // nenhuma das duas mediu nada. O caso complementar — consumo vazio e falha
    // medida — tem guarda próprio, e é ele que cobre a semântica que esta
    // adaptação deixa de afirmar.
    renderCard({ byAgent: [], failuresByAgent: [] });

    expect(screen.getByTestId('consumo-por-agente-vazio')).toHaveTextContent(
      'Nenhum consumo por agente neste período.',
    );
  });
});
