import { Alert, Badge, Button, Group, Loader, Modal, Stack, Tabs, Text } from '@mantine/core';
import { DetailHeader } from '../../../components/layout/DetailHeader';
import { useDisclosure } from '@mantine/hooks';
import { notifications } from '@mantine/notifications';
import { Link, useParams, useSearchParams } from 'react-router';
import {
  useActivateAgentMutation,
  useAgentQuery,
  useAgentsQuery,
  useDeactivateAgentMutation,
} from '../api/useAgents';
import { useMcpServersQuery } from '../../mcp-servers/api/useMcpServers';
import { useKnowledgeBasesQuery } from '../../knowledge-bases/api/useKnowledgeBases';
import { AgentDelegationsTab } from '../components/AgentDelegationsTab';
import { AgentKnowledgeTab } from '../components/AgentKnowledgeTab';
import { AgentOverviewTab } from '../components/AgentOverviewTab';
import { AgentToolsTab } from '../components/AgentToolsTab';
import { ApiError } from '../api/agentsApi';

const OVERVIEW_TAB = 'visao-geral';
const TOOLS_TAB = 'ferramentas';
const KNOWLEDGE_TAB = 'conhecimento';
const DELEGATIONS_TAB = 'delegacoes';

type AgentDetailTab =
  typeof OVERVIEW_TAB | typeof TOOLS_TAB | typeof KNOWLEDGE_TAB | typeof DELEGATIONS_TAB;

// Ausência do parâmetro é a forma canônica da visão geral, e valor
// desconhecido cai nela também — sem reescrever o endereço, que só poluiria
// o histórico (Decision 1 do design.md da change
// frontend-agente-detalhe-abas).
function parseTab(value: string | null): AgentDetailTab {
  return value === TOOLS_TAB || value === KNOWLEDGE_TAB || value === DELEGATIONS_TAB
    ? value
    : OVERVIEW_TAB;
}

function TabCounter({ count }: { count: number }) {
  if (count === 0) {
    return null;
  }
  return (
    <Badge size="sm" variant="default" circle>
      {count}
    </Badge>
  );
}

export function AgentDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
  const activeTab = parseTab(searchParams.get('tab'));

  const { data, isLoading, error } = useAgentQuery(id!);
  const agentsQuery = useAgentsQuery();
  const mcpServersQuery = useMcpServersQuery({ enabled: activeTab === TOOLS_TAB });
  // Catálogo de bases buscado só com a aba de conhecimento ativa, no mesmo
  // molde acima. São 100+ bases declaradas pelo handoff, e é uma requisição que
  // as outras três abas não precisam (design.md, D7).
  const knowledgeBasesQuery = useKnowledgeBasesQuery({ enabled: activeTab === KNOWLEDGE_TAB });
  const activateMutation = useActivateAgentMutation();
  const deactivateMutation = useDeactivateAgentMutation();
  const [confirmOpened, { open: openConfirm, close: closeConfirm }] = useDisclosure(false);

  const handleTabChange = (value: string | null) => {
    const next = parseTab(value);
    setSearchParams(next === OVERVIEW_TAB ? {} : { tab: next });
  };

  const handleActivate = () => {
    activateMutation.mutate(id!, {
      onSuccess: (agent) => {
        notifications.show({
          color: 'green',
          title: 'Agente ativado',
          message: `"${agent.name}" foi ativado com sucesso.`,
        });
      },
      onError: () => {
        notifications.show({
          color: 'red',
          title: 'Erro ao ativar agente',
          message: 'Não foi possível ativar o agente. Tente novamente.',
        });
      },
    });
  };

  const handleConfirmDeactivate = () => {
    deactivateMutation.mutate(id!, {
      onSuccess: (agent) => {
        closeConfirm();
        notifications.show({
          color: 'green',
          title: 'Agente desativado',
          message: `"${agent.name}" foi desativado com sucesso.`,
        });
      },
      onError: () => {
        closeConfirm();
        notifications.show({
          color: 'red',
          title: 'Erro ao desativar agente',
          message: 'Não foi possível desativar o agente. Tente novamente.',
        });
      },
    });
  };

  if (isLoading || agentsQuery.isLoading) {
    return (
      <Group>
        <Loader size="sm" />
        <Text>Carregando agente...</Text>
      </Group>
    );
  }

  if (error instanceof ApiError && error.status === 404) {
    return <Alert color="red">Agente não encontrado.</Alert>;
  }

  if (error || !data) {
    return <Alert color="red">Não foi possível carregar o agente.</Alert>;
  }

  const needsReconfiguration = data.provider === null || data.model === null;

  return (
    <Stack gap="md" pb={80}>
      <DetailHeader
        backTo="/agents"
        backLabel="Agentes"
        title={data.name}
        status={
          <>
            {needsReconfiguration && <Badge color="yellow">Precisa de reconfiguração</Badge>}
            <Badge color={data.isActive ? 'green' : 'gray'}>
              {data.isActive ? 'Ativo' : 'Inativo'}
            </Badge>
          </>
        }
        description={data.description ?? undefined}
        actions={
          <>
            <Button component={Link} to={`/agents/${data.id}/edit`} variant="default">
              Editar
            </Button>
            {data.isActive ? (
              <Button color="red" variant="outline" onClick={openConfirm}>
                Desativar
              </Button>
            ) : (
              <Button
                color="green"
                variant="outline"
                onClick={handleActivate}
                loading={activateMutation.isPending}
              >
                Ativar
              </Button>
            )}
          </>
        }
      />

      {/* keepMounted={false} é o que sustenta o modelo de rascunho: só a aba
          ativa existe no DOM, então no máximo uma guarda de navegação está
          armada por vez (Decision 2 do design.md). */}
      <Tabs value={activeTab} onChange={handleTabChange} keepMounted={false}>
        <Tabs.List>
          <Tabs.Tab value={OVERVIEW_TAB}>Visão geral</Tabs.Tab>
          <Tabs.Tab value={TOOLS_TAB} rightSection={<TabCounter count={data.mcpServers.length} />}>
            Ferramentas
          </Tabs.Tab>
          <Tabs.Tab
            value={KNOWLEDGE_TAB}
            rightSection={<TabCounter count={data.knowledgeBases.length} />}
          >
            Conhecimento
          </Tabs.Tab>
          <Tabs.Tab
            value={DELEGATIONS_TAB}
            rightSection={<TabCounter count={data.delegatesTo.length} />}
          >
            Delegações
          </Tabs.Tab>
        </Tabs.List>

        <Tabs.Panel value={OVERVIEW_TAB} pt="md">
          <AgentOverviewTab agent={data} />
        </Tabs.Panel>

        <Tabs.Panel value={TOOLS_TAB} pt="md">
          {mcpServersQuery.isLoading ? (
            <Group>
              <Loader size="sm" />
              <Text>Carregando servidores MCP...</Text>
            </Group>
          ) : mcpServersQuery.isError ? (
            <Alert color="red">Não foi possível carregar os servidores MCP.</Alert>
          ) : (
            <AgentToolsTab agent={data} mcpServers={mcpServersQuery.data ?? []} />
          )}
        </Tabs.Panel>

        <Tabs.Panel value={KNOWLEDGE_TAB} pt="md">
          {knowledgeBasesQuery.isLoading ? (
            <Group>
              <Loader size="sm" />
              <Text>Carregando bases de conhecimento...</Text>
            </Group>
          ) : knowledgeBasesQuery.isError ? (
            // Sem o catálogo não há descrição nem estado, então a aba não
            // renderiza a lista pela metade: informar a falha é mais honesto que
            // uma tela que parece funcionar com metade do conteúdo faltando.
            <Alert color="red">Não foi possível carregar as bases de conhecimento.</Alert>
          ) : (
            <AgentKnowledgeTab agent={data} catalog={knowledgeBasesQuery.data ?? []} />
          )}
        </Tabs.Panel>

        <Tabs.Panel value={DELEGATIONS_TAB} pt="md">
          {agentsQuery.isError ? (
            <Alert color="red">
              Não foi possível carregar o catálogo de agentes para gerenciar delegações.
            </Alert>
          ) : (
            <AgentDelegationsTab agent={data} agentsCatalog={agentsQuery.data ?? []} />
          )}
        </Tabs.Panel>
      </Tabs>

      <Modal opened={confirmOpened} onClose={closeConfirm} title="Confirmar desativação">
        <Text size="sm">
          Mensagens enviadas a este agente enquanto ele estiver inativo serão rejeitadas. Tem
          certeza que deseja desativar "{data.name}"?
        </Text>
        <Group justify="flex-end" mt="md">
          <Button variant="default" onClick={closeConfirm}>
            Cancelar
          </Button>
          <Button
            color="red"
            onClick={handleConfirmDeactivate}
            loading={deactivateMutation.isPending}
          >
            Confirmar desativação
          </Button>
        </Group>
      </Modal>
    </Stack>
  );
}
