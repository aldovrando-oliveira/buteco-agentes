import { Alert, Button, Group, Modal, Stack, Text } from '@mantine/core';
import { Link } from 'react-router';
import type { DeleteKnowledgeBaseFailure } from '../utils/deleteKnowledgeBaseMessages';

// CONFIRMAÇÃO DE EXCLUSÃO DE BASE, no padrão da exclusão de documento
// (KnowledgeBaseDetailPage, modal "Excluir documento"): título com o nome, o que
// sai, que não há volta, os agentes afetados, e Cancelar ao lado do botão
// vermelho com o nome da ação (design.md da change exclusao-base-painel, D2).
//
// Os dados chegam por prop, e cada um tem três estados (convenção 13): valor,
// vazio medido, e desconhecido (`null`). O desconhecido NUNCA vira zero nem
// "nenhum": a listagem ou o GET /agents que não respondeu não mediu nada.
//
// Sem digitar o nome para confirmar: a barreira é desativar antes, e o nome nem
// identifica a base, porque o cadastro aceita nomes repetidos (D2).

export interface KnowledgeBaseDeleteModalProps {
  opened: boolean;
  knowledgeBaseName: string;
  // Tamanho da listagem de documentos da base, que devolve a lista inteira (não
  // pagina): zero é medido. `null` enquanto ela carrega ou quando falhou.
  documentCount: number | null;
  // Nomes dos agentes vinculados, de agentsConsultingBase sobre GET /agents.
  // `null` enquanto ele carrega ou quando falhou.
  agentNames: string[] | null;
  // Só em base sincronizada.
  syncedFolder: { folderName: string; providerLabel: string } | null;
  pending: boolean;
  failure: DeleteKnowledgeBaseFailure | null;
  onConfirm: () => void;
  onClose: () => void;
}

const AND_LIST = new Intl.ListFormat('pt-BR', { style: 'long', type: 'conjunction' });

function documentsSentence(count: number | null): string {
  if (count === null) {
    return 'A base e todos os documentos dela são excluídos, com os fragmentos indexados e o histórico. Não há como desfazer.';
  }
  if (count === 0) {
    return 'A base é excluída, com o histórico. Ela não tem documentos. Não há como desfazer.';
  }
  if (count === 1) {
    return 'A base e o único documento dela são excluídos, com os fragmentos indexados e o histórico. Não há como desfazer.';
  }
  return `A base e os seus ${count} documentos são excluídos, com os fragmentos indexados e o histórico. Não há como desfazer.`;
}

// "já não consultam esta base, porque ela está inativa" é afirmação sobre outro
// app (convenção 13): a resolução de tools filtra as bases por IsActive em
// apps/workers/src/Buteco.Workers/Knowledge/Execution/KnowledgeToolSetResolver.cs:87.
// Gatilho: a resolução deixar de filtrar por IsActive.
function agentsSentence(names: string[] | null): string {
  if (names === null) {
    return 'Não foi possível conferir os agentes vinculados; os vínculos que existirem saem junto.';
  }
  if (names.length === 0) {
    return 'Nenhum agente está vinculado a esta base.';
  }
  if (names.length === 1) {
    return `O vínculo com ${names[0]} sai junto. Esse agente já não consulta esta base, porque ela está inativa.`;
  }
  return `Os vínculos com ${AND_LIST.format(names)} saem junto. Esses agentes já não consultam esta base, porque ela está inativa.`;
}

export function KnowledgeBaseDeleteModal({
  opened,
  knowledgeBaseName,
  documentCount,
  agentNames,
  syncedFolder,
  pending,
  failure,
  onConfirm,
  onClose,
}: KnowledgeBaseDeleteModalProps) {
  // Durante a requisição o modal não fecha: fechar esconderia o resultado.
  const close = () => {
    if (!pending) onClose();
  };
  const canConfirm = failure === null || failure.canRetry;

  return (
    <Modal opened={opened} onClose={close} title={`Excluir "${knowledgeBaseName}"?`}>
      <Stack gap="sm">
        <Text size="sm">{documentsSentence(documentCount)}</Text>
        <Text size="sm">{agentsSentence(agentNames)}</Text>
        {syncedFolder && (
          <Text size="sm">
            A pasta “{syncedFolder.folderName}” fica livre para ser usada por outra base. Os
            arquivos no {syncedFolder.providerLabel} não são tocados.
          </Text>
        )}
        {failure && (
          <Alert color="red" role="alert">
            {failure.message}
          </Alert>
        )}
      </Stack>

      <Group justify="flex-end" mt="md">
        {failure?.kind === 'not-found' && (
          <Button component={Link} to="/knowledge-bases" variant="default">
            Voltar para as bases
          </Button>
        )}
        <Button variant="default" onClick={close} disabled={pending}>
          {canConfirm ? 'Cancelar' : 'Fechar'}
        </Button>
        {canConfirm && (
          <Button color="red" onClick={onConfirm} loading={pending}>
            Excluir base
          </Button>
        )}
      </Group>
    </Modal>
  );
}
