import { useState } from 'react';
import {
  Alert,
  Button,
  Group,
  Loader,
  SegmentedControl,
  Stack,
  Text,
  TextInput,
  Title,
} from '@mantine/core';
import { Link } from 'react-router';
import { useAgentsQuery } from '../api/useAgents';
import { AgentTable } from '../components/AgentTable';
import { matchesSearch } from '../../../utils/searchText';
import type { Agent } from '../types/agent';

const ALL = 'todos';
const ACTIVE = 'ativos';
const INACTIVE = 'inativos';
const NEEDS_RECONFIGURATION = 'reconfigurar';

type StatusFilter = typeof ALL | typeof ACTIVE | typeof INACTIVE | typeof NEEDS_RECONFIGURATION;

function matchesStatus(agent: Agent, filter: StatusFilter): boolean {
  switch (filter) {
    case ACTIVE:
      return agent.isActive;
    case INACTIVE:
      return !agent.isActive;
    case NEEDS_RECONFIGURATION:
      return agent.provider === null || agent.model === null;
    default:
      return true;
  }
}

export function AgentListPage() {
  const { data, isLoading, isError } = useAgentsQuery();
  // Busca e filtro são estado de componente, não parâmetro de rota: um
  // termo em digitação encheria o histórico de navegação a cada tecla, e
  // ninguém compartilha uma lista filtrada por um prefixo (Decision 3 do
  // design.md da change frontend-listas-busca-e-colunas).
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<StatusFilter>(ALL);

  const agents = data ?? [];
  const visible = agents.filter(
    (agent) => matchesSearch(search, agent.name, agent.description) && matchesStatus(agent, status),
  );

  const hasAgents = agents.length > 0;
  const showEmptyCatalog = !isLoading && !isError && !hasAgents;
  const showNoMatches = !isLoading && !isError && hasAgents && visible.length === 0;

  return (
    <Stack>
      <Group justify="space-between" align="flex-start">
        <Stack gap={2}>
          <Title order={2}>Agentes</Title>
          {hasAgents && (
            <Text size="sm" c="dimmed">
              {agents.length} {agents.length === 1 ? 'agente cadastrado' : 'agentes cadastrados'} ·
              cada um atende mensagens dos canais vinculados a ele
            </Text>
          )}
        </Stack>
        <Button component={Link} to="/agents/new">
          Novo agente
        </Button>
      </Group>

      {hasAgents && (
        <Group gap="sm" wrap="nowrap">
          {/* Sem rótulo visível: o texto de exemplo já diz o que a busca
              alcança, e permanece como nome acessível do campo. O controle
              segmentado sobe para a mesma linha, compacto, com borda e sem os
              separadores verticais que são default do Mantine e que o protótipo
              não tem (design.md da change frontend-acabamento-telas, D5). */}
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
              { value: ALL, label: 'Todos' },
              { value: ACTIVE, label: 'Ativos' },
              { value: INACTIVE, label: 'Inativos' },
              { value: NEEDS_RECONFIGURATION, label: 'Reconfigurar' },
            ]}
          />
        </Group>
      )}

      {isLoading && (
        <Group>
          <Loader size="sm" />
          <Text>Carregando agentes...</Text>
        </Group>
      )}

      {isError && <Alert color="red">Não foi possível carregar os agentes.</Alert>}

      {showEmptyCatalog && <Text c="dimmed">Nenhum agente cadastrado ainda.</Text>}

      {/* Catálogo vazio e busca sem resultado são problemas diferentes,
          com saídas diferentes — cadastrar algo ou limpar a busca
          (Decision 6 do design.md). */}
      {showNoMatches && <Text c="dimmed">Nenhum agente corresponde à busca.</Text>}

      {visible.length > 0 && <AgentTable agents={visible} />}
    </Stack>
  );
}
