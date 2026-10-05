import { Alert, Badge, Button, Code, Group, Loader, Stack, Table, Text } from '@mantine/core';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { SectionLabel } from '../../../components/data/SectionLabel';
import {
  documentNote,
  fragmentCountLabel,
  nonTerminalCount,
  statusPresentation,
} from '../utils/documentIndexing';
import type { KnowledgeDocumentSummary } from '../types/knowledgeDocument';

interface KnowledgeDocumentsCardProps {
  documents: KnowledgeDocumentSummary[] | undefined;
  isLoading: boolean;
  error: unknown;
  // Base sincronizada: os documentos vêm da pasta e o apps/api recusa criar,
  // atualizar e excluir com 409 (KnowledgeDocumentEndpoints.cs, `SyncedKnowledgeBaseConflict`).
  // Sem as três ações e sem a coluna; reindexar continua, porque a rota dele não
  // tem essa guarda e a sincronização não reenvia arquivo com o mesmo marcador
  // (frontend-detalhe-base-sincronizada, D1). Obrigatória: quem monta o card
  // decide pela origem da base.
  readOnly: boolean;
  onAdd?: () => void;
  onUpdate?: (document: KnowledgeDocumentSummary) => void;
  onDelete?: (document: KnowledgeDocumentSummary) => void;
  onReindex: (document: KnowledgeDocumentSummary) => void;
  reindexingId?: string | null;
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('pt-BR');
}

// Apresentacional: não importa hook de query nem de mutation (convenção 7, D11).
// A página busca e repassa; as ações chegam como callbacks.

function FragmentCountCell({ document }: { document: KnowledgeDocumentSummary }) {
  const label = fragmentCountLabel(document);

  // Célula VAZIA quando não há contagem a exibir — nunca zero, nunca travessão
  // no lugar de um número que existiria. A regra e a causa estão em
  // utils/documentIndexing.ts.
  if (label === null) {
    return null;
  }

  return (
    <Text size="sm" c="dimmed" ff="monospace">
      {label}
    </Text>
  );
}

// Faixa de falha de largura total, sob a linha. Alert com variante default
// `light`, que resolve a cor por theme.variantColorResolver e troca sozinha
// entre os esquemas — nenhuma variável nova por esquema é necessária
// (design.md, D9). Mesmo molde de AgentKnowledgeTab.
//
// O motivo vem COMPLETO, sem truncar: é a única cópia da falha que a tela tem, e
// apps/api garante que ele é texto legível por operador. Nada de lineClamp aqui.
//
// O texto é renderizado COMO VEIO, sem interpretação, reescrita ou complemento —
// em particular, sem nenhum conselho sobre tamanho de documento, que é coisa que
// o sistema não verifica (convenção 13).
function FailureBand({
  colSpan,
  document,
  onReindex,
  reindexing,
}: {
  colSpan: number;
  document: KnowledgeDocumentSummary;
  onReindex: () => void;
  reindexing: boolean;
}) {
  return (
    <Table.Tr>
      <Table.Td colSpan={colSpan} p={0}>
        <Alert color="red" radius={0} py="xs" title="Falhou ao indexar">
          <Stack gap="xs" align="flex-start">
            {/* Medida limitada a 820px, como o protótipo especifica — e isso
                NÃO é truncar: o texto vem inteiro, só não atravessa 1.500px de
                linha, que é ilegível. A conferência da rodada 1 pegou isto
                comparando DIMENSÃO, não estado (foi exatamente o que a 5b
                deixou passar). */}
            <Text size="sm" maw={820} data-testid={`failure-reason-${document.id}`}>
              {document.failureReason ??
                'O motivo desta falha não foi registrado. Reindexar volta a enfileirar o documento.'}
            </Text>
            <Button
              size="xs"
              variant="outline"
              color="red"
              onClick={onReindex}
              loading={reindexing}
            >
              Reindexar documento
            </Button>
          </Stack>
        </Alert>
      </Table.Td>
    </Table.Tr>
  );
}

export function KnowledgeDocumentsCard({
  documents,
  isLoading,
  error,
  readOnly,
  onAdd,
  onUpdate,
  onDelete,
  onReindex,
  reindexingId,
}: KnowledgeDocumentsCardProps) {
  // No lugar do botão, a explicação da prancha 4a: a propriedade é da base e
  // permanente, e cabe no cabeçalho sem disputar atenção com a faixa de itens
  // não utilizáveis (D1).
  const headerAction = readOnly ? (
    <Text size="xs" c="dimmed">
      Somente leitura — o conteúdo vem da pasta
    </Text>
  ) : (
    <Button size="xs" onClick={onAdd}>
      Adicionar documento
    </Button>
  );

  if (isLoading) {
    return (
      <SectionedCard title="Documentos">
        <SectionedCard.Body>
          <Group gap="xs">
            <Loader size="sm" />
            <Text size="sm">Carregando documentos...</Text>
          </Group>
        </SectionedCard.Body>
      </SectionedCard>
    );
  }

  // Falha ao listar NÃO vira estado vazio: uma requisição que não respondeu não
  // é evidência de ausência. Dizer "nenhum documento" aqui seria afirmar o que o
  // sistema não sabe (convenção 13).
  if (error || !documents) {
    return (
      <SectionedCard title="Documentos">
        <SectionedCard.Body>
          <Alert color="red" data-testid="documents-load-error">
            Não foi possível carregar os documentos desta base. A listagem não foi lida, então não
            há como dizer quantos existem.
          </Alert>
        </SectionedCard.Body>
      </SectionedCard>
    );
  }

  // Estado vazio VERIFICADO — a listagem foi consultada e voltou vazia. É o que
  // distingue este texto da nota de sequenciamento que a 5a-1 tinha aqui.
  if (documents.length === 0) {
    return (
      <SectionedCard title="Documentos" action={headerAction}>
        <SectionedCard.Body>
          <Stack gap="xs" align="flex-start">
            <Text size="sm" data-testid="documents-empty">
              Nenhum documento nesta base.
            </Text>
            <Text size="xs" c="dimmed">
              {readOnly
                ? 'Os documentos entram pela sincronização com a pasta.'
                : 'Uma consulta do agente não devolve nada até algum documento ficar indexado.'}
            </Text>
          </Stack>
        </SectionedCard.Body>
      </SectionedCard>
    );
  }

  const pendentes = nonTerminalCount(documents);

  return (
    <Stack gap="xs">
      {/* Faixa de resumo, derivada da própria listagem — sem requisição
          adicional. Diz quantos e não QUANTO: o sistema conhece o estado, não o
          percentual, então não há barra de progresso em lugar nenhum. */}
      {pendentes > 0 && (
        <Alert color="yellow" py="xs" data-testid="documents-pending-summary">
          {pendentes === 1
            ? '1 documento ainda não está utilizável'
            : `${pendentes} documentos ainda não estão utilizáveis`}{' '}
          — a indexação roda em segundo plano e o estado de cada um está na lista abaixo.
        </Alert>
      )}

      <SectionedCard title="Documentos" action={headerAction}>
        {/* Somente leitura: as larguras da prancha 4a com layout fixo. Sem a
            coluna Ações, o layout automático dava 49% à coluna Documento contra
            60% na prancha (conferência visual, R1-1). A tabela da base manual
            continua com o layout de hoje. */}
        <Table layout={readOnly ? 'fixed' : undefined}>
          <Table.Thead bg="var(--buteco-surface-subtle)">
            <Table.Tr>
              <Table.Th w={readOnly ? '56%' : undefined}>
                <SectionLabel>Documento</SectionLabel>
              </Table.Th>
              <Table.Th w={readOnly ? '16%' : undefined}>
                <SectionLabel>Fragmentos</SectionLabel>
              </Table.Th>
              <Table.Th w={readOnly ? '18%' : undefined}>
                <SectionLabel>Indexação</SectionLabel>
              </Table.Th>
              {!readOnly && (
                <Table.Th>
                  <SectionLabel>Ações</SectionLabel>
                </Table.Th>
              )}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {/* A ordem é a da resposta. apps/api já ordena por CreatedAt com
                desempate por Id; reordenar aqui criaria uma segunda ordenação,
                com o comparador do .NET contra a collation do Postgres. */}
            {documents.map((document) => {
              const status = statusPresentation(document.indexingStatus);
              const failed = document.indexingStatus === 'Failed';

              return [
                <Table.Tr key={document.id} data-testid={`document-row-${document.id}`}>
                  <Table.Td>
                    <Stack gap={2}>
                      <Group gap="xs" wrap="nowrap">
                        <Text size="sm" fw={600}>
                          {document.title}
                        </Text>
                        <Code>{document.sourceType}</Code>
                      </Group>
                      <Text size="xs" c="dimmed">
                        {documentNote(document, formatDate(document.updatedAt))}
                      </Text>
                    </Stack>
                  </Table.Td>
                  <Table.Td>
                    <FragmentCountCell document={document} />
                  </Table.Td>
                  <Table.Td>
                    <Group gap="xs" wrap="nowrap">
                      {document.indexingStatus === 'Indexing' && (
                        <Loader size={12} data-testid={`document-indexing-${document.id}`} />
                      )}
                      <Badge color={status.color}>{status.label}</Badge>
                    </Group>
                  </Table.Td>
                  {!readOnly && (
                    <Table.Td>
                      <Group gap="xs" wrap="nowrap">
                        <Button size="xs" variant="default" onClick={() => onUpdate?.(document)}>
                          Atualizar
                        </Button>
                        <Button
                          size="xs"
                          variant="subtle"
                          color="red"
                          onClick={() => onDelete?.(document)}
                        >
                          Excluir
                        </Button>
                      </Group>
                    </Table.Td>
                  )}
                </Table.Tr>,
                failed ? (
                  <FailureBand
                    key={`${document.id}-failure`}
                    colSpan={readOnly ? 3 : 4}
                    document={document}
                    onReindex={() => onReindex(document)}
                    reindexing={reindexingId === document.id}
                  />
                ) : null,
              ];
            })}
          </Table.Tbody>
        </Table>
      </SectionedCard>
    </Stack>
  );
}
