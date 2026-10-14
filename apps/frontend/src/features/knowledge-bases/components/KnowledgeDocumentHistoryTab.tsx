import { Alert, Badge, Box, Button, Group, Loader, Stack, Text, Title } from '@mantine/core';
import { useEffect, useRef } from 'react';
import { SectionedCard } from '../../../components/data/SectionedCard';
import {
  authorLabel,
  eventDetail,
  eventTypeLabel,
  formatEventTime,
  groupEventsByDay,
} from '../utils/documentHistory';
import type { KnowledgeDocumentEvent } from '../types/knowledgeDocumentEvent';

// A ABA HISTÓRICO (design.md da change frontend-aba-historico). Apresentacional:
// a página busca e repassa, e as ações chegam como callbacks (convenção 7).
//
// OS ESTADOS SÃO DERIVADOS DE `events`, E NÃO DE `status` (D4). Com dados na
// mão, uma falha é do `Atualizar` ou da página seguinte, e a lista FICA: o que
// está na tela foi lido, só não é o mais novo, ou não é tudo. Só sem dados uma
// falha é "não foi possível carregar o histórico". `events === undefined` é
// "não respondeu", e nunca vira lista vazia — vazio é uma resposta que chegou
// dizendo que não há (convenção 13).
interface KnowledgeDocumentHistoryTabProps {
  // `undefined` enquanto a primeira página não respondeu, ou quando ela falhou.
  events: KnowledgeDocumentEvent[] | undefined;
  firstPageFailed: boolean;
  hasNextPage: boolean;
  isFetchingNextPage: boolean;
  nextPageFailed: boolean;
  isRefreshing: boolean;
  refreshFailed: boolean;
  synced: boolean;
  onRetry: () => void;
  onLoadMore: () => void;
  onRetryNextPage: () => void;
  onRefresh: () => void;
}

// Cores do Badge pelo `variant="light"` default do tema, que troca de ponta com
// o esquema sozinho (convenção 16; correção C5 — a prancha crava hex do claro).
// O desconhecido vai em `outline`, para não se confundir com o cinza-claro de
// `Excluído` (D3).
const TYPE_COLOR: Record<string, string> = {
  Created: 'green',
  Updated: 'blue',
  Deleted: 'gray',
};

function EventTypeBadge({ type }: { type: string }) {
  const { label, known } = eventTypeLabel(type);
  return known ? (
    <Badge color={TYPE_COLOR[type]}>{label}</Badge>
  ) : (
    <Badge color="gray" variant="outline" data-unknown-type>
      {label}
    </Badge>
  );
}

function HistoryNotes({ synced }: { synced: boolean }) {
  return (
    <Stack gap={4} data-testid="history-notes">
      {/* Sem data: o painel não sabe quando a #98 entrou no ar, e a D10 dela
          fixou o começo, não um dia (D9). */}
      <Text size="xs" c="dimmed">
        O histórico é registrado a partir da implantação desta funcionalidade. Documentos incluídos
        antes disso não têm evento de inclusão.
      </Text>
      {synced && (
        <Text size="xs" c="dimmed">
          Arquivos ignorados e falhas de acesso não entram no histórico: eles aparecem no estado
          atual da base.
        </Text>
      )}
    </Stack>
  );
}

const ROW_COLUMNS = '56px 120px minmax(0, 1fr) minmax(120px, auto)';

function EventRow({ event, index }: { event: KnowledgeDocumentEvent; index: number }) {
  const detail = eventDetail(event);
  return (
    // `tabIndex={-1}`: alvo do foco depois de "Carregar mais" (D8), sem entrar
    // na ordem do Tab.
    <Box
      component="li"
      tabIndex={-1}
      data-event-index={index}
      data-testid="history-event"
      px="md"
      py="sm"
      style={{
        display: 'grid',
        gridTemplateColumns: ROW_COLUMNS,
        gap: 'var(--mantine-spacing-sm)',
        alignItems: 'center',
        borderTop: '1px solid var(--mantine-color-default-border)',
      }}
    >
      {/* O instante completo para máquina; para os olhos, só a hora — o dia já
          está no título do grupo. */}
      <Text component="time" dateTime={event.occurredAt} size="sm" c="dimmed" ff="monospace">
        {formatEventTime(event.occurredAt)}
      </Text>
      <Box>
        <EventTypeBadge type={event.type} />
      </Box>
      <Stack gap={2} style={{ minWidth: 0 }}>
        {/* O título GRAVADO NO EVENTO, sem link e sem cruzar com a listagem:
            um documento renomeado ou que já saiu aparece como era (D7). */}
        <Text size="sm" fw={600} style={{ overflowWrap: 'anywhere' }}>
          {event.documentTitle}
        </Text>
        {detail && (
          <Text size="xs" c="dimmed">
            {detail}
          </Text>
        )}
      </Stack>
      <Text size="xs" c="dimmed" ta="right" data-testid="history-event-author">
        {authorLabel(event.author)}
      </Text>
    </Box>
  );
}

export function KnowledgeDocumentHistoryTab({
  events,
  firstPageFailed,
  hasNextPage,
  isFetchingNextPage,
  nextPageFailed,
  isRefreshing,
  refreshFailed,
  synced,
  onRetry,
  onLoadMore,
  onRetryNextPage,
  onRefresh,
}: KnowledgeDocumentHistoryTabProps) {
  // FOCO NO PRIMEIRO EVENTO ACRESCENTADO (D8). Sem isto, na última página o
  // botão some e o foco cai no `body`; e com o botão presente, quem usa leitor
  // de tela continuaria de onde estava, e não de onde a lista cresceu.
  const pendingFocusIndex = useRef<number | null>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const loadedCount = events?.length ?? 0;

  useEffect(() => {
    const index = pendingFocusIndex.current;
    if (index === null || loadedCount <= index) return;
    pendingFocusIndex.current = null;
    listRef.current?.querySelector<HTMLElement>(`[data-event-index="${index}"]`)?.focus();
  }, [loadedCount]);

  const handleLoadMore = () => {
    pendingFocusIndex.current = loadedCount;
    onLoadMore();
  };

  const handleRefresh = () => {
    pendingFocusIndex.current = null;
    onRefresh();
  };

  // `Atualizar` só com a primeira página respondida: no carregamento não há o
  // que atualizar, e a falha já tem "Tentar novamente" (D4).
  const action = (
    <Group gap="sm" wrap="nowrap">
      {/* Cor padrão, e não `dimmed`: sobre a faixa do cabeçalho (gray-3), o
          `dimmed` mede 4,14:1 no claro, abaixo do mínimo de 4,5:1 para texto
          pequeno (conferência visual, R2-1). */}
      <Text size="xs">Mais recentes primeiro</Text>
      {events !== undefined && (
        <Button size="xs" variant="default" onClick={handleRefresh} loading={isRefreshing}>
          Atualizar
        </Button>
      )}
    </Group>
  );

  let body;
  if (events === undefined) {
    body = firstPageFailed ? (
      <SectionedCard.Body>
        <Alert color="red" title="Não foi possível carregar o histórico desta base.">
          <Button size="xs" variant="outline" color="red" onClick={onRetry}>
            Tentar novamente
          </Button>
        </Alert>
      </SectionedCard.Body>
    ) : (
      <SectionedCard.Body>
        <Group gap="xs">
          <Loader size="sm" />
          <Text size="sm">Carregando histórico…</Text>
        </Group>
      </SectionedCard.Body>
    );
  } else if (events.length === 0) {
    body = (
      <SectionedCard.Body>
        {refreshFailed && (
          <Alert color="red" mb="sm">
            Não foi possível atualizar o histórico.
          </Alert>
        )}
        <Text size="sm" c="dimmed" data-testid="history-empty">
          Nenhuma mudança foi registrada nesta base.
        </Text>
      </SectionedCard.Body>
    );
  } else {
    const groups = groupEventsByDay(events);
    let index = 0;
    body = (
      <Box ref={listRef}>
        {refreshFailed && (
          <Box px="md" py="sm">
            <Alert color="red">Não foi possível atualizar o histórico.</Alert>
          </Box>
        )}
        {groups.map((group) => {
          const headingId = `history-day-${group.day.replaceAll('/', '-')}`;
          return (
            <Box component="section" key={group.day} aria-labelledby={headingId}>
              {/* SEM FUNDO (correção C9, conferência visual R1-2). A prancha pinta
                  a faixa do dia num tom entre o do cabeçalho e o do card; com o
                  tom do cabeçalho (`--buteco-surface-subtle`), o primeiro dia
                  grudava na faixa "Mudanças nos documentos". Nenhuma variável do
                  tema fica entre os dois nos dois esquemas
                  (`--mantine-color-default-hover` é gray-0 no claro, que some
                  contra o branco), e criar uma para este uso só seria variável
                  sem segundo consumidor (convenções 2 e 16). */}
              <Box
                px="md"
                pt="sm"
                pb={6}
                style={{ borderTop: '1px solid var(--mantine-color-default-border)' }}
              >
                {/* Título de verdade (h3), com o estilo de rótulo da prancha:
                    quem usa leitor de tela salta de dia em dia (D8). */}
                <Title
                  order={3}
                  id={headingId}
                  fz="xs"
                  fw={600}
                  c="dimmed"
                  tt="uppercase"
                  style={{ letterSpacing: '0.05em' }}
                >
                  {group.day}
                </Title>
              </Box>
              <Box component="ul" m={0} p={0} style={{ listStyle: 'none' }}>
                {group.events.map((event) => (
                  <EventRow key={event.id} event={event} index={index++} />
                ))}
              </Box>
            </Box>
          );
        })}
        <Box px="md" py="sm" style={{ borderTop: '1px solid var(--mantine-color-default-border)' }}>
          {nextPageFailed ? (
            // A falha da página seguinte NÃO diz que acabou: seria afirmar o fim
            // de uma lista cuja continuação não foi lida (D4).
            <Alert color="red" title="Não foi possível carregar mais eventos.">
              <Button size="xs" variant="outline" color="red" onClick={onRetryNextPage}>
                Tentar novamente
              </Button>
            </Alert>
          ) : hasNextPage ? (
            <Group justify="center">
              <Button
                size="xs"
                variant="default"
                onClick={handleLoadMore}
                loading={isFetchingNextPage}
              >
                Carregar mais
              </Button>
            </Group>
          ) : (
            <Text size="xs" c="dimmed" ta="center" data-testid="history-end">
              Não há eventos mais antigos.
            </Text>
          )}
        </Box>
      </Box>
    );
  }

  return (
    <Stack gap="sm">
      <SectionedCard title="Mudanças nos documentos" action={action}>
        {body}
      </SectionedCard>
      <HistoryNotes synced={synced} />
    </Stack>
  );
}
