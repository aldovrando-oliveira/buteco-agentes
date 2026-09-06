import { useMemo, useState } from 'react';
import { Checkbox, Group, Stack, Text, TextInput } from '@mantine/core';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { notifications } from '@mantine/notifications';
import { UnsavedChangesBar } from '../../../components/feedback/UnsavedChangesBar';
import { UnsavedChangesModal } from '../../../components/feedback/UnsavedChangesModal';
import { useUnsavedChangesGuard } from '../../../hooks/useUnsavedChangesGuard';
import { useReplaceAgentDelegationsMutation } from '../api/useAgents';
import type { Agent } from '../types/agent';

interface AgentDelegationsTabProps {
  agent: Agent;
  agentsCatalog: Agent[];
}

function sameSet(a: string[], b: string[]): boolean {
  if (a.length !== b.length) {
    return false;
  }
  const sortedA = [...a].sort();
  const sortedB = [...b].sort();
  return sortedA.every((value, index) => value === sortedB[index]);
}

// Lista de agentes com busca, no lugar do campo de seleção múltipla que
// existia antes: o modelo de cada agente-alvo e a marca de inativo não
// cabem em um multi-select, e a lista deixa esta aba com a mesma anatomia
// da aba de ferramentas (Decision 8 do design.md da change
// frontend-agente-detalhe-abas).
export function AgentDelegationsTab({ agent, agentsCatalog }: AgentDelegationsTabProps) {
  const mutation = useReplaceAgentDelegationsMutation(agent.id);
  const savedIds = useMemo(
    () => agent.delegatesTo.map((delegate) => delegate.id),
    [agent.delegatesTo],
  );
  const [selection, setSelection] = useState<string[]>(savedIds);
  const [search, setSearch] = useState('');

  const isDirty = !sameSet(selection, savedIds);
  const guard = useUnsavedChangesGuard(isDirty);

  // O próprio agente nunca aparece: o servidor rejeita auto-delegação, e
  // oferecer uma opção sempre inválida não ajuda ninguém.
  const candidates = agentsCatalog.filter((candidate) => candidate.id !== agent.id);
  const term = search.trim().toLowerCase();
  const visible = term
    ? candidates.filter((candidate) => candidate.name.toLowerCase().includes(term))
    : candidates;

  const handleToggle = (candidateId: string, checked: boolean) => {
    setSelection((previous) =>
      checked
        ? [...previous, candidateId]
        : previous.filter((selectedId) => selectedId !== candidateId),
    );
  };

  const handleSave = () => {
    mutation.mutate(selection, {
      onSuccess: (updated) => {
        setSelection(updated.delegatesTo.map((delegate) => delegate.id));
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

  return (
    <Stack gap="sm" maw={620}>
      <Text size="sm" c="dimmed">
        {selection.length} {selection.length === 1 ? 'agente-alvo' : 'agentes-alvo'} · delegação é
        unidirecional
      </Text>

      {candidates.length === 0 ? (
        <Text size="sm" c="dimmed">
          Nenhum outro agente cadastrado para receber delegações.
        </Text>
      ) : (
        <>
          {/* Sem rótulo visível e em largura cheia, como as buscas das
              listagens: o texto de exemplo já diz o que a busca alcança e
              permanece como nome acessível. */}
          <TextInput
            aria-label="Buscar por nome"
            placeholder="Buscar por nome"
            value={search}
            onChange={(event) => setSearch(event.currentTarget.value)}
          />

          <SectionedCard>
            {visible.length === 0 ? (
              <SectionedCard.Body>
                <Text size="sm" c="dimmed">
                  Nenhum agente corresponde à busca.
                </Text>
              </SectionedCard.Body>
            ) : (
              <>
                {visible.map((candidate) => (
                  <SectionedCard.Row key={candidate.id}>
                    <Group
                      justify="space-between"
                      wrap="nowrap"
                      data-testid={`delegation-row-${candidate.id}`}
                    >
                      <Checkbox
                        label={candidate.name}
                        checked={selection.includes(candidate.id)}
                        onChange={(event) =>
                          handleToggle(candidate.id, event.currentTarget.checked)
                        }
                      />
                      <Text size="sm" ff="monospace" c={candidate.isActive ? 'dimmed' : 'yellow'}>
                        {candidate.isActive ? (candidate.model ?? 'não configurado') : '(inativo)'}
                      </Text>
                    </Group>
                  </SectionedCard.Row>
                ))}
              </>
            )}
          </SectionedCard>
        </>
      )}

      {(isDirty || mutation.isPending) && (
        <UnsavedChangesBar
          message="Alterações não salvas nas delegações"
          saveLabel="Salvar delegações"
          savingMessage="Salvando as delegações…"
          saving={mutation.isPending}
          onDiscard={() => setSelection(savedIds)}
          onSave={handleSave}
        />
      )}

      <UnsavedChangesModal
        opened={guard.isBlocked}
        message="As alterações nas delegações deste agente serão descartadas."
        onConfirm={guard.confirmNavigation}
        onCancel={guard.cancelNavigation}
      />
    </Stack>
  );
}
