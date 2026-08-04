import { useEffect } from 'react';
import { Badge, Button, Card, Checkbox, Collapse, Group, Loader, Stack, Text } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { useMcpServerToolsQuery } from '../../mcp-servers/api/useMcpServers';
import type { McpServer } from '../../mcp-servers/types/mcpServer';

interface AgentMcpServerRowProps {
  mcpServer: McpServer;
  selected: boolean;
  allowedTools: string[];
  onToggleSelected: (selected: boolean) => void;
  onToggleTool: (toolName: string, checked: boolean) => void;
  onToolsDiscovered: (toolNames: string[]) => void;
}

export function AgentMcpServerRow({
  mcpServer,
  selected,
  allowedTools,
  onToggleSelected,
  onToggleTool,
  onToolsDiscovered,
}: AgentMcpServerRowProps) {
  const [expanded, { toggle, open }] = useDisclosure(false);
  const toolsQuery = useMcpServerToolsQuery(mcpServer.id, { enabled: expanded });

  useEffect(() => {
    if (toolsQuery.data?.success) {
      onToolsDiscovered(toolsQuery.data.tools!.map((tool) => tool.name));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [toolsQuery.data]);

  const handleToggleSelected = (checked: boolean) => {
    onToggleSelected(checked);
    if (checked) {
      open();
    }
  };

  const discoveryFailed = toolsQuery.isError || toolsQuery.data?.success === false;

  return (
    <Card withBorder data-testid={`agent-mcp-server-row-${mcpServer.id}`}>
      <Group justify="space-between">
        <Group gap="xs">
          <Checkbox
            label={mcpServer.name}
            checked={selected}
            onChange={(event) => handleToggleSelected(event.currentTarget.checked)}
          />
          {!mcpServer.isActive && <Badge color="gray">Inativo</Badge>}
        </Group>
        <Button variant="subtle" size="xs" onClick={toggle}>
          {expanded ? 'Ocultar tools' : 'Ver tools'}
        </Button>
      </Group>

      <Collapse expanded={expanded}>
        <Stack mt="sm" gap={4}>
          {toolsQuery.isLoading && (
            <Group gap="xs">
              <Loader size="xs" />
              <Text size="sm">Buscando tools...</Text>
            </Group>
          )}

          {discoveryFailed && (
            <Stack gap="xs">
              <Text size="sm" c="red">
                {toolsQuery.data?.message ?? 'Não foi possível buscar as tools deste servidor.'}
              </Text>
              <Button size="xs" variant="outline" onClick={() => toolsQuery.refetch()}>
                Tentar novamente
              </Button>
            </Stack>
          )}

          {toolsQuery.data?.success === true && toolsQuery.data.tools!.length === 0 && (
            <Text size="sm" c="dimmed">
              Este servidor não oferece nenhuma tool.
            </Text>
          )}

          {toolsQuery.data?.success === true &&
            toolsQuery.data.tools!.map((tool) => (
              <Checkbox
                key={tool.name}
                label={tool.name}
                description={tool.description}
                disabled={!selected}
                checked={allowedTools.includes(tool.name)}
                onChange={(event) => onToggleTool(tool.name, event.currentTarget.checked)}
              />
            ))}
        </Stack>
      </Collapse>
    </Card>
  );
}
