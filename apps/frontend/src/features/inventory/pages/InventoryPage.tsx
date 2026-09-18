import { Anchor, Box, Button, Group, Paper, Skeleton, Stack, Text, Title } from '@mantine/core';
import { ArrowRight, BookOpen, Bot, MessagesSquare, Server } from 'lucide-react';
import { Link } from 'react-router';
import { SectionLabel } from '../../../components/data/SectionLabel';
import { useAgentsQuery } from '../../agents/api/useAgents';
import { useMcpServersQuery } from '../../mcp-servers/api/useMcpServers';
import { useKnowledgeBasesQuery } from '../../knowledge-bases/api/useKnowledgeBases';
import { useChannelsQuery } from '../../channels/api/useChannels';

// Inventário dos catálogos. A contagem de cada um sai de `array.length` sobre a
// MESMA consulta que alimenta a listagem daquele catálogo — mesma chave de
// cache, mesmo QueryClient (design.md, D3). Nenhuma consulta nova, nenhuma rota
// nova de backend: as quatro já existem e nenhuma delas pagina, então o
// comprimento do array É a contagem, e é completa (D6).
//
// OS QUATRO ITENS ESTÃO ESCRITOS À MÃO, UM A UM, E ISSO É DELIBERADO.
// A régua de extração é repetição já observada, nunca prevista (convenção 2), e
// as quatro cópias podem não ser iguais: o de Canais fala com OUTRO processo, em
// outra porta, por outro `request<T>` (channelsApi.ts:26). Escrever os quatro é
// o que permite contar depois e decidir com o dado na mão (D4).
//
// NENHUM ITEM CARREGA FRASE EXPLICATIVA, e isso é um requisito, não um esquecimento.
// Cada listagem tem um subtítulo escrito à mão sobre o que aquele catálogo é;
// reproduzi-lo aqui criaria uma quinta superfície de texto a manter verdadeira,
// que envelheceria sem que esta tela mudasse. O número não envelhece — ele é lido
// do mesmo cache. A prosa, sim. Quem quer a explicação está a um clique dela (D3).
//
// CADA ITEM CARREGA O PRÓPRIO ESTADO DE CARREGAMENTO E O PRÓPRIO ERRO.
// Nada de `isLoading || outraQuery.isLoading` como em ChannelListPage.tsx:14: numa
// tela cujo conteúdo são quatro números, um apps/inbox lento não pode segurar três
// itens prontos atrás de um carregamento compartilhado (D6, e o risco declarado
// no design.md sobre apps/inbox no caminho de entrada).
export function InventoryPage() {
  const agentsQuery = useAgentsQuery();
  const mcpServersQuery = useMcpServersQuery();
  const knowledgeBasesQuery = useKnowledgeBasesQuery();
  const channelsQuery = useChannelsQuery();

  return (
    <Stack>
      <Title order={2}>Inventário</Title>

      {/* auto-fit com mínimo de 256px: a grade reflui sozinha, sem breakpoint
          declarado. Os itens da mesma linha saem com a mesma altura pelo
          `stretch` padrão da grade, sobre o piso de 148px — é o que impede a
          linha de ficar serrilhada quando um item ganha razão e botão de nova
          tentativa e o vizinho não (D4, D7).

          Sem largura máxima de container: nenhuma página do painel tem uma, e
          introduzir o padrão para uma tela só seria abstração sem o segundo
          consumidor que a convenção 2 exige (D7). */}
      <Box
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(min(256px, 100%), 1fr))',
          gap: 'var(--mantine-spacing-md)',
        }}
      >
        {/* ---------------------------------------------------------------- */}
        <Paper
          withBorder
          radius="md"
          p="md"
          data-testid="inventario-agents"
          style={{ display: 'flex', flexDirection: 'column', minHeight: 148 }}
        >
          <Group gap={7} wrap="nowrap">
            <Bot size={14} />
            <SectionLabel>Agentes</SectionLabel>
          </Group>

          <Box style={{ flex: 1, display: 'flex', alignItems: 'center' }} py="md">
            {agentsQuery.isLoading ? (
              <Stack gap={7}>
                <Skeleton height={24} width={96} radius="sm" />
                {/* `dimmed`, e não o tom terciário do protótipo: aquele dá
                    3,21:1 sobre a superfície, abaixo do mínimo de 4,5:1 que o
                    próprio tema exige (theme.ts:168-171) — D8, D9. */}
                <Text size="sm" c="dimmed">
                  Consultando…
                </Text>
              </Stack>
            ) : agentsQuery.isError || !agentsQuery.data ? (
              <Box>
                {/* Travessão é "não sei", nunca zero. E a razão ao lado é o que
                    impede que ele seja LIDO como zero — mesmo tratamento de
                    KnowledgeBaseAgentsCard.tsx:17-27 (D6). */}
                <Text ff="monospace" fz={28} c="dimmed" lh={1}>
                  —
                </Text>
                <Text size="sm" c="dimmed" mt={7}>
                  Não foi possível consultar os agentes.
                </Text>
                <Button
                  variant="default"
                  size="xs"
                  mt={9}
                  onClick={() => void agentsQuery.refetch()}
                >
                  Tentar novamente
                </Button>
              </Box>
            ) : agentsQuery.data.length === 0 ? (
              // Zero MEDIDO: a consulta respondeu e não há registro. É verdade, e
              // por isso pode ser afirmado em palavras — o oposto do travessão
              // acima (D6, convenção 13).
              <Text fw={600} fz={18}>
                Nenhum agente
              </Text>
            ) : (
              <Group gap={7} align="baseline" wrap="nowrap">
                <Text
                  ff="monospace"
                  fz={28}
                  fw={600}
                  lh={1}
                  data-testid="inventario-agents-contagem"
                >
                  {agentsQuery.data.length}
                </Text>
                <Text c="dimmed">{agentsQuery.data.length === 1 ? 'agente' : 'agentes'}</Text>
              </Group>
            )}
          </Box>

          <Anchor
            component={Link}
            to="/agents"
            size="sm"
            fw={600}
            pt="sm"
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 5,
              borderTop: '1px solid var(--mantine-color-default-border)',
            }}
          >
            Ver agentes
            <ArrowRight size={13} />
          </Anchor>
        </Paper>

        {/* ---------------------------------------------------------------- */}
        <Paper
          withBorder
          radius="md"
          p="md"
          data-testid="inventario-mcp-servers"
          style={{ display: 'flex', flexDirection: 'column', minHeight: 148 }}
        >
          <Group gap={7} wrap="nowrap">
            <Server size={14} />
            <SectionLabel>Servidores MCP</SectionLabel>
          </Group>

          <Box style={{ flex: 1, display: 'flex', alignItems: 'center' }} py="md">
            {mcpServersQuery.isLoading ? (
              <Stack gap={7}>
                <Skeleton height={24} width={96} radius="sm" />
                <Text size="sm" c="dimmed">
                  Consultando…
                </Text>
              </Stack>
            ) : mcpServersQuery.isError || !mcpServersQuery.data ? (
              <Box>
                <Text ff="monospace" fz={28} c="dimmed" lh={1}>
                  —
                </Text>
                <Text size="sm" c="dimmed" mt={7}>
                  Não foi possível consultar os servidores MCP.
                </Text>
                <Button
                  variant="default"
                  size="xs"
                  mt={9}
                  onClick={() => void mcpServersQuery.refetch()}
                >
                  Tentar novamente
                </Button>
              </Box>
            ) : mcpServersQuery.data.length === 0 ? (
              <Text fw={600} fz={18}>
                Nenhum servidor MCP
              </Text>
            ) : (
              <Group gap={7} align="baseline" wrap="nowrap">
                <Text
                  ff="monospace"
                  fz={28}
                  fw={600}
                  lh={1}
                  data-testid="inventario-mcp-servers-contagem"
                >
                  {mcpServersQuery.data.length}
                </Text>
                <Text c="dimmed">
                  {mcpServersQuery.data.length === 1 ? 'servidor' : 'servidores'}
                </Text>
              </Group>
            )}
          </Box>

          <Anchor
            component={Link}
            to="/mcp-servers"
            size="sm"
            fw={600}
            pt="sm"
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 5,
              borderTop: '1px solid var(--mantine-color-default-border)',
            }}
          >
            Ver servidores MCP
            <ArrowRight size={13} />
          </Anchor>
        </Paper>

        {/* ---------------------------------------------------------------- */}
        <Paper
          withBorder
          radius="md"
          p="md"
          data-testid="inventario-knowledge-bases"
          style={{ display: 'flex', flexDirection: 'column', minHeight: 148 }}
        >
          <Group gap={7} wrap="nowrap">
            <BookOpen size={14} />
            <SectionLabel>Bases de conhecimento</SectionLabel>
          </Group>

          <Box style={{ flex: 1, display: 'flex', alignItems: 'center' }} py="md">
            {knowledgeBasesQuery.isLoading ? (
              <Stack gap={7}>
                <Skeleton height={24} width={96} radius="sm" />
                <Text size="sm" c="dimmed">
                  Consultando…
                </Text>
              </Stack>
            ) : knowledgeBasesQuery.isError || !knowledgeBasesQuery.data ? (
              <Box>
                <Text ff="monospace" fz={28} c="dimmed" lh={1}>
                  —
                </Text>
                <Text size="sm" c="dimmed" mt={7}>
                  Não foi possível consultar as bases de conhecimento.
                </Text>
                <Button
                  variant="default"
                  size="xs"
                  mt={9}
                  onClick={() => void knowledgeBasesQuery.refetch()}
                >
                  Tentar novamente
                </Button>
              </Box>
            ) : knowledgeBasesQuery.data.length === 0 ? (
              <Text fw={600} fz={18}>
                Nenhuma base de conhecimento
              </Text>
            ) : (
              <Group gap={7} align="baseline" wrap="nowrap">
                <Text
                  ff="monospace"
                  fz={28}
                  fw={600}
                  lh={1}
                  data-testid="inventario-knowledge-bases-contagem"
                >
                  {knowledgeBasesQuery.data.length}
                </Text>
                <Text c="dimmed">{knowledgeBasesQuery.data.length === 1 ? 'base' : 'bases'}</Text>
              </Group>
            )}
          </Box>

          <Anchor
            component={Link}
            to="/knowledge-bases"
            size="sm"
            fw={600}
            pt="sm"
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 5,
              borderTop: '1px solid var(--mantine-color-default-border)',
            }}
          >
            Ver bases
            <ArrowRight size={13} />
          </Anchor>
        </Paper>

        {/* ----------------------------------------------------------------
            O item de Canais é o que impede a extração precoce dos quatro: ele é
            o único que fala com apps/inbox, em outra porta, por outro
            `request<T>` — e por isso falha de forma independente dos outros três
            (design.md, D4 e o risco declarado). */}
        <Paper
          withBorder
          radius="md"
          p="md"
          data-testid="inventario-channels"
          style={{ display: 'flex', flexDirection: 'column', minHeight: 148 }}
        >
          <Group gap={7} wrap="nowrap">
            <MessagesSquare size={14} />
            <SectionLabel>Canais</SectionLabel>
          </Group>

          <Box style={{ flex: 1, display: 'flex', alignItems: 'center' }} py="md">
            {channelsQuery.isLoading ? (
              <Stack gap={7}>
                <Skeleton height={24} width={96} radius="sm" />
                <Text size="sm" c="dimmed">
                  Consultando…
                </Text>
              </Stack>
            ) : channelsQuery.isError || !channelsQuery.data ? (
              <Box>
                <Text ff="monospace" fz={28} c="dimmed" lh={1}>
                  —
                </Text>
                <Text size="sm" c="dimmed" mt={7}>
                  Não foi possível consultar os canais.
                </Text>
                <Button
                  variant="default"
                  size="xs"
                  mt={9}
                  onClick={() => void channelsQuery.refetch()}
                >
                  Tentar novamente
                </Button>
              </Box>
            ) : channelsQuery.data.length === 0 ? (
              <Text fw={600} fz={18}>
                Nenhum canal
              </Text>
            ) : (
              <Group gap={7} align="baseline" wrap="nowrap">
                <Text
                  ff="monospace"
                  fz={28}
                  fw={600}
                  lh={1}
                  data-testid="inventario-channels-contagem"
                >
                  {channelsQuery.data.length}
                </Text>
                <Text c="dimmed">{channelsQuery.data.length === 1 ? 'canal' : 'canais'}</Text>
              </Group>
            )}
          </Box>

          <Anchor
            component={Link}
            to="/channels"
            size="sm"
            fw={600}
            pt="sm"
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 5,
              borderTop: '1px solid var(--mantine-color-default-border)',
            }}
          >
            Ver canais
            <ArrowRight size={13} />
          </Anchor>
        </Paper>
      </Box>
    </Stack>
  );
}
