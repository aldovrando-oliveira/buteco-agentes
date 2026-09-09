import { useState } from 'react';
import {
  Alert,
  Button,
  Group,
  Loader,
  Paper,
  SegmentedControl,
  Stack,
  Text,
  TextInput,
  Title,
} from '@mantine/core';
import { Link } from 'react-router';
import { useKnowledgeBasesQuery } from '../api/useKnowledgeBases';
import { useAgentsQuery } from '../../agents/api/useAgents';
import { KnowledgeBaseTable } from '../components/KnowledgeBaseTable';
import { matchesSearch } from '../../../utils/searchText';
import type { KnowledgeBase } from '../types/knowledgeBase';

const ALL = 'todas';
const ACTIVE = 'ativas';
const INACTIVE = 'inativas';

type StatusFilter = typeof ALL | typeof ACTIVE | typeof INACTIVE;

// Três opções, e não as quatro do protótipo: `Com falha` filtra por estado de
// indexação agregado por base, o mesmo dado que a coluna `Indexação` não tem
// como obter. Oferecer o filtro inerte seria pior que não oferecê-lo
// (design.md, D9).
function matchesStatus(knowledgeBase: KnowledgeBase, filter: StatusFilter): boolean {
  switch (filter) {
    case ACTIVE:
      return knowledgeBase.isActive;
    case INACTIVE:
      return !knowledgeBase.isActive;
    default:
      return true;
  }
}

export function KnowledgeBaseListPage() {
  const { data, isLoading, isError } = useKnowledgeBasesQuery();
  // Só para derivar a coluna "Consultada por". Uma falha aqui não impede a
  // listagem: a coluna fica sem afirmar uso e o resto continua servindo, mesmo
  // tratamento de McpServerListPage (design.md, D21).
  const agentsQuery = useAgentsQuery();
  // Estado de componente, não parâmetro de rota, pelo mesmo motivo das outras
  // listas: um termo em digitação encheria o histórico a cada tecla.
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<StatusFilter>(ALL);

  const knowledgeBases = data ?? [];
  const visible = knowledgeBases.filter(
    (knowledgeBase) =>
      matchesSearch(search, knowledgeBase.name, knowledgeBase.description) &&
      matchesStatus(knowledgeBase, status),
  );

  const hasKnowledgeBases = knowledgeBases.length > 0;
  const showEmptyCatalog = !isLoading && !isError && !hasKnowledgeBases;
  const showNoMatches = !isLoading && !isError && hasKnowledgeBases && visible.length === 0;

  return (
    <Stack>
      <Group justify="space-between" align="flex-start">
        <Stack gap={2}>
          <Title order={2}>Bases de conhecimento</Title>
          {hasKnowledgeBases && (
            <Text size="sm" c="dimmed">
              {knowledgeBases.length} {knowledgeBases.length === 1 ? 'base' : 'bases'} · o agente
              consulta sob demanda, como ferramenta
            </Text>
          )}
        </Stack>
        <Button component={Link} to="/knowledge-bases/new">
          Nova base
        </Button>
      </Group>

      {hasKnowledgeBases && (
        <Group gap="sm" wrap="nowrap">
          <TextInput
            aria-label="Buscar por nome ou descrição"
            placeholder="Buscar por nome ou descrição"
            value={search}
            onChange={(event) => setSearch(event.currentTarget.value)}
            maw={340}
            style={{ flex: 1 }}
          />
          <SegmentedControl
            size="xs"
            withItemsBorders={false}
            style={{ border: '1px solid var(--mantine-color-default-border)', flexShrink: 0 }}
            value={status}
            onChange={(value) => setStatus(value as StatusFilter)}
            data={[
              { value: ALL, label: 'Todas' },
              { value: ACTIVE, label: 'Ativas' },
              { value: INACTIVE, label: 'Inativas' },
            ]}
          />
        </Group>
      )}

      {isLoading && (
        <Group>
          <Loader size="sm" />
          <Text>Carregando bases de conhecimento...</Text>
        </Group>
      )}

      {isError && <Alert color="red">Não foi possível carregar as bases de conhecimento.</Alert>}

      {/* Catálogo vazio explica o que é uma base antes de pedir a primeira: quem
          chega aqui pela navegação pode não saber para que serve. Bloco
          tracejado, distinto da linha simples do vazio de busca — são problemas
          diferentes, com saídas diferentes. */}
      {showEmptyCatalog && (
        <Paper
          withBorder
          radius="md"
          p="xl"
          style={{ borderStyle: 'dashed' }}
          data-testid="empty-catalog"
        >
          <Stack align="center" gap="xs">
            <Text fw={600}>Nenhuma base de conhecimento cadastrada ainda.</Text>
            <Text size="sm" c="dimmed" ta="center" maw={520}>
              Uma base guarda documentos markdown que o agente consulta durante a conversa, quando
              julga que o assunto está ali.
            </Text>
            <Button component={Link} to="/knowledge-bases/new" mt="xs">
              Criar a primeira base
            </Button>
          </Stack>
        </Paper>
      )}

      {showNoMatches && <Text c="dimmed">Nenhuma base corresponde à busca.</Text>}

      {visible.length > 0 && (
        <KnowledgeBaseTable knowledgeBases={visible} agents={agentsQuery.data} />
      )}
    </Stack>
  );
}
