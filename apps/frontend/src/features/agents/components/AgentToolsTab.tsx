import { useMemo, useState } from 'react';
import { Alert, Button, Card, Stack, Text } from '@mantine/core';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { notifications } from '@mantine/notifications';
import { Link } from 'react-router';
import { UnsavedChangesBar } from '../../../components/feedback/UnsavedChangesBar';
import { UnsavedChangesModal } from '../../../components/feedback/UnsavedChangesModal';
import { useUnsavedChangesGuard } from '../../../hooks/useUnsavedChangesGuard';
import { ApiError } from '../api/agentsApi';
import { useReplaceAgentMcpServersMutation } from '../api/useAgents';
import { AgentMcpServerRow } from './AgentMcpServerRow';
import type { Agent } from '../types/agent';
import type { McpServer } from '../../mcp-servers/types/mcpServer';

interface AgentToolsTabProps {
  agent: Agent;
  mcpServers: McpServer[];
}

type Links = Record<string, string[]>;

function linksFromAgent(agent: Agent): Links {
  return Object.fromEntries(
    agent.mcpServers.map((mcpServer) => [mcpServer.id, [...mcpServer.allowedTools]]),
  );
}

// Forma canônica do vínculo: a ordem em que servidores e tools foram
// marcados não é diferença, então o diff compara conjuntos ordenados.
function normalize(links: Links): string {
  return JSON.stringify(
    Object.keys(links)
      .sort()
      .map((mcpServerId) => [mcpServerId, [...links[mcpServerId]].sort()]),
  );
}

export function AgentToolsTab({ agent, mcpServers }: AgentToolsTabProps) {
  const mutation = useReplaceAgentMcpServersMutation(agent.id);
  const savedLinks = useMemo(() => linksFromAgent(agent), [agent]);
  const [draft, setDraft] = useState<Links>(() => linksFromAgent(agent));
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});
  const [submitError, setSubmitError] = useState<{ title: string; detail?: string } | undefined>();

  const isDirty = normalize(draft) !== normalize(savedLinks);
  const guard = useUnsavedChangesGuard(isDirty);

  const boundServerIds = Object.keys(draft);
  const allowedToolCount = boundServerIds.reduce(
    (total, mcpServerId) => total + draft[mcpServerId].length,
    0,
  );
  const boundWithoutTools = mcpServers.filter(
    (mcpServer) => mcpServer.id in draft && draft[mcpServer.id].length === 0,
  );

  const handleToggleSelected = (mcpServerId: string, selected: boolean) => {
    setDraft((previous) => {
      if (selected) {
        return { ...previous, [mcpServerId]: previous[mcpServerId] ?? [] };
      }
      return Object.fromEntries(Object.entries(previous).filter(([id]) => id !== mcpServerId));
    });
    // Marcar vincula, expande e dispara a descoberta no mesmo gesto
    // (Decision 9 do design.md).
    if (selected) {
      setExpanded((previous) => ({ ...previous, [mcpServerId]: true }));
    }
  };

  const handleToggleTool = (mcpServerId: string, toolName: string, checked: boolean) => {
    setDraft((previous) => {
      if (!(mcpServerId in previous)) {
        return previous;
      }
      const current = previous[mcpServerId];
      const next = checked ? [...current, toolName] : current.filter((tool) => tool !== toolName);
      return { ...previous, [mcpServerId]: next };
    });
  };

  // Tool salva que o servidor não oferece mais sai da seleção em silêncio:
  // não é erro do usuário nem tem ação possível.
  const handleToolsDiscovered = (mcpServerId: string, toolNames: string[]) => {
    setDraft((previous) => {
      if (!(mcpServerId in previous)) {
        return previous;
      }
      const filtered = previous[mcpServerId].filter((tool) => toolNames.includes(tool));
      if (filtered.length === previous[mcpServerId].length) {
        return previous;
      }
      return { ...previous, [mcpServerId]: filtered };
    });
  };

  const handleSave = () => {
    setSubmitError(undefined);
    const bindings = Object.entries(draft).map(([mcpServerId, allowedTools]) => ({
      mcpServerId,
      allowedTools,
    }));

    mutation.mutate(bindings, {
      onSuccess: (updated) => {
        setDraft(linksFromAgent(updated));
        notifications.show({
          color: 'green',
          title: 'Vínculo salvo',
          message: `Os servidores MCP de "${agent.name}" foram atualizados com sucesso.`,
        });
      },
      onError: (error) => {
        // 502 é falha de handshake com um servidor específico durante a
        // validação atômica: nada foi salvo, e o rascunho continua intacto
        // para o operador corrigir e tentar de novo.
        if (error instanceof ApiError && error.status === 502) {
          setSubmitError({
            title: error.problem?.title ?? error.message,
            detail: error.problem?.detail,
          });
          return;
        }
        notifications.show({
          color: 'red',
          title: 'Erro ao salvar vínculo',
          message: 'Não foi possível atualizar os servidores MCP do agente. Tente novamente.',
        });
      },
    });
  };

  if (mcpServers.length === 0) {
    return (
      <Card withBorder style={{ borderStyle: 'dashed' }}>
        <Stack align="center" gap="sm" py="md">
          <Text size="sm" c="dimmed">
            Nenhum servidor MCP cadastrado ainda.
          </Text>
          <Button component={Link} to="/mcp-servers/new">
            Cadastrar servidor MCP
          </Button>
        </Stack>
      </Card>
    );
  }

  return (
    <Stack gap="sm">
      <Text size="sm" c="dimmed">
        {boundServerIds.length}{' '}
        {boundServerIds.length === 1 ? 'servidor vinculado' : 'servidores vinculados'} ·{' '}
        {allowedToolCount} {allowedToolCount === 1 ? 'tool permitida' : 'tools permitidas'} · as
        tools são descobertas ao vivo em cada servidor
      </Text>

      {submitError && (
        <Alert color="red" title={submitError.title} data-testid="binding-submit-error">
          {submitError.detail ?? 'Nenhum vínculo foi salvo. Corrija e tente novamente.'}
        </Alert>
      )}

      {boundWithoutTools.length > 0 && (
        <Alert color="yellow" title="Vinculado sem tools" data-testid="bound-without-tools-callout">
          {boundWithoutTools.map((mcpServer) => mcpServer.name).join(', ')}{' '}
          {boundWithoutTools.length === 1 ? 'está vinculado' : 'estão vinculados'} sem nenhuma tool
          marcada. Nenhuma ferramenta desses servidores será oferecida ao modelo em runtime.
        </Alert>
      )}

      <SectionedCard>
        {mcpServers.map((mcpServer) => (
          <SectionedCard.Row key={mcpServer.id}>
            <AgentMcpServerRow
              mcpServer={mcpServer}
              selected={mcpServer.id in draft}
              allowedTools={draft[mcpServer.id] ?? []}
              expanded={expanded[mcpServer.id] ?? false}
              onToggleSelected={(selected) => handleToggleSelected(mcpServer.id, selected)}
              onToggleExpanded={() =>
                setExpanded((previous) => ({
                  ...previous,
                  [mcpServer.id]: !(previous[mcpServer.id] ?? false),
                }))
              }
              onToggleTool={(toolName, checked) =>
                handleToggleTool(mcpServer.id, toolName, checked)
              }
              onToolsDiscovered={(toolNames) => handleToolsDiscovered(mcpServer.id, toolNames)}
            />
          </SectionedCard.Row>
        ))}
      </SectionedCard>

      {(isDirty || mutation.isPending) && (
        <UnsavedChangesBar
          message="Alterações não salvas neste vínculo"
          saveLabel="Salvar vínculo"
          savingMessage="Validando as tools nos servidores MCP…"
          saving={mutation.isPending}
          onDiscard={() => {
            setSubmitError(undefined);
            setDraft(linksFromAgent(agent));
          }}
          onSave={handleSave}
        />
      )}

      <UnsavedChangesModal
        opened={guard.isBlocked}
        message="As alterações neste vínculo com servidores MCP serão descartadas."
        onConfirm={guard.confirmNavigation}
        onCancel={guard.cancelNavigation}
      />
    </Stack>
  );
}
