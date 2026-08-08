import { useState } from 'react';
import { Button, Group, MultiSelect, Stack, Title } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useReplaceAgentDelegationsMutation } from '../api/useAgents';
import type { Agent } from '../types/agent';

interface AgentDelegationsSectionProps {
  agent: Agent;
  agentsCatalog: Agent[];
}

export function AgentDelegationsSection({ agent, agentsCatalog }: AgentDelegationsSectionProps) {
  const mutation = useReplaceAgentDelegationsMutation(agent.id);
  const originalSelection = agent.delegatesTo.map((delegate) => delegate.id);
  const [selection, setSelection] = useState<string[]>(originalSelection);

  const options = agentsCatalog
    .filter((candidate) => candidate.id !== agent.id)
    .map((candidate) => ({
      value: candidate.id,
      label: candidate.isActive ? candidate.name : `${candidate.name} (inativo)`,
    }));

  const handleSave = () => {
    mutation.mutate(selection, {
      onSuccess: () => {
        notifications.show({
          color: 'green',
          title: 'Delegações atualizadas',
          message: `As delegações de saída de "${agent.name}" foram atualizadas com sucesso.`,
        });
      },
      onError: () => {
        notifications.show({
          color: 'red',
          title: 'Erro ao atualizar delegações',
          message: 'Não foi possível atualizar as delegações do agente. Tente novamente.',
        });
      },
    });
  };

  const handleCancel = () => {
    setSelection(originalSelection);
  };

  return (
    <Stack mt="md">
      <Title order={3}>Delegações de saída</Title>
      <MultiSelect
        label="Agentes para os quais este agente delega"
        placeholder="Selecione os agentes-alvo"
        data={options}
        value={selection}
        onChange={setSelection}
        searchable
      />
      <Group>
        <Button onClick={handleSave} loading={mutation.isPending}>
          Salvar delegações
        </Button>
        <Button variant="default" onClick={handleCancel}>
          Cancelar
        </Button>
      </Group>
    </Stack>
  );
}
