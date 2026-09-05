import { useEffect } from 'react';
import {
  Alert,
  Badge,
  Button,
  Checkbox,
  Collapse,
  Group,
  Loader,
  SimpleGrid,
  Stack,
  Text,
} from '@mantine/core';
import { useMcpServerToolsQuery } from '../../mcp-servers/api/useMcpServers';
import type { McpServer } from '../../mcp-servers/types/mcpServer';

interface AgentMcpServerRowProps {
  mcpServer: McpServer;
  selected: boolean;
  allowedTools: string[];
  expanded: boolean;
  onToggleSelected: (selected: boolean) => void;
  onToggleExpanded: () => void;
  onToggleTool: (toolName: string, checked: boolean) => void;
  onToolsDiscovered: (toolNames: string[]) => void;
}

export function AgentMcpServerRow({
  mcpServer,
  selected,
  allowedTools,
  expanded,
  onToggleSelected,
  onToggleExpanded,
  onToggleTool,
  onToolsDiscovered,
}: AgentMcpServerRowProps) {
  // A descoberta é disparada por expansão, e marcar o checkbox expande no
  // componente pai — então marcar vincula, expande e busca em um gesto só
  // (Decision 9 do design.md). Abrir a aba, por si, não dispara nada.
  const toolsQuery = useMcpServerToolsQuery(mcpServer.id, { enabled: expanded });

  useEffect(() => {
    if (toolsQuery.data?.success) {
      onToolsDiscovered(toolsQuery.data.tools!.map((tool) => tool.name));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [toolsQuery.data]);

  const discoveredTools = toolsQuery.data?.success ? toolsQuery.data.tools! : undefined;
  const discoveryFailed = toolsQuery.isError || toolsQuery.data?.success === false;
  const boundWithoutTools = selected && allowedTools.length === 0;

  const selectionSummary = !selected
    ? 'Não vinculado'
    : discoveredTools
      ? `${allowedTools.length} de ${discoveredTools.length} selecionadas`
      : `${allowedTools.length} selecionadas`;

  return (
    <Stack gap="xs" py="sm" data-testid={`agent-mcp-server-row-${mcpServer.id}`}>
      <Group justify="space-between" wrap="nowrap" align="flex-start">
        <Group gap="xs" wrap="nowrap" style={{ minWidth: 0 }}>
          <Checkbox
            label={mcpServer.name}
            checked={selected}
            onChange={(event) => onToggleSelected(event.currentTarget.checked)}
          />
          {!mcpServer.isActive && <Badge color="gray">Inativo</Badge>}
          {boundWithoutTools && <Badge color="yellow">Sem tools</Badge>}
          <Text size="xs" ff="monospace" c="dimmed" truncate>
            {mcpServer.url}
          </Text>
        </Group>
        <Group gap="sm" wrap="nowrap">
          <Text size="sm" c={boundWithoutTools ? 'yellow' : 'dimmed'}>
            {selectionSummary}
          </Text>
          <Button variant="subtle" size="xs" onClick={onToggleExpanded}>
            {expanded ? 'Ocultar tools' : 'Ver tools'}
          </Button>
        </Group>
      </Group>

      {selected && !mcpServer.isActive && (
        <Alert color="yellow" ml={40} data-testid={`inactive-bound-callout-${mcpServer.id}`}>
          Servidor inativo: as tools marcadas aqui não estão sendo oferecidas ao agente até que ele
          seja reativado.
        </Alert>
      )}

      <Collapse expanded={expanded}>
        <Stack gap="xs" pl={40} pr="md" pb="xs">
          {toolsQuery.isLoading && (
            <Group gap="xs">
              <Loader size="xs" />
              <Text size="sm">Buscando tools...</Text>
            </Group>
          )}

          {discoveryFailed && (
            <Alert color="red">
              <Stack gap="xs" align="flex-start">
                <Text size="sm">
                  {toolsQuery.data?.message ??
                    'Não foi possível buscar as tools deste servidor.'}
                </Text>
                <Button size="xs" variant="outline" onClick={() => toolsQuery.refetch()}>
                  Tentar novamente
                </Button>
              </Stack>
            </Alert>
          )}

          {discoveredTools?.length === 0 && (
            <Text size="sm" c="dimmed">
              Este servidor não oferece nenhuma tool.
            </Text>
          )}

          {discoveredTools && discoveredTools.length > 0 && (
            <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="xs">
              {discoveredTools.map((tool) => (
                <Checkbox
                  key={tool.name}
                  label={tool.name}
                  description={tool.description}
                  disabled={!selected}
                  checked={allowedTools.includes(tool.name)}
                  onChange={(event) => onToggleTool(tool.name, event.currentTarget.checked)}
                />
              ))}
            </SimpleGrid>
          )}
        </Stack>
      </Collapse>
    </Stack>
  );
}
