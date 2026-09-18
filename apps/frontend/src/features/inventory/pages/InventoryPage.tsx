import { Anchor, Box, Button, Group, Paper, Skeleton, Stack, Text, Title } from '@mantine/core';
import {
  ArrowRight,
  BookOpen,
  Bot,
  CirclePlay,
  MessageSquare,
  MessagesSquare,
  Server,
} from 'lucide-react';
import { Link } from 'react-router';
import { SectionLabel } from '../../../components/data/SectionLabel';
import { useAgentsQuery } from '../../agents/api/useAgents';
import { useMcpServersQuery } from '../../mcp-servers/api/useMcpServers';
import { useKnowledgeBasesQuery } from '../../knowledge-bases/api/useKnowledgeBases';
import { useChannelsQuery } from '../../channels/api/useChannels';
import { useMessagesSummaryQuery, useSessionsSummaryQuery } from '../../sessions/api/useSessions';

// Inventário: quatro itens de CATÁLOGO e dois de ATIVIDADE, cada contagem com UMA
// apuração só (design.md de frontend-inventario-atividade-periodo, D2):
//
//   catálogo  — `array.length` sobre a MESMA consulta que alimenta a listagem
//               daquele catálogo, mesma chave de cache, mesmo QueryClient. Nenhuma
//               das quatro rotas pagina, então o comprimento do array É a
//               contagem, e é completa.
//   atividade — o número que a rota AGREGADA de apps/inbox devolve para os
//               últimos 7 dias (`startedCount`, `inboundCount`). NUNCA recontado
//               aqui: somar as sessões listadas por canal parece barato e conta
//               OUTRO conjunto. A janela nasce dentro do queryFn, com chave fixa
//               — posta na chave, cada render geraria consulta nova e o item
//               ficaria em "Consultando…" para sempre (D3).
//
// OS SEIS ITENS ESTÃO ESCRITOS À MÃO, UM A UM, E ISSO É DELIBERADO.
// A régua de extração é repetição já observada, nunca prevista (convenção 2), e
// os seis NÃO são iguais: os de catálogo têm atalho e os de atividade não (não
// existe listagem que conte o mesmo conjunto); os de atividade têm janela; e três
// falam com OUTRO processo, em outra porta, por outro `request<T>`. Extrair agora
// esconderia exatamente essas diferenças (D5).
//
// NENHUM ITEM CARREGA FRASE EXPLICATIVA, e isso é um requisito, não um esquecimento.
// Cada listagem tem um subtítulo escrito à mão sobre o que aquele catálogo é;
// reproduzi-lo aqui criaria outra superfície de texto a manter verdadeira, que
// envelheceria sem que esta tela mudasse. O item de atividade não tem listagem,
// e o que o distingue de uma contagem parecida é o nome e a janela — nada mais.
//
// CADA ITEM CARREGA O PRÓPRIO ESTADO DE CARREGAMENTO E O PRÓPRIO ERRO.
// Nada de `isLoading || outraQuery.isLoading` como em ChannelListPage.tsx:14: numa
// tela cujo conteúdo são seis números, um apps/inbox lento não pode segurar os
// itens prontos atrás de um carregamento compartilhado — e são três consultas a
// ele agora, não uma (risco declarado no design.md).
export function InventoryPage() {
  const agentsQuery = useAgentsQuery();
  const mcpServersQuery = useMcpServersQuery();
  const knowledgeBasesQuery = useKnowledgeBasesQuery();
  const channelsQuery = useChannelsQuery();
  const sessionsSummaryQuery = useSessionsSummaryQuery();
  const messagesSummaryQuery = useMessagesSummaryQuery();

  return (
    <Stack>
      <Title order={2}>Inventário</Title>

      {/* auto-fit com mínimo de 256px e TETO DE QUATRO COLUNAS (design.md de
          frontend-inventario-atividade-periodo, D6).

          O teto é o `max(…, (100% − 3·gap)/4)`: nenhuma faixa pode ser mais
          estreita que um quarto da grade, então não cabem cinco. Sem ele, com
          seis itens, o auto-fit dava 5 + 1 na faixa de 1600–1871px de janela —
          que inclui a largura de trabalho real (~1860px): os quatro catálogos e
          um item de atividade na primeira linha, o outro sozinho na segunda.
          Com o teto: 4 + 2 a 1860px, itens de 389px.

          A REGRA VALE PARA OS SEIS ITENS, inclusive os quatro de catálogo: é uma
          grade só, e é ela que decide onde os quatro ficam.

          Pontos de reflow, medidos no motor com o cromo do app (224 + 2 × 16):
          4 colunas ≥ 1328px de janela, 3 ≥ 1056px, 2 ≥ 784px, 1 abaixo — os
          mesmos de antes, porque dependem só do mínimo, do gap e do cromo.

          Os itens da mesma linha saem com a mesma altura pelo `stretch` padrão
          da grade, sobre o piso de 148px — é o que impede a linha de ficar
          serrilhada quando um item ganha razão e botão de nova tentativa e o
          vizinho não.

          Sem largura máxima de container: nenhuma página do painel tem uma (D7
          de frontend-inventario-catalogos). O protótipo tem 1584px, e é
          divergência registrada (D8).

          NENHUM TESTE DE SUÍTE COBRE ESTA GRADE, e isso é deliberado: jsdom não
          calcula layout (convenção 14), e uma asserção sobre esta string só
          fixaria o texto do CSS. A verificação é a conferência visual manual. */}
      <Box
        style={{
          display: 'grid',
          gridTemplateColumns:
            'repeat(auto-fit, minmax(max(min(256px, 100%), calc((100% - 3 * var(--mantine-spacing-md)) / 4)), 1fr))',
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

        {/* ----------------------------------------------------------------
            ITENS DE ATIVIDADE. Diferem dos quatro acima em três pontos, e os
            três são requisito (D2, D5):
              - sem atalho: nenhuma listagem do painel conta o mesmo conjunto —
                as sessões do canal não têm período, as mensagens da sessão têm
                as duas direções. Um link levaria a um número diferente;
              - a janela fica FORA do ternário de estado, e por isso aparece nos
                quatro estados por construção: ela é da pergunta, não da
                resposta. Um "—" sem janela seria um "não sei" sem objeto;
              - `justifyContent: 'center'` no card: sem rodapé, rótulo, número e
                janela se centralizam JUNTOS no card esticado pela linha, em vez
                de ficar presos no topo com espaço morto embaixo. */}
        <Paper
          withBorder
          radius="md"
          p="md"
          data-testid="inventario-sessions"
          style={{
            display: 'flex',
            flexDirection: 'column',
            justifyContent: 'center',
            minHeight: 148,
          }}
        >
          <Group gap={7} wrap="nowrap">
            <CirclePlay size={14} />
            <SectionLabel>Sessões iniciadas</SectionLabel>
          </Group>

          <Box py="md">
            {sessionsSummaryQuery.isLoading ? (
              <Stack gap={7}>
                <Skeleton height={24} width={96} radius="sm" />
                <Text size="sm" c="dimmed">
                  Consultando…
                </Text>
              </Stack>
            ) : sessionsSummaryQuery.isError || !sessionsSummaryQuery.data ? (
              <Box>
                <Text ff="monospace" fz={28} c="dimmed" lh={1}>
                  —
                </Text>
                <Text size="sm" c="dimmed" mt={7}>
                  Não foi possível consultar as sessões iniciadas.
                </Text>
                <Button
                  variant="default"
                  size="xs"
                  mt={9}
                  onClick={() => void sessionsSummaryQuery.refetch()}
                >
                  Tentar novamente
                </Button>
              </Box>
            ) : sessionsSummaryQuery.data.startedCount === 0 ? (
              // Zero MEDIDO no período: a agregação percorreu as sessões e não
              // achou nenhuma iniciada na janela. Dito por extenso, nunca "0".
              <Text fw={600} fz={18}>
                Nenhuma sessão iniciada
              </Text>
            ) : (
              <Group gap={7} align="baseline" wrap="nowrap">
                <Text
                  ff="monospace"
                  fz={28}
                  fw={600}
                  lh={1}
                  data-testid="inventario-sessions-contagem"
                >
                  {sessionsSummaryQuery.data.startedCount}
                </Text>
                <Text c="dimmed">
                  {sessionsSummaryQuery.data.startedCount === 1 ? 'sessão' : 'sessões'}
                </Text>
              </Group>
            )}
            <Text size="xs" fw={500} c="dimmed" mt={6} data-testid="inventario-sessions-janela">
              últimos 7 dias
            </Text>
          </Box>
        </Paper>

        {/* ---------------------------------------------------------------- */}
        <Paper
          withBorder
          radius="md"
          p="md"
          data-testid="inventario-messages"
          style={{
            display: 'flex',
            flexDirection: 'column',
            justifyContent: 'center',
            minHeight: 148,
          }}
        >
          <Group gap={7} wrap="nowrap">
            <MessageSquare size={14} />
            <SectionLabel>Mensagens recebidas</SectionLabel>
          </Group>

          <Box py="md">
            {messagesSummaryQuery.isLoading ? (
              <Stack gap={7}>
                <Skeleton height={24} width={96} radius="sm" />
                <Text size="sm" c="dimmed">
                  Consultando…
                </Text>
              </Stack>
            ) : messagesSummaryQuery.isError || !messagesSummaryQuery.data ? (
              <Box>
                <Text ff="monospace" fz={28} c="dimmed" lh={1}>
                  —
                </Text>
                <Text size="sm" c="dimmed" mt={7}>
                  Não foi possível consultar as mensagens recebidas.
                </Text>
                <Button
                  variant="default"
                  size="xs"
                  mt={9}
                  onClick={() => void messagesSummaryQuery.refetch()}
                >
                  Tentar novamente
                </Button>
              </Box>
            ) : messagesSummaryQuery.data.inboundCount === 0 ? (
              <Text fw={600} fz={18}>
                Nenhuma mensagem recebida
              </Text>
            ) : (
              <Group gap={7} align="baseline" wrap="nowrap">
                <Text
                  ff="monospace"
                  fz={28}
                  fw={600}
                  lh={1}
                  data-testid="inventario-messages-contagem"
                >
                  {messagesSummaryQuery.data.inboundCount}
                </Text>
                <Text c="dimmed">
                  {messagesSummaryQuery.data.inboundCount === 1 ? 'mensagem' : 'mensagens'}
                </Text>
              </Group>
            )}
            <Text size="xs" fw={500} c="dimmed" mt={6} data-testid="inventario-messages-janela">
              últimos 7 dias
            </Text>
          </Box>
        </Paper>
      </Box>
    </Stack>
  );
}
