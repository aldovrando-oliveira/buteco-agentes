import { useMemo, useState } from 'react';
import { Alert, Checkbox, Group, Stack, Text, TextInput } from '@mantine/core';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { notifications } from '@mantine/notifications';
import { UnsavedChangesBar } from '../../../components/feedback/UnsavedChangesBar';
import { UnsavedChangesModal } from '../../../components/feedback/UnsavedChangesModal';
import { useUnsavedChangesGuard } from '../../../hooks/useUnsavedChangesGuard';
import { ApiError } from '../api/agentsApi';
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

// Devolve a mensagem de uma recusa PERMANENTE, ou undefined quando a falha é
// transitória e o genérico com "Tente novamente" está correto.
//
// O RAMO É PELA CHAVE DO ValidationProblem, NUNCA PELO TEXTO DA MENSAGEM, e as
// três razões estão em ordem de peso:
//
// 1. Casar texto ("fecha um ciclo") para de funcionar EM SILÊNCIO no dia em que
//    alguém melhorar a redação da API, e a tela volta ao genérico sem nenhum
//    sinal. Esta linha de trabalho já pagou esse erro duas vezes do lado do
//    teste — `delegacao-ciclo-no-cadastro` registrou que restaurar a sonda
//    `Probe_CycleAB_A` como estava faria o guarda reprovar POR TEXTO, porque as
//    mensagens tinham mudado. Em produção a consequência é pior: nada reprova.
// 2. PUT /agents/{id}/delegations tem QUATRO 400, e os quatro saem sob esta
//    mesma chave — conjunto nulo, auto-delegação, ciclo e ids inexistentes
//    (AgentDelegationEndpoints.cs:26, :42, :55, :64). Os quatro já vêm com texto
//    de operador pronto. Tratar só o ciclo deixaria três no genérico.
// 3. O quinto motivo de 400 que a rota ganhar entra sem tocar este arquivo.
//
// A premissa, escrita porque o ramo se apoia nela: ValidationProblem é recusa de
// ENTRADA por definição, então 400 sob esta chave é sempre permanente. Se a API
// algum dia devolver 400 aqui para algo transitório, esta decisão reabre.
function refusalFrom(error: unknown): string | undefined {
  if (error instanceof ApiError && error.status === 400) {
    return error.problem?.errors?.targetAgentIds?.[0];
  }
  return undefined;
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

  // A recusa permanente devolvida pelo servidor, exibida até a próxima
  // tentativa ou o descarte. Estado local e mensagem crua: ver refusalFrom e o
  // Alert lá embaixo.
  const [refusal, setRefusal] = useState<string | undefined>();

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
    setRefusal(undefined);
    mutation.mutate(selection, {
      onSuccess: (updated) => {
        setSelection(updated.delegatesTo.map((delegate) => delegate.id));
        notifications.show({
          color: 'green',
          title: 'Delegações atualizadas',
          message: `As delegações de saída de "${agent.name}" foram atualizadas com sucesso.`,
        });
      },
      onError: (error) => {
        // Mesma forma de AgentToolsTab: ramo específico grava estado
        // persistente e sai cedo; todo o resto cai no genérico abaixo, onde
        // "Tente novamente" está correto (rede, 5xx, e o 404 — que na prática
        // não chega aqui, porque apps/api não tem rota de exclusão de agente).
        const permanent = refusalFrom(error);
        if (permanent) {
          setRefusal(permanent);
          return;
        }
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

      {/* O aviso fica aqui, e NÃO na notificação, por um número medido: o
          @mantine/notifications 9.4.2 traz `autoClose: 4e3` em
          `esm/Notifications.mjs:14-16`, e `main.tsx:21` monta <Notifications />
          sem sobrescrever — toda notificação deste painel se fecha em 4
          SEGUNDOS. O caminho do ciclo é a informação que o operador precisa ler
          para agir; um canal que a mostra e a tira nesse prazo é pior que não
          mostrar, porque dá a impressão de que a informação foi dada.

          A mensagem sai INTEIRA E SEM INTERPRETAÇÃO. O caminho é montado no
          servidor com `string.Join(" → ", nomes)` sobre nomes de agente, que são
          texto livre: um agente chamado "Vendas → Suporte" produz um caminho que
          parser nenhum distingue do separador. E parsear seria reimplementar no
          cliente o conhecimento de uma regra do servidor — o mesmo motivo que
          recusa detecção de ciclo aqui, aplicado à apresentação. */}
      {refusal && (
        <Alert color="red" title="Delegações não salvas" data-testid="delegations-refusal">
          {refusal}
        </Alert>
      )}

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
          onDiscard={() => {
            setSelection(savedIds);
            setRefusal(undefined);
          }}
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
