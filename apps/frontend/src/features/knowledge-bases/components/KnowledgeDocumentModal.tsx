import {
  Alert,
  Button,
  Group,
  Loader,
  Modal,
  SegmentedControl,
  Select,
  Stack,
  Text,
  Textarea,
  TextInput,
} from '@mantine/core';
import { useEffect, useMemo, useState } from 'react';
import { DocumentFileList, type DocumentFileEntry } from './DocumentFileList';
import type { SelectedFile } from '../utils/documentUpload';
import type { CreateKnowledgeDocumentInput, KnowledgeDocument } from '../types/knowledgeDocument';

const SOURCE_TYPE = 'markdown';

type Mode = 'upload' | 'manual';

interface KnowledgeDocumentModalProps {
  opened: boolean;
  /** `null` ao adicionar; o documento completo (com `extractedText`) ao atualizar. */
  document: KnowledgeDocument | null;
  loadingDocument?: boolean;
  onClose: () => void;
  /** Cria UM documento. Rejeita em caso de falha — o modal trata por linha. */
  onCreate: (input: CreateKnowledgeDocumentInput) => Promise<unknown>;
  onUpdate: (input: CreateKnowledgeDocumentInput) => Promise<unknown>;
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Não foi possível criar este documento.';
}

export function KnowledgeDocumentModal({
  opened,
  document,
  loadingDocument = false,
  onClose,
  onCreate,
  onUpdate,
}: KnowledgeDocumentModalProps) {
  const isUpdate = document !== null;

  const [mode, setMode] = useState<Mode>('upload');
  const [entries, setEntries] = useState<DocumentFileEntry[]>([]);
  const [title, setTitle] = useState('');
  const [content, setContent] = useState('');
  const [titleError, setTitleError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Ao abrir: adicionar começa em "subir arquivos"; atualizar começa em
  // "escrever manualmente", já com o conteúdo atual carregado.
  useEffect(() => {
    if (!opened) return;
    setMode(document ? 'manual' : 'upload');
    setEntries([]);
    setTitle(document?.title ?? '');
    setContent(document?.extractedText ?? '');
    setTitleError(null);
    setFormError(null);
    setSubmitting(false);
  }, [opened, document]);

  const accepted = entries.filter((e) => e.file.accepted);
  const pendentes = accepted.filter((e) => e.send.kind !== 'created');

  // O CONTEÚDO MUDOU?
  //
  // Comparação local de string, porque `contentHash` NÃO sai no fio em resposta
  // nenhuma (design.md, D5). O comentário de KnowledgeDocument.Update() diz que,
  // para toda linha criada a partir da 2a, o hash e a comparação de string
  // coincidem — elas só divergem na linha LEGADA, anterior à 2a, cujo hash é
  // nulo: ali a string diz "não mudou" e o servidor reindexa assim mesmo.
  //
  // Nesse caso a UI erra por OMISSÃO (deixa de avisar sobre uma reindexação que
  // vai acontecer), nunca por excesso. É o lado seguro, e está declarado na spec
  // em vez de escondido.
  const contentChanged = useMemo(() => {
    if (!document) return true;
    if (mode === 'upload') {
      const escolhido = accepted[0];
      return escolhido !== undefined && escolhido.file.accepted
        ? escolhido.file.content !== document.extractedText
        : false;
    }
    return content !== document.extractedText;
  }, [document, mode, accepted, content]);

  const handleEntriesAdded = (files: SelectedFile[]) => {
    setFormError(null);
    setEntries((current) => {
      const novos = files.map((file) => ({ file, send: { kind: 'idle' as const } }));
      // Ao substituir, um arquivo só: o último escolhido vence.
      return isUpdate ? novos.slice(-1) : [...current, ...novos];
    });
  };

  const handleTitleChange = (index: number, value: string) => {
    setFormError(null);
    setEntries((current) =>
      current.map((entry, i) =>
        i === index && entry.file.accepted
          ? { ...entry, file: { ...entry.file, title: value } }
          : entry,
      ),
    );
  };

  const handleRemove = (index: number) => {
    setFormError(null);
    setEntries((current) => current.filter((_, i) => i !== index));
  };

  // FALHA PARCIAL NO LOTE (design.md, D2).
  //
  // N chamadas INDEPENDENTES ao POST unitário, SEQUENCIAIS na ordem da lista. A
  // sequência não é sobre carga — N é a escolha de um operador, não um lote de
  // sistema: é para o estado parcial ser legível. "As três primeiras entraram, a
  // quarta falhou, a quinta não foi tentada" é frase verdadeira; com paralelismo
  // o conjunto que entrou é arbitrário.
  //
  // A linha `created` deixa de ser enviável, então um segundo acionamento manda
  // só o que falta e NUNCA duplica o que já entrou.
  //
  // O modal NÃO FECHA em falha parcial: fechar apagaria a única cópia do que
  // falhou e por quê.
  const submitUpload = async () => {
    if (pendentes.length === 0) {
      setFormError('Escolha pelo menos um arquivo .md, .markdown ou .txt.');
      return;
    }

    if (pendentes.some((e) => e.file.accepted && !e.file.title.trim())) {
      setFormError('Todo documento precisa de um título.');
      return;
    }

    setSubmitting(true);
    setFormError(null);
    let houveFalha = false;

    for (let index = 0; index < entries.length; index += 1) {
      const entry = entries[index];
      if (!entry.file.accepted || entry.send.kind === 'created') continue;

      setEntries((current) =>
        current.map((e, i) => (i === index ? { ...e, send: { kind: 'sending' } } : e)),
      );

      try {
        const input = {
          title: entry.file.title.trim(),
          sourceType: SOURCE_TYPE,
          content: entry.file.content,
        };

        if (isUpdate) {
          // Substituir por arquivo PRESERVA o título atual do documento.
          await onUpdate({ ...input, title: document!.title });
        } else {
          await onCreate(input);
        }

        setEntries((current) =>
          current.map((e, i) => (i === index ? { ...e, send: { kind: 'created' } } : e)),
        );
      } catch (error) {
        houveFalha = true;
        setEntries((current) =>
          current.map((e, i) =>
            i === index ? { ...e, send: { kind: 'failed', error: errorMessage(error) } } : e,
          ),
        );
      }
    }

    setSubmitting(false);

    if (!houveFalha) {
      onClose();
    }
  };

  const submitManual = async () => {
    if (!title.trim()) {
      setTitleError('O título do documento é obrigatório.');
      return;
    }

    setSubmitting(true);
    setTitleError(null);
    setFormError(null);

    try {
      const input = { title: title.trim(), sourceType: SOURCE_TYPE, content };
      if (isUpdate) {
        await onUpdate(input);
      } else {
        await onCreate(input);
      }
      onClose();
    } catch (error) {
      setFormError(errorMessage(error));
    } finally {
      setSubmitting(false);
    }
  };

  // O rótulo conta o que SERÁ TENTADO — isso é verdade. Afirmar o desfecho não
  // seria.
  const submitLabel = () => {
    if (isUpdate) return contentChanged ? 'Salvar e reindexar' : 'Salvar';
    if (mode === 'manual') return 'Adicionar documento';
    return pendentes.length > 1
      ? `Adicionar ${pendentes.length} documentos`
      : 'Adicionar documento';
  };

  return (
    <Modal
      opened={opened}
      onClose={onClose}
      title={isUpdate ? 'Atualizar documento' : 'Adicionar documento'}
      size="lg"
    >
      {loadingDocument ? (
        <Group gap="xs">
          <Loader size="sm" />
          <Text size="sm">Carregando documento...</Text>
        </Group>
      ) : (
        <Stack gap="md">
          <SegmentedControl
            value={mode}
            onChange={(value) => {
              setMode(value as Mode);
              setFormError(null);
            }}
            data={[
              { value: 'upload', label: isUpdate ? 'Substituir por arquivo' : 'Subir arquivos' },
              { value: 'manual', label: 'Escrever manualmente' },
            ]}
          />

          {mode === 'upload' ? (
            <Stack gap="sm">
              <DocumentFileList
                entries={entries}
                multiple={!isUpdate}
                onEntriesAdded={handleEntriesAdded}
                onTitleChange={handleTitleChange}
                onRemove={handleRemove}
                disabled={submitting}
              />

              {/* O rodapé NÃO promete conjunto. O protótipo diz "2 documentos
                  serão criados e entram como pendentes", que afirma atomicidade
                  que o desenho não sustenta: se a segunda chamada falhar, a
                  primeira JÁ CRIOU um documento. */}
              {!isUpdate && pendentes.length > 0 && (
                <Text size="xs" c="dimmed" data-testid="batch-footer">
                  Cada arquivo vira um documento próprio. Se algum falhar, os que já entraram
                  continuam criados.
                </Text>
              )}
            </Stack>
          ) : (
            <Stack gap="sm">
              <TextInput
                label="Título"
                required
                value={title}
                onChange={(event) => {
                  setTitle(event.currentTarget.value);
                  setTitleError(null);
                }}
                error={titleError}
              />

              {/* Desabilitado com um único valor: mostra que o campo existe e
                  que hoje não há escolha. */}
              <Select
                label="Tipo de origem"
                data={[SOURCE_TYPE]}
                value={SOURCE_TYPE}
                disabled
                description="Único tipo suportado por enquanto. Mídia binária não é armazenada — o que fica na base é texto."
              />

              {/* Altura fixa com rolagem própria, como no protótipo — e não
                  `autosize`: o Autosize do Mantine acessa `defaultView` do nó e
                  estoura no jsdom, o que tiraria a cobertura de todo este modo
                  em troca de nada visível ao operador. */}
              <Textarea
                label="Conteúdo (markdown)"
                rows={12}
                styles={{ input: { fontFamily: 'var(--mantine-font-family-monospace)' } }}
                value={content}
                onChange={(event) => setContent(event.currentTarget.value)}
              />
            </Stack>
          )}

          {/* AS DUAS CÓPIAS DE EFEITO (design.md, D5). A tela não sabe de
              antemão em qual caso o operador está, então cobre os dois — e
              remover o que era falso era só metade do trabalho. */}
          {isUpdate &&
            (contentChanged ? (
              <Alert color="yellow" py="xs" data-testid="update-reindex-warning">
                O conteúdo mudou: o documento volta para pendente e será indexado de novo. O
                conteúdo indexado anteriormente continua respondendo até a nova indexação terminar
                com sucesso — e se ela falhar, ele permanece.
              </Alert>
            ) : (
              <Alert color="gray" py="xs" data-testid="update-no-reindex-note">
                O conteúdo não mudou: salvar não reindexa o documento, e o estado de indexação
                continua o mesmo.
              </Alert>
            ))}

          {!isUpdate && (
            <Text size="xs" c="dimmed">
              Ao adicionar, o documento entra como pendente e a indexação roda em segundo plano. Ele
              só aparece nas consultas do agente depois de indexado.
            </Text>
          )}

          {formError && (
            <Alert color="red" py="xs" data-testid="modal-error">
              {formError}
            </Alert>
          )}

          <Group justify="flex-end">
            <Button variant="default" onClick={onClose} disabled={submitting}>
              Cancelar
            </Button>
            <Button
              onClick={() => void (mode === 'upload' ? submitUpload() : submitManual())}
              loading={submitting}
            >
              {submitLabel()}
            </Button>
          </Group>
        </Stack>
      )}
    </Modal>
  );
}
