import { Group, Stack, Text } from '@mantine/core';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { formatSyncInstant } from '../utils/syncState';
import { ignoredFileReason } from '../utils/syncStateMessages';
import type { KnowledgeBaseIgnoredFile } from '../types/knowledgeBase';

interface KnowledgeBaseIgnoredFilesCardProps {
  ignoredFiles: KnowledgeBaseIgnoredFile[] | null;
  lastCompletedAt: string | null;
  failing: boolean;
}

function countLabel(files: KnowledgeBaseIgnoredFile[]): string {
  if (files.length === 0) {
    return 'Nenhum';
  }
  return files.length === 1 ? '1 arquivo' : `${files.length} arquivos`;
}

// Arquivos da pasta que não entraram na base (prancha 4a), apresentacional
// (design.md da change frontend-detalhe-base-sincronizada, D3).
//
// OS DOIS VAZIOS NÃO SÃO O MESMO FATO (convenção 13):
//   - `null`: nenhum ciclo bem-sucedido foi gravado, a lista NÃO EXISTE ainda —
//     sem contagem, e o texto diz quando ela aparece;
//   - `[]`: o último ciclo bem-sucedido não recusou nenhum arquivo. "Não foi
//     recusado", e não "todos entraram": arquivo em contenção nem entra nem é
//     recusado (#105, D7).
// Em falha, a lista é a do último sucesso (a falha não a toca, #102), e o card
// diz isso, para ela não ser lida como o estado da pasta agora.
export function KnowledgeBaseIgnoredFilesCard({
  ignoredFiles,
  lastCompletedAt,
  failing,
}: KnowledgeBaseIgnoredFilesCardProps) {
  const count =
    ignoredFiles === null ? null : (
      <Text size="xs" c="dimmed">
        {countLabel(ignoredFiles)}
      </Text>
    );

  return (
    <SectionedCard
      title="Arquivos da pasta que não entraram na base"
      action={count}
      data-testid="ignored-files-card"
    >
      {ignoredFiles === null ? (
        <SectionedCard.Body>
          <Text size="sm" c="dimmed">
            A lista aparece depois da primeira sincronização concluída.
          </Text>
        </SectionedCard.Body>
      ) : ignoredFiles.length === 0 ? (
        <SectionedCard.Body>
          <Text size="sm" c="dimmed">
            Nenhum arquivo foi recusado na última sincronização concluída.
          </Text>
        </SectionedCard.Body>
      ) : (
        <Stack gap={0}>
          {failing && lastCompletedAt && (
            <SectionedCard.Body>
              <Text size="xs" c="dimmed">
                {`Lista da última sincronização concluída, em ${formatSyncInstant(lastCompletedAt)}.`}
              </Text>
            </SectionedCard.Body>
          )}
          {/* A ordem é a da resposta, sem reordenar. Sem link para o arquivo:
              `externalRef` é o id no provedor, e montar a URL dele é
              conhecimento do conector (D3). */}
          {ignoredFiles.map((file) => (
            <SectionedCard.Row key={file.externalRef}>
              <Group justify="space-between" wrap="nowrap" gap="md" align="flex-start">
                <Text size="sm">{file.name}</Text>
                <Text size="sm" c="dimmed" ta="right" maw="60%">
                  {ignoredFileReason(file)}
                </Text>
              </Group>
            </SectionedCard.Row>
          ))}
        </Stack>
      )}
    </SectionedCard>
  );
}
