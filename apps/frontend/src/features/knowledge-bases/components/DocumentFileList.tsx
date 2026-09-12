import {
  Alert,
  Badge,
  Box,
  Button,
  Code,
  FileButton,
  Group,
  Stack,
  Text,
  TextInput,
} from '@mantine/core';
import { useState, type DragEvent } from 'react';
import { ACCEPTED_ATTRIBUTE, collectFiles, type SelectedFile } from '../utils/documentUpload';

// Estado de envio por linha (design.md, D2). A linha `criado` deixa de ser
// enviável: um segundo acionamento reenvia só as que faltam, e nunca duplica o
// que já entrou.
export type FileSendState =
  { kind: 'idle' } | { kind: 'sending' } | { kind: 'created' } | { kind: 'failed'; error: string };

export interface DocumentFileEntry extends Record<string, unknown> {
  file: SelectedFile;
  send: FileSendState;
}

interface DocumentFileListProps {
  entries: DocumentFileEntry[];
  multiple: boolean;
  onEntriesAdded: (files: SelectedFile[]) => void;
  onTitleChange: (index: number, title: string) => void;
  onRemove: (index: number) => void;
  disabled?: boolean;
}

// Apresentacional: não importa hook de query nem de mutation (convenção 7).
//
// Sem @mantine/dropzone — conferido ausente do package.json e do node_modules, e
// o core já resolve (design.md, D6). FileButton dá o seletor com `multiple` e
// `accept`; a área de soltar são três handlers de arrastar.
export function DocumentFileList({
  entries,
  multiple,
  onEntriesAdded,
  onTitleChange,
  onRemove,
  disabled = false,
}: DocumentFileListProps) {
  const [dragging, setDragging] = useState(false);

  // PONTO ÚNICO DE TRATAMENTO. As duas entradas — seletor e arrastar — chegam
  // aqui, e nada mais (design.md, D13). É a convergência que faz o teste do
  // caminho do seletor, com File real, cobrir a lógica dos dois.
  const handleFiles = async (files: File[]) => {
    if (files.length === 0) return;
    onEntriesAdded(await collectFiles(files));
  };

  // Adaptador fino: preventDefault, extrai, delega. NENHUMA lógica própria — se
  // ganhar lógica, o teste de convergência reprova, que é o objetivo dele.
  const handleDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    setDragging(false);
    void handleFiles(Array.from(event.dataTransfer?.files ?? []));
  };

  // preventDefault no dragover é OBRIGATÓRIO: sem ele o navegador não dispara
  // `drop` e NAVEGA para o arquivo, deixando a área de soltar inteiramente
  // morta. O jsdom não tem essa regra, então nenhum teste desta suíte pega a
  // regressão — ela é item nomeado da conferência manual.
  const handleDragOver = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    if (!dragging) setDragging(true);
  };

  return (
    <Stack gap="sm">
      <Box
        data-testid="document-dropzone"
        onDragOver={handleDragOver}
        onDragLeave={(event) => {
          event.preventDefault();
          setDragging(false);
        }}
        onDrop={handleDrop}
        p="lg"
        style={{
          border: `1px dashed ${dragging ? 'var(--mantine-primary-color-filled)' : 'var(--mantine-color-default-border)'}`,
          borderRadius: 'var(--mantine-radius-md)',
          textAlign: 'center',
        }}
      >
        <Stack gap={4} align="center">
          <Text size="sm" fw={600}>
            {multiple ? 'Arraste arquivos markdown aqui' : 'Arraste o arquivo markdown aqui'}
          </Text>
          <FileButton
            multiple={multiple}
            accept={ACCEPTED_ATTRIBUTE}
            onChange={(picked) =>
              void handleFiles(multiple ? (picked as File[]) : [picked as File].filter(Boolean))
            }
          >
            {(props) => (
              <Button {...props} size="xs" variant="default" disabled={disabled}>
                Escolher no computador
              </Button>
            )}
          </FileButton>
          <Text size="xs" c="dimmed">
            .md · .markdown · .txt
          </Text>
        </Stack>
      </Box>

      {/* Diz o que o sistema VERIFICA — a recusa por extensão é real. Nada aqui
          relaciona tamanho a falha de indexação (convenção 13). */}
      <Text size="xs" c="dimmed">
        PDF, DOCX e imagens não são aceitos: a base guarda texto markdown, e mídia binária não é
        armazenada. Converta o arquivo antes de subir.
      </Text>

      {entries.map((entry, index) => (
        <Box
          key={`${entry.file.fileName}-${index}`}
          data-testid={`file-entry-${index}`}
          p="xs"
          style={{
            border: '1px solid var(--mantine-color-default-border)',
            borderRadius: 'var(--mantine-radius-sm)',
          }}
        >
          <Stack gap="xs">
            <Group justify="space-between" wrap="nowrap">
              <Group gap="xs" wrap="nowrap">
                <Code>{entry.file.fileName}</Code>
                <Text size="xs" c="dimmed">
                  {entry.file.size}
                </Text>
              </Group>
              <Group gap="xs" wrap="nowrap">
                {entry.send.kind === 'created' && (
                  <Badge color="green" data-testid={`file-created-${index}`}>
                    Adicionado
                  </Badge>
                )}
                {entry.send.kind === 'sending' && <Badge color="blue">Enviando</Badge>}
                {entry.send.kind !== 'created' && (
                  <Button
                    size="compact-xs"
                    variant="subtle"
                    color="gray"
                    aria-label={`Remover ${entry.file.fileName}`}
                    onClick={() => onRemove(index)}
                    disabled={disabled}
                  >
                    ×
                  </Button>
                )}
              </Group>
            </Group>

            {entry.file.accepted && entry.send.kind !== 'created' && (
              <TextInput
                label="Título do documento"
                value={entry.file.title}
                onChange={(event) => onTitleChange(index, event.currentTarget.value)}
                disabled={disabled}
              />
            )}

            {/* Arquivo recusado entra na lista COM o motivo — nunca descartado
                em silêncio. */}
            {!entry.file.accepted && (
              <Alert color="red" py={4} data-testid={`file-rejected-${index}`}>
                {entry.file.error}
              </Alert>
            )}

            {entry.send.kind === 'failed' && (
              <Alert color="red" py={4} data-testid={`file-failed-${index}`}>
                {entry.send.error}
              </Alert>
            )}
          </Stack>
        </Box>
      ))}
    </Stack>
  );
}
