import { Box, Text } from '@mantine/core';

// A LACUNA DECLARADA — O QUADRO 6 DO `Estados.dc.html`.
//
// Uma linha: o nome do que falta, e o qualificador. Mais a marca
// `data-declared-gap`, que é o que a separa do travessão.
//
// POR QUE ELA EXISTE EM VEZ DE O ELEMENTO SUMIR: elemento ausente é invisível, e
// ninguém volta para procurar o que não aparece. A lacuna declarada é item
// aberto que se vê.
//
// ------------------------------------------------- O QUE ELA *NÃO* FAZ MAIS
//
// **Ela não argumenta.** Nomeia o que falta e para por aí — decisão do dono, em
// 24/09, na terceira rodada de conferência.
//
// A versão anterior carregava um `reason` explicando a causa ("A rota serve o
// cache apenas como total do sistema; reparti-lo por modelo inventaria a
// distribuição"). **O porquê pertence à ISSUE**, e a convenção 23 existe
// exatamente para isso: é a issue que sobrevive ao archive, não o `design.md`
// nem um parágrafo de tela. Quem precisa da causa vai à #51 ou à #66.
//
// A #67 SAIU desta lista: ela foi decidida (caminho 2) e as três colunas dela
// deixaram de ser lacuna — não há `DeclaredGap` nenhum falando delas em tela
// nenhuma, então o ponteiro mandaria procurar a causa de um quadro que não
// existe.
//
// **E ela não fala a língua do backend.** O qualificador foi
// `"não servido por esta rota"`, que é vocabulário de quem escreve o servidor
// numa tela lida por quem OPERA — e que ainda insinuava existir outra rota que
// serviria, o que é falso para a L2 e para a L3.
//
// ------------------------------------------------------------ O QUALIFICADOR
//
// `"não disponível"` nas quatro, decidido pelo dono com o risco na mesa: o
// termo é vizinho de "tente de novo", que é o que o TRAVESSÃO significa. O que
// contém esse risco não é a palavra, é o resto:
//
//   - a marca `data-declared-gap`, e nunca `data-metric-state`;
//   - a ausência de qualquer ação de nova tentativa ao lado;
//   - o guarda que proíbe "falhou", "erro" e "tentar de novo" no texto.
//
// **A lista tinha uma QUARTA perna e ela saiu com a #83** — era "a moldura do
// `block`, que o travessão não tem". Ela não contava: os dois sítios vivos
// sempre pediram a forma sem moldura, então **nenhuma lacuna em tela jamais teve
// essa contenção**. A lista afirmava mais cuidado do que havia, que é a forma de
// erro que a convenção 13 nomeia — a afirmação verdadeira quando foi escrita e
// que outra etapa tornou falsa, aqui pela direção mais enganosa: ela nasceu
// descrevendo uma perna que a tela não usava.
//
// Se a confusão aparecer numa conferência futura, é aqui que se mexe — e o
// risco está escrito para que a mudança não pareça capricho.
//
// --------------------------------------------------------- UMA FORMA SÓ, E POR QUÊ
//
// Este componente teve DUAS formas até a #83, e a segunda era **o padrão**.
//
// `block` desenhava um contorno interrompido em volta da lacuna, e nasceu para o
// rodapé de card, onde a lacuna substituía um grupo de COLUNAS. A décima rodada
// de conferência da #52 removeu os três rodapés — *"subtítulo vira lacuna; coluna
// sai sem deixar quadro"* —, e ela **ficou sem consumidor**, enquanto continuava
// sendo o valor que saía de graça para quem não escolhesse nada.
//
// **Era defeito latente, não preferência de estilo:** um `<DeclaredGap>` novo sem
// prop renderizava exatamente o elemento que o requisito *"Métrica aprovada no
// protótipo e sem fonte não é inventada"* proíbe para coluna. O caso proibido era
// o padrão; o permitido é que precisava ser pedido por escrito.
//
// **E ninguém viu porque o ramo morto tinha teste verde.** Um guarda que cobre
// código sem consumidor mantém o código vivo e esconde que ele está morto — a
// cobertura vira camuflagem, e é por isso que nenhuma varredura a acusaria.
//
// **A #83 removeu a forma inteira em vez de só inverter o padrão, e o motivo é o
// que decide:** inverter desarmava a armadilha e deixava viva a declaração sem
// consumidor, que nenhuma ferramenta varre. Removendo, a prop deixa de existir e
// **o compilador passa a acusar** todo sítio futuro que tente escolher — a classe
// sai da leitura manual e entra no `tsc`. Ver a #89, que registra a mesma classe
// noutro lugar, e a #94, que registra a terceira forma dela.
//
// **Se um artboard desenhar o elemento depois, reabrir pela #83**, onde a razão
// está escrita. São poucas linhas, e o custo de reescrevê-las é menor que o de
// manter disponível uma forma que a spec proíbe.
//
// A forma que sobrou é a do SUBTÍTULO: uma linha esmaecida, sem contorno, onde a
// lacuna substitui um subtítulo. É a L2, e a D8 diz exatamente isso: "o KPI
// mostra o total; o subtítulo vira a lacuna declarada".
//
// **O peso segue o elemento substituído, não o estado.** Dar contorno ao
// subtítulo fazia o que falta pesar mais que o que existe.

export interface DeclaredGapProps {
  /** O que o protótipo desenha e não chega a esta tela. */
  label: string;
  /** O que falta. Concorda em número com `label`. */
  qualifier: string;
  'data-testid'?: string;
}

export function DeclaredGap({ label, qualifier, 'data-testid': testId }: DeclaredGapProps) {
  return (
    <Box data-testid={testId} data-declared-gap="true">
      <Text size="xs" fw={400} c="dimmed">
        {label} —{' '}
        <Text span c="dimmed">
          {qualifier}
        </Text>
      </Text>
    </Box>
  );
}
