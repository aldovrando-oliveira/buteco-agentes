import { Anchor, Box, Group, Loader, Text } from '@mantine/core';
import { Link } from 'react-router';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { agentsConsultingBase } from '../utils/agentUsage';
import type { Agent } from '../../agents/types/agent';

// Os três valores de `status` de uma consulta do TanStack Query, declarados aqui
// e não importados da biblioteca: o componente apresentacional não depende da
// camada de consulta (convenção 7).
type AgentsQueryStatus = 'pending' | 'error' | 'success';

interface KnowledgeBaseAgentsCardProps {
  knowledgeBaseId: string;
  // Catálogo de agentes buscado pela página e repassado como propriedade
  // (convenção 7). Indefinido enquanto a consulta não tem dado.
  agents?: Agent[];
  // O estado da consulta vem EXPLÍCITO, ao lado do dado. Antes o card recebia só
  // `agents` e tratava `undefined` como falha — e `undefined` também é o que
  // chega durante o carregamento, então o card afirmava "não foi possível
  // carregar" antes de GET /agents responder (convenção 13, design.md D5).
  status: AgentsQueryStatus;
}

const TITLE = 'Agentes que consultam esta base';

// ALTURA MÍNIMA DA LINHA DE UM CHIP em todo estado do card. Medido na
// conferência: carregando saía com 88px, uma linha de chips com 94px e vazio ou
// indisponível com 83px, então a barra de abas e tudo abaixo dela PULAVAM quando
// GET /agents respondia. Com o card acima da barra, o salto passou a mover a
// tela inteira. É o mesmo defeito, e a mesma correção, da altura fixa da barra
// de abas e da faixa do SectionedCard.
const STATE_MIN_HEIGHT = 28;
const CENTERED = { display: 'flex', alignItems: 'center' } as const;

export function KnowledgeBaseAgentsCard({
  knowledgeBaseId,
  agents,
  status,
}: KnowledgeBaseAgentsCardProps) {
  // Cada ramo decide por um fato, nenhum por exclusão. O dado na mão vem
  // primeiro: se uma revalidação em segundo plano falhar depois de uma
  // resposta, o TanStack Query mantém `data` e passa `status` a `error`, e a
  // lista lida continua sendo uma medição que aconteceu (design.md, D5).
  if (agents) {
    return <ConsultingAgents knowledgeBaseId={knowledgeBaseId} agents={agents} />;
  }

  if (status === 'pending') {
    // Mesmo padrão de carregamento de card de KnowledgeDocumentsCard.
    return (
      <SectionedCard title={TITLE} data-testid="knowledge-base-agents-card">
        <SectionedCard.Body>
          <Group gap="xs" mih={STATE_MIN_HEIGHT} data-testid="agents-loading">
            <Loader size="sm" />
            <Text size="sm">Carregando agentes...</Text>
          </Group>
        </SectionedCard.Body>
      </SectionedCard>
    );
  }

  // Falha não vira "nenhum agente consulta": uma requisição que não respondeu
  // não é evidência de ausência de vínculo; o resto do detalhe continua servindo.
  // McpServerAgentsCard ainda confunde carregamento com falha (#112).
  return (
    <SectionedCard title={TITLE} data-testid="knowledge-base-agents-card">
      <SectionedCard.Body>
        <Text
          size="sm"
          c="dimmed"
          mih={STATE_MIN_HEIGHT}
          style={CENTERED}
          data-testid="agents-unavailable"
        >
          Não foi possível carregar os agentes, então não dá para dizer quem consulta esta base.
        </Text>
      </SectionedCard.Body>
    </SectionedCard>
  );
}

function ConsultingAgents({
  knowledgeBaseId,
  agents,
}: {
  knowledgeBaseId: string;
  agents: Agent[];
}) {
  const consulting = agentsConsultingBase(agents, knowledgeBaseId);

  // A contagem conta os chips, inativos incluídos: o número encabeça o que está
  // embaixo dele. E só existe com a lista respondida e não vazia — o zero medido
  // não vira "0 agentes", porque o estado vazio já diz o fato (design.md, D3).
  const count =
    consulting.length > 0 ? (
      <Text size="xs" c="dimmed" data-testid="agents-count">
        {consulting.length} {consulting.length === 1 ? 'agente' : 'agentes'}
      </Text>
    ) : undefined;

  return (
    <SectionedCard title={TITLE} action={count} data-testid="knowledge-base-agents-card">
      <SectionedCard.Body>
        {consulting.length === 0 ? (
          // A tela de vínculo passou a existir (change
          // frontend-agente-aba-conhecimento), então a copy deixa de anunciar
          // etapa futura e diz onde vincular. O protótipo mandava "Vincule-a na
          // Visão geral de um agente", e isso continua errado: a revisão 2 do
          // próprio handoff moveu o vínculo da Visão geral para uma aba
          // própria. Sem link para um agente específico — não há qual escolher
          // a partir daqui.
          //
          // Alinhada à esquerda e sem padding extra, como na prancha 3a: com o
          // card acima da barra, a versão centralizada com `py="lg"` (123px de
          // altura, medido na conferência) empurrava as abas para baixo sem
          // carregar informação nenhuma.
          <Text
            size="sm"
            c="dimmed"
            mih={STATE_MIN_HEIGHT}
            style={CENTERED}
            data-testid="agents-empty"
          >
            Nenhum agente consulta esta base. O vínculo é feito na aba Conhecimento do detalhe do
            agente.
          </Text>
        ) : (
          // Chips que quebram na horizontal: muitos agentes viram mais linhas de
          // chips, não um card alto (issue #99, prancha 4a). Lista de verdade,
          // para o leitor de tela anunciar quantos são (design.md, D4).
          <Group component="ul" gap={8} m={0} p={0} style={{ listStyle: 'none' }}>
            {consulting.map((agent) => (
              <AgentChip key={agent.id} agent={agent} />
            ))}
          </Group>
        )}
      </SectionedCard.Body>
    </SectionedCard>
  );
}

// CORREÇÃO DE PROTÓTIPO (design.md, D2): o chip do protótipo só tem o nome, mas
// agente inativo não consulta a base, e um chip igual aos outros afirmaria uma
// consulta que não acontece. Só o inativo leva marca — "Ativo" em todo chip
// repetiria o caso normal — e a marca é texto, não só cor. Ela fica FORA do link,
// para o nome acessível continuar sendo o nome do agente.
//
// Local ao card, sem componente compartilhado: não existe outro chip no painel
// (design.md, D4).
//
// Fundo no tom da página, e não o `--mantine-color-default` da primeira versão:
// medido na conferência, aquele é BRANCO no claro, igual ao card, e o chip
// virava só contorno, onde a prancha 4a o destaca do card. `--buteco-page-bg` já
// troca por esquema — no escuro fica mais escuro que o card, que é o sentido
// certo da superfície sutil nesse esquema. Texto na tinta do card, e não na cor
// de link: o chip da prancha é neutro, e o formato de pílula já diz que é
// clicável (design.md, D4).
function AgentChip({ agent }: { agent: Agent }) {
  return (
    <Box
      component="li"
      data-agent-chip
      h={28}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        borderRadius: 14,
        border: '1px solid var(--mantine-color-default-border)',
        background: 'var(--buteco-page-bg)',
      }}
    >
      {/* O link ocupa a pílula na altura toda e leva o padding: no chip ativo o
          chip inteiro navega, como na prancha 4a. A primeira versão tinha o
          padding no `li`, e só o texto (101x19 num chip de 168x28) era
          clicável — medido na conferência. */}
      <Anchor
        component={Link}
        to={`/agents/${agent.id}`}
        size="md"
        c="inherit"
        h="100%"
        pl={12}
        pr={agent.isActive ? 12 : 6}
        style={{ display: 'inline-flex', alignItems: 'center', borderRadius: 14 }}
      >
        {agent.name}
      </Anchor>
      {!agent.isActive && (
        <Text component="span" size="xs" c="dimmed" pr={12}>
          Inativo
        </Text>
      )}
    </Box>
  );
}
