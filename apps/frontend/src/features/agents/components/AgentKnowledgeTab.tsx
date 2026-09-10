import { useMemo, useState } from 'react';
import { Alert, Anchor, Box, Button, Group, Stack, Text } from '@mantine/core';
import { Link } from 'react-router';
import { notifications } from '@mantine/notifications';
import { useDisclosure } from '@mantine/hooks';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { UnsavedChangesBar } from '../../../components/feedback/UnsavedChangesBar';
import { UnsavedChangesModal } from '../../../components/feedback/UnsavedChangesModal';
import { useUnsavedChangesGuard } from '../../../hooks/useUnsavedChangesGuard';
import { useReplaceAgentKnowledgeBasesMutation } from '../api/useAgents';
import { knowledgeBaseRows } from '../utils/knowledgeBaseRows';
import { KnowledgeBaseLinkModal } from './KnowledgeBaseLinkModal';
import type { Agent } from '../types/agent';
import type { KnowledgeBase } from '../../knowledge-bases/types/knowledgeBase';

interface AgentKnowledgeTabProps {
  agent: Agent;
  // Catálogo completo, buscado pela página só quando esta aba está ativa
  // (convenção 7 e design.md, D7).
  catalog: KnowledgeBase[];
}

function sameSet(a: string[], b: string[]): boolean {
  if (a.length !== b.length) {
    return false;
  }
  const sortedA = [...a].sort();
  const sortedB = [...b].sort();
  return sortedA.every((value, index) => value === sortedB[index]);
}

// Aba de vínculo com rascunho, barra de salvamento e guarda de navegação — o
// mesmo idioma de AgentToolsTab e AgentDelegationsTab, e deliberadamente CONTRA
// a regra 4 do handoff de design, que pedia uma requisição por linha
// (design.md, D3). Três motivos, em ordem de peso:
//
//  1. O contrato é PUT de conjunto inteiro. Se ele falha, falhou a escrita do
//     conjunto e NADA mudou no servidor — marcar uma linha como falha afirmaria
//     algo que o contrato não distingue (convenção 13).
//  2. Ação por linha sob replace-all perde escrita concorrente: dois cliques
//     rápidos produzem dois PUT calculados a partir do mesmo estado anterior, e
//     o segundo a chegar desfaz o primeiro em silêncio. Verificado no protótipo,
//     onde os botões das outras linhas seguem habilitados durante a requisição.
//  3. As outras duas abas de vínculo deste mesmo detalhe já usam este idioma.
export function AgentKnowledgeTab({ agent, catalog }: AgentKnowledgeTabProps) {
  const mutation = useReplaceAgentKnowledgeBasesMutation(agent.id);
  const savedIds = useMemo(
    () => agent.knowledgeBases.map((knowledgeBase) => knowledgeBase.id),
    [agent.knowledgeBases],
  );
  const [selection, setSelection] = useState<string[]>(savedIds);
  const [modalOpened, { open: openModal, close: closeModal }] = useDisclosure(false);

  const isDirty = !sameSet(selection, savedIds);
  const guard = useUnsavedChangesGuard(isDirty);

  const rows = knowledgeBaseRows(selection, catalog);

  const handleToggle = (knowledgeBaseId: string) => {
    setSelection((previous) =>
      previous.includes(knowledgeBaseId)
        ? previous.filter((id) => id !== knowledgeBaseId)
        : [...previous, knowledgeBaseId],
    );
  };

  const handleSave = () => {
    // Sempre o conjunto completo resultante, nunca uma diferença.
    mutation.mutate(selection, {
      onSuccess: (updated) => {
        // Rebaseia o rascunho na resposta: sem isso, um segundo salvamento
        // reenviaria o estado de antes do primeiro.
        setSelection(updated.knowledgeBases.map((knowledgeBase) => knowledgeBase.id));
        notifications.show({
          color: 'green',
          title: 'Bases de conhecimento atualizadas',
          message: `As bases de conhecimento de "${agent.name}" foram atualizadas com sucesso.`,
        });
      },
      onError: () => {
        notifications.show({
          color: 'red',
          title: 'Erro ao atualizar bases de conhecimento',
          message:
            'Não foi possível atualizar as bases de conhecimento do agente. Nenhuma alteração foi gravada.',
        });
      },
    });
  };

  const summary =
    rows.length === 0
      ? 'Nenhuma base vinculada — este agente não consulta conhecimento'
      : rows.length === 1
        ? '1 base vinculada · consultada sob demanda, como ferramenta'
        : `${rows.length} bases vinculadas · consultadas sob demanda, como ferramenta`;

  return (
    <Stack gap="sm">
      <Group justify="space-between" wrap="wrap" gap="sm">
        <Text size="sm" c="dimmed" data-testid="knowledge-summary">
          {summary}
        </Text>
        <Button onClick={openModal}>Vincular base</Button>
      </Group>

      <SectionedCard
        title="Bases vinculadas"
        action={
          <Text size="xs" c="dimmed">
            {catalog.length === 1 ? '1 base no catálogo' : `${catalog.length} bases no catálogo`}
          </Text>
        }
      >
        {rows.length === 0 ? (
          <SectionedCard.Body>
            {/* Vazio forte de propósito: é o estado que explica um agente
                respondendo sem contexto, que é o sintoma que traz o operador
                até aqui. */}
            <Box
              ta="center"
              py="xl"
              px="md"
              style={{
                border: '1px dashed var(--mantine-color-default-border)',
                borderRadius: 'var(--mantine-radius-md)',
              }}
              data-testid="knowledge-empty"
            >
              <Text size="sm" fw={600}>
                Nenhuma base vinculada
              </Text>
              <Text size="sm" c="dimmed" mt={4} maw={440} mx="auto">
                Sem vínculo, este agente responde só com o system prompt: ele não tem onde consultar
                horários, cardápio ou políticas.
              </Text>
              <Button mt="md" onClick={openModal}>
                Vincular base
              </Button>
            </Box>
          </SectionedCard.Body>
        ) : (
          rows.map((row) => (
            <SectionedCard.Row key={row.id}>
              <Stack gap={6} data-testid={`knowledge-row-${row.id}`}>
                <Group justify="space-between" wrap="nowrap" gap="sm" align="flex-start">
                  <Stack gap={2} style={{ minWidth: 0, flex: 1 }}>
                    <Anchor component={Link} to={`/knowledge-bases/${row.id}`} size="sm" fw={600}>
                      {row.name}
                    </Anchor>
                    {/* A descrição é o que tem largura máxima, não o card: o
                        protótipo deixa o card ocupar a largura inteira e limita
                        só a linha de texto em 620px, para ela não virar uma
                        linha longa demais para ler em tela larga. */}
                    <Text size="xs" c="dimmed" maw={620}>
                      {row.description}
                    </Text>
                  </Stack>
                  {/* flex: none — sem isso o botão encolhe até truncar o
                      rótulo ("Desvinc") na linha de descrição longa. Achado na
                      conferência manual; jsdom não enxerga largura. */}
                  <Button
                    variant="default"
                    size="xs"
                    style={{ flex: 'none' }}
                    onClick={() => handleToggle(row.id)}
                  >
                    Desvincular
                  </Button>
                </Group>
                {/* Contraparte de produto do risco R6 da etapa de vínculo: o
                    backend aceita de propósito vincular base inativa, porque o
                    filtro por estado pertence à resolução em runtime. Cabe à UI
                    dizer a consequência. */}
                {!row.isActive && (
                  <Alert color="yellow" py="xs" data-testid={`knowledge-inactive-${row.id}`}>
                    Base inativa: o agente não a consulta enquanto ela estiver desativada.
                  </Alert>
                )}
              </Stack>
            </SectionedCard.Row>
          ))
        )}
      </SectionedCard>

      <KnowledgeBaseLinkModal
        opened={modalOpened}
        catalog={catalog}
        selectedIds={selection}
        onToggle={handleToggle}
        onClose={closeModal}
      />

      {(isDirty || mutation.isPending) && (
        <UnsavedChangesBar
          message="Alterações não salvas nas bases de conhecimento"
          saveLabel="Salvar bases de conhecimento"
          savingMessage="Salvando as bases de conhecimento…"
          saving={mutation.isPending}
          onDiscard={() => setSelection(savedIds)}
          onSave={handleSave}
        />
      )}

      <UnsavedChangesModal
        opened={guard.isBlocked}
        message="As alterações nas bases de conhecimento deste agente serão descartadas."
        onConfirm={guard.confirmNavigation}
        onCancel={guard.cancelNavigation}
      />
    </Stack>
  );
}
