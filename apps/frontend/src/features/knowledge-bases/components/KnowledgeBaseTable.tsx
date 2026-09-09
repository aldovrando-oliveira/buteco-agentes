import { Anchor, Badge, Stack, Table, Text } from '@mantine/core';
import { Link } from 'react-router';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { SectionLabel } from '../../../components/data/SectionLabel';
import { agentsConsultingBase } from '../utils/agentUsage';
import type { KnowledgeBase } from '../types/knowledgeBase';
import type { Agent } from '../../agents/types/agent';

interface KnowledgeBaseTableProps {
  knowledgeBases: KnowledgeBase[];
  // Catálogo de agentes usado só para derivar a coluna "Consultada por". Vem por
  // propriedade, buscado pela página (convenção 7), e pode ser indefinido quando
  // a consulta falha — mesmo tratamento de McpServerTable.
  agents?: Agent[];
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

// Três colunas, e não as cinco do protótipo. `Documentos` e `Indexação` ficaram
// fora porque KnowledgeBaseResponse não tem contagem nenhuma e os documentos
// vivem em GET /knowledge-bases/{id}/documents — uma requisição por base, com
// 100+ bases declaradas pelo handoff (design.md, D1). Nada é preenchido com zero
// ou travessão no lugar delas: dizer "0 documentos" afirmaria que a contagem foi
// feita (convenção 13).
//
// `Consultada por` entrou (D21): é derivada de UMA requisição a GET /agents, a
// mesma que o card do detalhe usa, não uma por base.
export function KnowledgeBaseTable({ knowledgeBases, agents }: KnowledgeBaseTableProps) {
  return (
    <SectionedCard>
      <Table>
        <Table.Thead bg="var(--buteco-surface-subtle)">
          <Table.Tr>
            <Table.Th>
              <SectionLabel>Base</SectionLabel>
            </Table.Th>
            <Table.Th>
              <SectionLabel>Consultada por</SectionLabel>
            </Table.Th>
            <Table.Th>
              <SectionLabel>Estado</SectionLabel>
            </Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {knowledgeBases.map((knowledgeBase) => (
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
              <Table.Td>
                <ConsultedByCell knowledgeBase={knowledgeBase} agents={agents} />
              </Table.Td>
              <Table.Td>
                <Badge color={knowledgeBase.isActive ? 'green' : 'gray'}>
                  {knowledgeBase.isActive ? 'Ativa' : 'Inativa'}
                </Badge>
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </SectionedCard>
  );
}
