import { Alert, Badge, Button, Group, Loader, Modal, Stack, Text } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { notifications } from '@mantine/notifications';
import { Link, useParams } from 'react-router';
import { DetailHeader } from '../../../components/layout/DetailHeader';
import { useAgentsQuery } from '../../agents/api/useAgents';
import {
  useActivateKnowledgeBaseMutation,
  useDeactivateKnowledgeBaseMutation,
  useKnowledgeBaseQuery,
} from '../api/useKnowledgeBases';
import { KnowledgeBaseDescriptionCard } from '../components/KnowledgeBaseDescriptionCard';
import { KnowledgeBaseDocumentsPlaceholder } from '../components/KnowledgeBaseDocumentsPlaceholder';
import { KnowledgeBaseAgentsCard } from '../components/KnowledgeBaseAgentsCard';
import { ApiError } from '../api/knowledgeBasesApi';

// Sem `Tabs`. As duas abas do protótipo — Documentos e Diagnóstico do índice —
// pertencem à 5a-2 e à 5c, e uma barra com uma aba só afirmaria uma estrutura
// que esta tela não tem. Nascem na 5c, quando existirem duas de verdade
// (design.md, D3).
export function KnowledgeBaseDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { data, isLoading, error } = useKnowledgeBaseQuery(id!);
  // Só para derivar quem consulta esta base. Uma falha aqui não impede o
  // detalhe de carregar (design.md, D2).
  const agentsQuery = useAgentsQuery();
  const activateMutation = useActivateKnowledgeBaseMutation();
  const deactivateMutation = useDeactivateKnowledgeBaseMutation();
  const [confirmOpened, { open: openConfirm, close: closeConfirm }] = useDisclosure(false);

  const handleActivate = () => {
    activateMutation.mutate(id!, {
      onSuccess: (knowledgeBase) => {
        notifications.show({
          color: 'green',
          title: 'Base ativada',
          message: `"${knowledgeBase.name}" foi ativada com sucesso.`,
        });
      },
      onError: () => {
        notifications.show({
          color: 'red',
          title: 'Erro ao ativar base',
          message: 'Não foi possível ativar a base de conhecimento. Tente novamente.',
        });
      },
    });
  };

  const handleConfirmDeactivate = () => {
    deactivateMutation.mutate(id!, {
      onSuccess: (knowledgeBase) => {
        closeConfirm();
        notifications.show({
          color: 'green',
          title: 'Base desativada',
          message: `"${knowledgeBase.name}" foi desativada com sucesso.`,
        });
      },
      onError: () => {
        closeConfirm();
        notifications.show({
          color: 'red',
          title: 'Erro ao desativar base',
          message: 'Não foi possível desativar a base de conhecimento. Tente novamente.',
        });
      },
    });
  };

  if (isLoading) {
    return (
      <Group>
        <Loader size="sm" />
        <Text>Carregando base de conhecimento...</Text>
      </Group>
    );
  }

  if (error instanceof ApiError && error.status === 404) {
    return (
      <Stack gap="sm" align="flex-start">
        <Alert color="red">Base de conhecimento não encontrada.</Alert>
        <Button component={Link} to="/knowledge-bases" variant="default">
          Voltar para as bases
        </Button>
      </Stack>
    );
  }

  if (error || !data) {
    return <Alert color="red">Não foi possível carregar a base de conhecimento.</Alert>;
  }

  return (
    <Stack gap="md">
      {/* `description` fica de fora de propósito: o subtítulo do cabeçalho a
          faria parecer decorativa. Ela tem card próprio (D7). */}
      <DetailHeader
        backTo="/knowledge-bases"
        backLabel="Bases de conhecimento"
        title={data.name}
        showDescription={false}
        status={
          <Badge color={data.isActive ? 'green' : 'gray'}>
            {data.isActive ? 'Ativa' : 'Inativa'}
          </Badge>
        }
        actions={
          <>
            <Button component={Link} to={`/knowledge-bases/${data.id}/edit`} variant="default">
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

      <KnowledgeBaseDescriptionCard description={data.description} />

      <KnowledgeBaseDocumentsPlaceholder />

      <KnowledgeBaseAgentsCard knowledgeBaseId={data.id} agents={agentsQuery.data} />


      {/* Desativar passa por confirmação, como agente e servidor MCP; ativar é
          imediato. A cópia não nomeia agentes afetados: a visão inversa é
          derivada de GET /agents e pertence à etapa do vínculo (D2). */}
      <Modal opened={confirmOpened} onClose={closeConfirm} title="Confirmar desativação">
        <Stack gap="sm">
          <Text size="sm">
            Agentes vinculados a esta base deixarão de consultá-la enquanto ela estiver inativa. Tem
            certeza que deseja desativar "{data.name}"?
          </Text>
        </Stack>

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
