import { useState } from 'react';
import { Alert, Button, Card, Group, Loader, Stack, Text } from '@mantine/core';
import { useMcpServerToolsQuery } from '../api/useMcpServers';
import { agentsAllowingTool } from '../utils/agentUsage';
import type { Agent } from '../../agents/types/agent';

interface McpServerToolsCatalogProps {
  mcpServerId: string;
  agents?: Agent[];
}

// Começa ocioso: abrir o detalhe do servidor não consulta nada. Usa o
// mesmo hook e a mesma chave de cache da descoberta na aba de ferramentas
// do agente, então tools já descobertas por lá aparecem aqui sem nova
// requisição, e atualizar aqui vale para as duas telas — o dado é do
// servidor, não da tela (Decision 5 do design.md da change
// frontend-mcp-servidor-uso-e-diagnostico).
export function McpServerToolsCatalog({ mcpServerId, agents }: McpServerToolsCatalogProps) {
  const [requested, setRequested] = useState(false);
  const toolsQuery = useMcpServerToolsQuery(mcpServerId, { enabled: requested });

  const handleRefresh = () => {
    if (requested) {
      void toolsQuery.refetch();
      return;
    }
    setRequested(true);
  };

  const tools = toolsQuery.data?.success ? toolsQuery.data.tools! : undefined;
  const discoveryFailed = toolsQuery.isError || toolsQuery.data?.success === false;

  return (
    <Card withBorder data-testid="mcp-server-tools-catalog">
      <Stack gap="sm">
        <Group justify="space-between" wrap="nowrap">
          <Text size="xs" fw={600} tt="uppercase" c="dimmed">
            Catálogo de tools
          </Text>
          <Button
            size="xs"
            variant="default"
            onClick={handleRefresh}
            loading={toolsQuery.isFetching}
          >
            Atualizar
          </Button>
        </Group>

        {!requested && (
          <Text size="sm" c="dimmed">
            As tools são descobertas ao vivo. Clique em Atualizar para consultar o servidor.
          </Text>
        )}

        {requested && toolsQuery.isLoading && (
          <Group gap="xs">
            <Loader size="xs" />
            <Text size="sm">Buscando tools...</Text>
          </Group>
        )}

        {requested && discoveryFailed && (
          <Alert color="red">
            <Stack gap="xs" align="flex-start">
              <Text size="sm">
                {toolsQuery.data?.message ?? 'Não foi possível buscar as tools deste servidor.'}
              </Text>
              <Button size="xs" variant="outline" onClick={() => void toolsQuery.refetch()}>
                Tentar novamente
              </Button>
            </Stack>
          </Alert>
        )}

        {tools?.length === 0 && (
          <Text size="sm" c="dimmed">
            Este servidor não oferece nenhuma tool.
          </Text>
        )}

        {tools && tools.length > 0 && (
          <Stack gap="xs">
            {tools.map((tool) => {
              const allowedIn = agents ? agentsAllowingTool(agents, mcpServerId, tool.name) : null;

              return (
                <Group
                  key={tool.name}
                  justify="space-between"
                  align="flex-start"
                  wrap="nowrap"
                  data-testid={`tool-row-${tool.name}`}
                >
                  <Stack gap={0} style={{ minWidth: 0 }}>
                    <Text size="sm" ff="monospace">
                      {tool.name}
                    </Text>
                    {tool.description && (
                      <Text size="xs" c="dimmed">
                        {tool.description}
                      </Text>
                    )}
                  </Stack>
                  {allowedIn !== null && (
                    <Text size="xs" c={allowedIn > 0 ? 'blue' : 'dimmed'} ta="right">
                      {allowedIn > 0
                        ? `permitida em ${allowedIn} ${allowedIn === 1 ? 'agente' : 'agentes'}`
                        : 'não permitida em nenhum agente'}
                    </Text>
                  )}
                </Group>
              );
            })}
          </Stack>
        )}
      </Stack>
    </Card>
  );
}
