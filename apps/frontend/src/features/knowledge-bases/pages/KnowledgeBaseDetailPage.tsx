import { Alert, Badge, Button, Group, Loader, Modal, Stack, Tabs, Text } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { notifications } from '@mantine/notifications';
import { useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';
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
import { KnowledgeIndexDiagnosticsTab } from '../components/KnowledgeIndexDiagnosticsTab';
import { ApiError } from '../api/knowledgeBasesApi';
import { useKnowledgeIndexDiagnosticsQuery } from '../api/useKnowledgeIndex';
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

// A BARRA DE ABAS NASCE AQUI, e o gatilho registrado pela 5a-1 (D3) era este:
// uma barra com uma aba só afirmaria uma estrutura que a tela não tinha, então
// ela esperaria a segunda aba de verdade. A segunda chegou — o diagnóstico do
// índice, cuja rota de backend entrou em 13/09/2026.
//
// O desenho é o do detalhe do agente (`frontend-agente-detalhe-abas`), reusado e
// não reinventado: a aba ativa vive no endereço, a PRIMEIRA é a forma canônica e
// não carrega parâmetro, e valor desconhecido cai nela SEM reescrever o endereço
// — reescrever só poluiria o histórico.
const DOCUMENTS_TAB = 'documentos';
const DIAGNOSTICS_TAB = 'diagnostico';

type KnowledgeBaseDetailTab = typeof DOCUMENTS_TAB | typeof DIAGNOSTICS_TAB;

function parseTab(value: string | null): KnowledgeBaseDetailTab {
  return value === DIAGNOSTICS_TAB ? value : DOCUMENTS_TAB;
}

// CONTADOR SÓ COM A LISTAGEM RESPONDIDA, e a diferença em relação ao detalhe do
// agente é a razão de existir esta função em vez de reusar a de lá: no agente a
// contagem JÁ ESTÁ NA MÃO quando a barra renderiza (vem do próprio agente);
// aqui ela vem de uma segunda requisição, e um `0` durante o carregamento
// afirmaria uma contagem que ainda não foi feita (convenção 13, design.md D4).
function DocumentsTabCounter({ documents }: { documents: KnowledgeDocumentSummary[] | undefined }) {
  if (!documents || documents.length === 0) {
    return null;
  }

  return (
    <Badge size="sm" variant="default" circle>
      {documents.length}
    </Badge>
  );
}

export function KnowledgeBaseDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
  const activeTab = parseTab(searchParams.get('tab'));
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

  // A proveniência é buscada SÓ com a aba de diagnóstico ativa: a rota percorre o
  // heap inteiro da tabela de fragmentos, e abrir o detalhe para ver documentos
  // não deve pagar essa varredura (design.md, D5). O acompanhamento da única
  // transição que a muda depende dos documentos desta base, por isso eles vão
  // junto.
  const diagnosticsQuery = useKnowledgeIndexDiagnosticsQuery({
    enabled: activeTab === DIAGNOSTICS_TAB,
    documents: documentsQuery.data,
  });

  const handleTabChange = (value: string | null) => {
    const next = parseTab(value);
    setSearchParams(next === DOCUMENTS_TAB ? {} : { tab: next });
  };

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

      {/* keepMounted={false}: só a aba ativa existe no DOM. É o que sustenta o
          `enabled` da consulta de proveniência — a aba inativa não mantém
          observador vivo nem intervalo armado por trás dela. */}
      <Tabs value={activeTab} onChange={handleTabChange} keepMounted={false}>
        <Tabs.List>
          {/* ALTURA FIXA, e não padding — o mesmo defeito e a mesma correção da
              faixa de cabeçalho do `SectionedCard`. Medido na conferência: a
              barra sai com 34px enquanto a listagem de documentos não respondeu e
              40px depois que o contador aparece, ou seja ela CRESCE 6px sob o
              conteúdo já renderizado. Com altura própria, a barra é a mesma com e
              sem contador. */}
          <Tabs.Tab
            value={DOCUMENTS_TAB}
            h={40}
            rightSection={<DocumentsTabCounter documents={documentsQuery.data} />}
          >
            Documentos
          </Tabs.Tab>
          <Tabs.Tab value={DIAGNOSTICS_TAB} h={40}>
            Diagnóstico do índice
          </Tabs.Tab>
        </Tabs.List>

        <Tabs.Panel value={DOCUMENTS_TAB} pt="md">
          <Stack gap="md">
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
          </Stack>
        </Tabs.Panel>

        <Tabs.Panel value={DIAGNOSTICS_TAB} pt="md">
          <KnowledgeIndexDiagnosticsTab
            diagnostics={diagnosticsQuery.data}
            isLoading={diagnosticsQuery.isLoading}
            error={diagnosticsQuery.error}
            documents={documentsQuery.data}
            documentsError={documentsQuery.error}
            onGoToDocuments={() => handleTabChange(DOCUMENTS_TAB)}
          />
        </Tabs.Panel>
      </Tabs>

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
