import { Alert, Badge, Button, Group, Loader, Modal, Stack, Text } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { notifications } from '@mantine/notifications';
import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { DetailHeader } from '../../../components/layout/DetailHeader';
import { useAgentsQuery } from '../../agents/api/useAgents';
import {
  useActivateKnowledgeBaseMutation,
  useDeactivateKnowledgeBaseMutation,
  useKnowledgeBaseQuery,
} from '../api/useKnowledgeBases';
import { KnowledgeBaseDescriptionCard } from '../components/KnowledgeBaseDescriptionCard';
import { KnowledgeDocumentsCard } from '../components/KnowledgeDocumentsCard';
import { KnowledgeDocumentModal } from '../components/KnowledgeDocumentModal';
import { KnowledgeBaseAgentsCard } from '../components/KnowledgeBaseAgentsCard';
import { ApiError } from '../api/knowledgeBasesApi';
import {
  useCreateKnowledgeDocumentMutation,
  useDeleteKnowledgeDocumentMutation,
  useKnowledgeDocumentQuery,
  useKnowledgeDocumentsQuery,
  useReindexKnowledgeDocumentMutation,
  useUpdateKnowledgeDocumentMutation,
} from '../api/useKnowledgeDocuments';
import { agentsConsultingBase } from '../utils/agentUsage';
import type { KnowledgeDocumentSummary } from '../types/knowledgeDocument';

// Sem `Tabs`. A segunda aba do protótipo — Diagnóstico do índice — pertence à
// 5c, e uma barra com uma aba só afirmaria uma estrutura que esta tela não tem.
// Nasce lá, quando existirem duas de verdade (design.md da 5a-1, D3).
export function KnowledgeBaseDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { data, isLoading, error } = useKnowledgeBaseQuery(id!);
  // Só para derivar quem consulta esta base. Uma falha aqui não impede o
  // detalhe de carregar (design.md, D2).
  const agentsQuery = useAgentsQuery();
  const activateMutation = useActivateKnowledgeBaseMutation();
  const deactivateMutation = useDeactivateKnowledgeBaseMutation();
  const [confirmOpened, { open: openConfirm, close: closeConfirm }] = useDisclosure(false);

  // A página busca e repassa como prop; os componentes de documento não importam
  // hook de query nem de mutation (convenção 7, design.md D11).
  const documentsQuery = useKnowledgeDocumentsQuery(id!);
  const createDocument = useCreateKnowledgeDocumentMutation(id!);
  const updateDocument = useUpdateKnowledgeDocumentMutation(id!);
  const deleteDocument = useDeleteKnowledgeDocumentMutation(id!);
  const reindexDocument = useReindexKnowledgeDocumentMutation(id!);

  const [documentModalOpened, setDocumentModalOpened] = useState(false);
  // Id do documento em edição; `null` quando o modal é de adicionar.
  const [editingDocumentId, setEditingDocumentId] = useState<string | null>(null);
  const [documentToDelete, setDocumentToDelete] = useState<KnowledgeDocumentSummary | null>(null);

  // O conteúdo só é buscado quando o modal de atualizar está aberto: a listagem
  // não traz `extractedText`, e é ele que o modo manual carrega e compara.
  const editingDocumentQuery = useKnowledgeDocumentQuery(
    id!,
    documentModalOpened ? editingDocumentId : null,
  );

  const handleAddDocument = () => {
    setEditingDocumentId(null);
    setDocumentModalOpened(true);
  };

  const handleUpdateDocument = (document: KnowledgeDocumentSummary) => {
    setEditingDocumentId(document.id);
    setDocumentModalOpened(true);
  };

  const handleReindexDocument = (document: KnowledgeDocumentSummary) => {
    reindexDocument.mutate(document.id, {
      onSuccess: () => {
        notifications.show({
          color: 'blue',
          title: 'Documento enfileirado',
          message: `"${document.title}" voltou para a fila de indexação.`,
        });
      },
      onError: () => {
        notifications.show({
          color: 'red',
          title: 'Erro ao reindexar',
          message: 'Não foi possível reindexar o documento. Tente novamente.',
        });
      },
    });
  };

  const handleConfirmDeleteDocument = () => {
    if (!documentToDelete) return;

    deleteDocument.mutate(documentToDelete.id, {
      onSuccess: () => {
        notifications.show({
          color: 'green',
          title: 'Documento excluído',
          message: `"${documentToDelete.title}" foi excluído desta base.`,
        });
        setDocumentToDelete(null);
      },
      onError: () => {
        notifications.show({
          color: 'red',
          title: 'Erro ao excluir documento',
          message: 'Não foi possível excluir o documento. Tente novamente.',
        });
        setDocumentToDelete(null);
      },
    });
  };

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

      <KnowledgeDocumentsCard
        documents={documentsQuery.data}
        isLoading={documentsQuery.isLoading}
        error={documentsQuery.error}
        onAdd={handleAddDocument}
        onUpdate={handleUpdateDocument}
        onDelete={setDocumentToDelete}
        onReindex={handleReindexDocument}
        reindexingId={reindexDocument.isPending ? reindexDocument.variables : null}
      />

      <KnowledgeBaseAgentsCard knowledgeBaseId={data.id} agents={agentsQuery.data} />

      <KnowledgeDocumentModal
        opened={documentModalOpened}
        document={editingDocumentId ? (editingDocumentQuery.data ?? null) : null}
        loadingDocument={editingDocumentId !== null && editingDocumentQuery.isLoading}
        onClose={() => {
          setDocumentModalOpened(false);
          setEditingDocumentId(null);
        }}
        onCreate={(input) => createDocument.mutateAsync(input)}
        onUpdate={(input) => updateDocument.mutateAsync({ id: editingDocumentId!, input })}
      />

      {/* Exclusão de documento passa por confirmação, e a confirmação NOMEIA os
          agentes afetados — derivados de GET /agents no cliente, a mesma
          requisição que o card de agentes já usa (agentUsage.ts). */}
      <Modal
        opened={documentToDelete !== null}
        onClose={() => setDocumentToDelete(null)}
        title={documentToDelete ? `Excluir "${documentToDelete.title}"?` : ''}
      >
        <Stack gap="sm">
          <Text size="sm">
            O texto e os fragmentos deste documento saem do índice. Não há como desfazer.
          </Text>
          {agentsQuery.data && agentsConsultingBase(agentsQuery.data, data.id).length > 0 && (
            <Text size="sm" data-testid="delete-document-affected-agents">
              Afeta{' '}
              {agentsConsultingBase(agentsQuery.data, data.id)
                .map((agent) => agent.name)
                .join(', ')}
              : esses agentes deixam de encontrar este conteúdo na próxima consulta.
            </Text>
          )}
        </Stack>

        <Group justify="flex-end" mt="md">
          <Button variant="default" onClick={() => setDocumentToDelete(null)}>
            Cancelar
          </Button>
          <Button
            color="red"
            onClick={handleConfirmDeleteDocument}
            loading={deleteDocument.isPending}
          >
            Excluir documento
          </Button>
        </Group>
      </Modal>

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
