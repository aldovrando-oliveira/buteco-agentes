import { useState } from 'react';
import { Alert, Box, Button, Group, Loader, Stack, Text } from '@mantine/core';
import { SectionedCard } from '../../../components/data/SectionedCard';
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

  const mensagemAvulsa = (conteudo: React.ReactNode) => (
    <Box px="md" py="sm">
      {conteudo}
    </Box>
  );

  return (
    <SectionedCard
      data-testid="mcp-server-tools-catalog"
      title="Catálogo de tools"
      action={
        <Button
          size="compact-sm"
          variant="default"
          onClick={handleRefresh}
          loading={toolsQuery.isFetching}
        >
          Atualizar
        </Button>
      }
    >
      {!requested &&
        mensagemAvulsa(
          <Text size="sm" c="dimmed">
            As tools são descobertas ao vivo. Clique em Atualizar para consultar o servidor.
          </Text>,
        )}

      {requested &&
        toolsQuery.isLoading &&
        mensagemAvulsa(
          <Group gap="xs">
            <Loader size="xs" />
            <Text size="sm">Buscando tools...</Text>
          </Group>,
        )}

      {requested &&
        discoveryFailed &&
        mensagemAvulsa(
          <Alert color="red">
            <Stack gap="xs" align="flex-start">
              <Text size="sm">
                {toolsQuery.data?.message ?? 'Não foi possível buscar as tools deste servidor.'}
              </Text>
              <Button size="xs" variant="outline" onClick={() => void toolsQuery.refetch()}>
                Tentar novamente
              </Button>
            </Stack>
          </Alert>,
        )}

      {tools?.length === 0 &&
        mensagemAvulsa(
          <Text size="sm" c="dimmed">
            Este servidor não oferece nenhuma tool.
          </Text>,
        )}

      {tools?.map((tool) => {
        const allowedIn = agents ? agentsAllowingTool(agents, mcpServerId, tool.name) : null;

        return (
          <SectionedCard.Row key={tool.name}>
            <Group
              justify="space-between"
              align="flex-start"
              wrap="nowrap"
              gap="md"
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
                <Text
                  size="xs"
                  c={allowedIn > 0 ? 'butecoBlue' : 'dimmed'}
                  ta="right"
                  // Sem isto a descrição ao lado comprime este texto e ele
                  // quebra em duas linhas — o defeito do achado 4, anterior ao
                  // redesenho e só visível na comparação com o protótipo.
                  style={{ flexShrink: 0 }}
                >
                  {allowedIn > 0
                    ? `permitida em ${allowedIn} ${allowedIn === 1 ? 'agente' : 'agentes'}`
                    : 'não permitida em nenhum agente'}
                </Text>
              )}
            </Group>
          </SectionedCard.Row>
        );
      })}
    </SectionedCard>
  );
}
