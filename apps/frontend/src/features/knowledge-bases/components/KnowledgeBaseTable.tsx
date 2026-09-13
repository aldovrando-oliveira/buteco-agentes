import { Anchor, Badge, Group, Stack, Table, Text } from '@mantine/core';
import { Link } from 'react-router';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { SectionLabel } from '../../../components/data/SectionLabel';
import { agentsConsultingBase } from '../utils/agentUsage';
import { statusPresentation } from '../utils/documentIndexing';
import {
  documentCountLabel,
  indexingParts,
  summaryById,
  summaryFor,
} from '../utils/indexingSummary';
import type { KnowledgeBase, KnowledgeBaseIndexingSummary } from '../types/knowledgeBase';
import type { Agent } from '../../agents/types/agent';

interface KnowledgeBaseTableProps {
  knowledgeBases: KnowledgeBase[];
  // Catálogo de agentes usado só para derivar a coluna "Consultada por". Vem por
  // propriedade, buscado pela página (convenção 7), e pode ser indefinido quando
  // a consulta falha — mesmo tratamento de McpServerTable.
  agents?: Agent[];
  // Resumo de indexação de GET /knowledge-bases/indexing-summary, também buscado
  // pela página. Indefinido quando a consulta não respondeu: as duas colunas
  // dizem "não sei" e a listagem continua servindo.
  indexingSummary?: KnowledgeBaseIndexingSummary[];
}

// Três estados, e o protótipo só tem dois. Ele usa "—" quando nenhum agente
// consulta a base; a tela real também pode não SABER, quando GET /agents falha.
// Reusar "—" para os dois tornaria "desconhecido" indistinguível de "nenhum", que
// é a distinção que a convenção 13 protege — e é a mesma que McpServerTable já
// faz ("—" para indisponível, "Nenhum agente" para zero conhecido).
function ConsultedByCell({
  knowledgeBase,
  agents,
}: {
  knowledgeBase: KnowledgeBase;
  agents?: Agent[];
}) {
  if (!agents) {
    return (
      <Text size="sm" c="dimmed">
        —
      </Text>
    );
  }

  const consulting = agentsConsultingBase(agents, knowledgeBase.id);

  if (consulting.length === 0) {
    return (
      <Text size="sm" c="dimmed">
        Nenhum agente
      </Text>
    );
  }

  return <Text size="sm">{consulting.map((agent) => agent.name).join(', ')}</Text>;
}

// Travessão significa DESCONHECIDO, nunca zero. `documentCountLabel` devolve
// `null` só quando não há linha de resumo para esta base — e isso acontece por
// dois caminhos: a consulta não respondeu, ou o resumo não trouxe a base (ela foi
// criada entre as duas requisições, que são independentes).
//
// Zero é caso separado e tem texto próprio, `Nenhum`, porque é contagem MEDIDA.
function DocumentCountCell({ item }: { item?: KnowledgeBaseIndexingSummary }) {
  const label = documentCountLabel(item);

  if (label === null) {
    return (
      <Text size="sm" c="dimmed">
        —
      </Text>
    );
  }

  return <Text size="sm">{label}</Text>;
}

// Três estados, e o do meio é o que alguém vai "consertar": célula VAZIA quando a
// base não tem documento. Não há o que indexar, e a coluna vizinha já diz
// `Nenhum` — o protótipo escreve "Nenhum documento" aqui também, e o percurso
// mostrou a mesma informação em três células da mesma linha (design.md, D5).
//
// O tom de alerta fica SÓ na parcela de falha. Documento em andamento é o
// funcionamento normal do pipeline; o protótipo pinta a célula inteira de aviso
// quando há falha OU qualquer não terminal, e com isso marca como problema
// exatamente o estado que se resolve sozinho (D10).
//
// A cor vem de `statusPresentation('Failed')`, e não de uma literal: é a mesma
// que a tabela de documentos usa para documento em falha, então falha tem a
// mesma aparência nas duas telas da área — por construção, não por coincidência
// de duas strings iguais.
function IndexingCell({ item }: { item?: KnowledgeBaseIndexingSummary }) {
  const parts = indexingParts(item);

  if (parts === null) {
    return (
      <Text size="sm" c="dimmed">
        —
      </Text>
    );
  }

  if (parts.length === 0) {
    return null;
  }

  const failureColor = statusPresentation('Failed').color;

  // O separador é elemento PRÓPRIO, fora da parcela. Duas razões, e as duas são
  // de contrato: assim a parcela de falha contém exatamente o seu texto — o que
  // torna o tom afirmável sem depender de onde ela caiu na ordem —, e o "·" nunca
  // herda a cor de erro quando a falha não é a primeira parcela.
  return (
    <Group gap={5} wrap="nowrap">
      {parts.map((part, index) => (
        <Group key={part.label} gap={5} wrap="nowrap">
          {index > 0 && (
            <Text size="sm" c="dimmed" aria-hidden>
              ·
            </Text>
          )}
          <Text size="sm" c={part.isFailure ? failureColor : undefined}>
            {part.label}
          </Text>
        </Group>
      ))}
    </Group>
  );
}

// Cinco colunas, as mesmas do protótipo. As duas do meio nasceram aqui: até a
// etapa 5a-1 elas estavam proibidas porque KnowledgeBaseResponse não tem contagem
// nenhuma e os documentos vivem em GET /knowledge-bases/{id}/documents — uma
// requisição POR BASE, com 100+ bases declaradas pelo handoff. A regra não mudou;
// o dado mudou: GET /knowledge-bases/indexing-summary devolve as três contagens
// de TODAS as bases numa requisição só.
//
// A gramática das células novas tem três estados, e ela está inteira em
// `utils/indexingSummary.ts`, com o motivo. O resumo entra por `Map` indexado por
// id — nunca por posição, ainda que as duas respostas usem hoje o mesmo critério
// de ordenação (design.md, D6).
//
// O que continua FORA, e é decisão: os badges `Falha` e `Nada indexado` que o
// protótipo põe na coluna `Estado`. `Falha` repete em tom de alerta o que a
// coluna `Indexação` já diz na mesma linha, e `Nada indexado` dispara em base
// ativa SEM DOCUMENTO NENHUM e em base cujo único documento está indexando — nos
// dois casos afirmando problema onde não há (D9).
export function KnowledgeBaseTable({
  knowledgeBases,
  agents,
  indexingSummary,
}: KnowledgeBaseTableProps) {
  const summary = summaryById(indexingSummary);

  return (
    <SectionedCard>
      {/* Larguras explícitas, com `layout="fixed"` — achado da conferência
          manual, rodada 1 (convenção 14).

          Com o layout automático do Mantine, as duas colunas novas foram
          espremidas contra as existentes e a distribuição saiu MUITO longe da do
          protótipo: medido a 1860px, `Base` ficou com 844px (protótipo: ~572) e
          `Consultada por` despencou para 196px (protótipo: ~334), com a lista de
          nomes de agente quebrando em várias linhas ao lado de uma coluna `Base`
          quase vazia.

          As proporções abaixo são as do protótipo
          (`2.4fr | 140px | 1.5fr | 1.4fr | 150px`) normalizadas para 100%. É a
          classe de defeito que só a comparação de DIMENSÃO pega — a suíte roda em
          jsdom e não enxerga layout, e a etapa 5b registrou ter deixado passar
          exatamente isto por comparar só estados. */}
      <Table layout="fixed">
        <Table.Thead bg="var(--buteco-surface-subtle)">
          <Table.Tr>
            <Table.Th w="37%">
              <SectionLabel>Base</SectionLabel>
            </Table.Th>
            <Table.Th w="9%">
              <SectionLabel>Documentos</SectionLabel>
            </Table.Th>
            <Table.Th w="23%">
              <SectionLabel>Indexação</SectionLabel>
            </Table.Th>
            <Table.Th w="21%">
              <SectionLabel>Consultada por</SectionLabel>
            </Table.Th>
            <Table.Th w="10%">
              <SectionLabel>Estado</SectionLabel>
            </Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {knowledgeBases.map((knowledgeBase) => {
            const item = summaryFor(summary, knowledgeBase.id);

            return (
              <Table.Tr key={knowledgeBase.id}>
                <Table.Td>
                  <Stack gap={2}>
                    {/* Link real, e não só a linha clicável: é o alvo focável por
                        teclado que a spec exige. */}
                    <Anchor component={Link} to={`/knowledge-bases/${knowledgeBase.id}`}>
                      {knowledgeBase.name}
                    </Anchor>
                    <Text size="xs" c="dimmed" lineClamp={1}>
                      {knowledgeBase.description}
                    </Text>
                  </Stack>
                </Table.Td>
                <Table.Td data-testid={`documentos-${knowledgeBase.id}`}>
                  <DocumentCountCell item={item} />
                </Table.Td>
                <Table.Td data-testid={`indexacao-${knowledgeBase.id}`}>
                  <IndexingCell item={item} />
                </Table.Td>
                <Table.Td>
                  <ConsultedByCell knowledgeBase={knowledgeBase} agents={agents} />
                </Table.Td>
                <Table.Td>
                  <Badge color={knowledgeBase.isActive ? 'green' : 'gray'}>
                    {knowledgeBase.isActive ? 'Ativa' : 'Inativa'}
                  </Badge>
                </Table.Td>
              </Table.Tr>
            );
          })}
        </Table.Tbody>
      </Table>
    </SectionedCard>
  );
}
