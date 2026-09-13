import { useState } from 'react';
import {
  Alert,
  Button,
  Group,
  Loader,
  Paper,
  SegmentedControl,
  Stack,
  Text,
  TextInput,
  Title,
} from '@mantine/core';
import { Link } from 'react-router';
import {
  useKnowledgeBaseIndexingSummaryQuery,
  useKnowledgeBasesQuery,
} from '../api/useKnowledgeBases';
import { useAgentsQuery } from '../../agents/api/useAgents';
import { KnowledgeBaseTable } from '../components/KnowledgeBaseTable';
import { matchesSearch } from '../../../utils/searchText';
import {
  STATUS_ACTIVE,
  STATUS_ALL,
  STATUS_FAILED,
  STATUS_INACTIVE,
  effectiveStatusFilter,
  hasFailure,
  summaryById,
  summaryFor,
  type StatusFilter,
} from '../utils/indexingSummary';
import type { KnowledgeBase, KnowledgeBaseIndexingSummary } from '../types/knowledgeBase';

// QUATRO opções, e a quarta nasceu agora. Até a etapa 5a-1 eram três, e a razão
// escrita aqui era que `Com falha` filtra por estado de indexação agregado por
// base — dado que a API não devolvia, e oferecer o filtro inerte seria pior que
// não oferecê-lo (D9 da 5a-1).
//
// A REGRA NÃO MUDOU; O DADO MUDOU. `GET /knowledge-bases/indexing-summary` passou
// a devolver `failedCount` por base numa requisição para o conjunto, e com ele a
// opção tem o que filtrar.
//
// O que sobrou da regra antiga está em `effectiveStatusFilter`: sem resumo, a
// opção fica desabilitada no controle E o filtro cai para `todas`, porque
// esvaziar a listagem afirmaria que nenhuma base tem falha.
function matchesStatus(
  knowledgeBase: KnowledgeBase,
  filter: StatusFilter,
  summary: Map<string, KnowledgeBaseIndexingSummary> | undefined,
): boolean {
  switch (filter) {
    case STATUS_ACTIVE:
      return knowledgeBase.isActive;
    case STATUS_INACTIVE:
      return !knowledgeBase.isActive;
    case STATUS_FAILED:
      // Inclui base inativa com falha: desativar impede o uso pelo agente, não a
      // manutenção do conteúdo, e é justamente a linha que pede atenção.
      return hasFailure(summaryFor(summary, knowledgeBase.id));
    default:
      return true;
  }
}

export function KnowledgeBaseListPage() {
  const { data, isLoading, isError } = useKnowledgeBasesQuery();
  // Só para derivar a coluna "Consultada por". Uma falha aqui não impede a
  // listagem: a coluna fica sem afirmar uso e o resto continua servindo, mesmo
  // tratamento de McpServerListPage (design.md, D21).
  const agentsQuery = useAgentsQuery();
  // Segunda requisição, em PARALELO com a do catálogo — sem encadeamento e sem
  // `enabled` cruzado. A rota do resumo não depende de parâmetro nenhum do
  // catálogo, então encadear custaria uma viagem em série e não ganharia nada
  // (design.md, D2).
  //
  // A listagem é governada só pela consulta de bases: enquanto o resumo não
  // chega, ou se ele falhar, as linhas aparecem e as duas colunas dizem "não sei".
  const summaryQuery = useKnowledgeBaseIndexingSummaryQuery();
  // Estado de componente, não parâmetro de rota, pelo mesmo motivo das outras
  // listas: um termo em digitação encheria o histórico a cada tecla.
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<StatusFilter>(STATUS_ALL);

  const summaryAvailable = summaryQuery.data !== undefined;
  const summary = summaryById(summaryQuery.data);
  // O filtro EFETIVO, e não o selecionado. Cobre o caso que o `disabled` sozinho
  // não cobre: o operador seleciona `Com falha`, o resumo é invalidado, e a nova
  // consulta falha.
  const effectiveStatus = effectiveStatusFilter(status, summaryAvailable);

  const knowledgeBases = data ?? [];
  const visible = knowledgeBases.filter(
    (knowledgeBase) =>
      matchesSearch(search, knowledgeBase.name, knowledgeBase.description) &&
      matchesStatus(knowledgeBase, effectiveStatus, summary),
  );

  const hasKnowledgeBases = knowledgeBases.length > 0;
  const showEmptyCatalog = !isLoading && !isError && !hasKnowledgeBases;
  const showNoMatches = !isLoading && !isError && hasKnowledgeBases && visible.length === 0;

  return (
    <Stack>
      <Group justify="space-between" align="flex-start">
        <Stack gap={2}>
          <Title order={2}>Bases de conhecimento</Title>
          {hasKnowledgeBases && (
            <Text size="sm" c="dimmed">
              {knowledgeBases.length} {knowledgeBases.length === 1 ? 'base' : 'bases'} · o agente
              consulta sob demanda, como ferramenta
            </Text>
          )}
        </Stack>
        <Button component={Link} to="/knowledge-bases/new">
          Nova base
        </Button>
      </Group>

      {hasKnowledgeBases && (
        <Group gap="sm" wrap="nowrap">
          <TextInput
            aria-label="Buscar por nome ou descrição"
            placeholder="Buscar por nome ou descrição"
            value={search}
            onChange={(event) => setSearch(event.currentTarget.value)}
            maw={340}
            style={{ flex: 1 }}
          />
          <SegmentedControl
            size="xs"
            withItemsBorders={false}
            style={{ border: '1px solid var(--mantine-color-default-border)', flexShrink: 0 }}
            value={status}
            onChange={(value) => setStatus(value as StatusFilter)}
            data={[
              { value: STATUS_ALL, label: 'Todas' },
              { value: STATUS_ACTIVE, label: 'Ativas' },
              { value: STATUS_INACTIVE, label: 'Inativas' },
              // Desabilitada, e não ausente nem inerte. Some do controle
              // afirmaria que o filtro não existe; presente e sem efeito
              // afirmaria que nenhuma base tem falha — que é o "vazio ou inerte"
              // já recusado pela 5a-1 (design.md, D7).
              { value: STATUS_FAILED, label: 'Com falha', disabled: !summaryAvailable },
            ]}
          />
        </Group>
      )}

      {isLoading && (
        <Group>
          <Loader size="sm" />
          <Text>Carregando bases de conhecimento...</Text>
        </Group>
      )}

      {isError && <Alert color="red">Não foi possível carregar as bases de conhecimento.</Alert>}

      {/* Falha do resumo não derruba a listagem: as bases continuam servindo e as
          duas colunas dizem que não sabem. Dizer por quê é o que impede o
          travessão de ser lido como "zero" (design.md, D2). */}
      {!isLoading && !isError && hasKnowledgeBases && summaryQuery.isError && (
        <Alert color="yellow" data-testid="resumo-indisponivel">
          Não foi possível carregar o resumo de indexação. As colunas Documentos e Indexação ficam
          sem informação, e o filtro Com falha fica indisponível.
        </Alert>
      )}

      {/* Catálogo vazio explica o que é uma base antes de pedir a primeira: quem
          chega aqui pela navegação pode não saber para que serve. Bloco
          tracejado, distinto da linha simples do vazio de busca — são problemas
          diferentes, com saídas diferentes. */}
      {showEmptyCatalog && (
        <Paper
          withBorder
          radius="md"
          p="xl"
          style={{ borderStyle: 'dashed' }}
          data-testid="empty-catalog"
        >
          <Stack align="center" gap="xs">
            <Text fw={600}>Nenhuma base de conhecimento cadastrada ainda.</Text>
            <Text size="sm" c="dimmed" ta="center" maw={520}>
              Uma base guarda documentos markdown que o agente consulta durante a conversa, quando
              julga que o assunto está ali.
            </Text>
            <Button component={Link} to="/knowledge-bases/new" mt="xs">
              Criar a primeira base
            </Button>
          </Stack>
        </Paper>
      )}

      {showNoMatches && <Text c="dimmed">Nenhuma base corresponde à busca.</Text>}

      {visible.length > 0 && (
        <KnowledgeBaseTable
          knowledgeBases={visible}
          agents={agentsQuery.data}
          indexingSummary={summaryQuery.data}
        />
      )}
    </Stack>
  );
}
