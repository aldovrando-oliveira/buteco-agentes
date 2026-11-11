import { Alert, Anchor, Badge, Button, Group, Stack, Text } from '@mantine/core';
import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { ActionDenied } from '../../../components/feedback/ActionDenied';
import { is403 } from '../../../utils/failureNotice';
import type { AgentApplicationsState } from '../useAgentApplications';
import type { AgentApplication, AgentApplicationsCount } from '../types';
import {
  AFETA_ANTES,
  AFETA_DEPOIS,
  AFETA_DEPOIS_UMA,
  CARD_ERRO,
  NENHUMA_ADMIN,
  NENHUMA_OUTROS,
  aplicacoesAtivas,
  operacoesPorExtenso,
  todasDesativadas,
  usadoPorAtivas,
  usoDaAplicacao,
} from '../applicationsTexts';

// O card "Aplicações com acesso" do detalhe do agente (quadro `AgenteDetalhe`, versão 66; spec
// `panel-external-application-agents`). DUAS ROTAS, uma por papel (#187, Q8): o Admin lê a lista
// com nomes; Operador e Leitor leem só `{ total, ativas }`, e NUNCA chamam a lista — a escolha é
// pelo papel da sessão, antes de qualquer chamada.

export function AgentApplicationsCard({ estado }: { estado: AgentApplicationsState }) {
  const { admin, lista, contagem, ativas } = estado;
  const consulta = admin ? lista : contagem;

  return (
    <SectionedCard
      title="Aplicações com acesso"
      action={
        admin && (
          <Anchor component={Link} to="/applications" size="xs">
            Gerenciar
          </Anchor>
        )
      }
      data-testid="agent-applications-card"
    >
      {consulta.isPending ? (
        <SectionedCard.Row>
          <Text size="sm" c="dimmed" role="status">
            Carregando…
          </Text>
        </SectionedCard.Row>
      ) : consulta.isError ? (
        <SectionedCard.Row>
          {is403(consulta.error) ? (
            <ActionDenied />
          ) : (
            <Alert color="red" role="alert" p="xs">
              <Group justify="space-between" gap="sm" wrap="wrap">
                <Text size="sm">{CARD_ERRO}</Text>
                <Button variant="default" size="compact-xs" onClick={() => void consulta.refetch()}>
                  Tentar de novo
                </Button>
              </Group>
            </Alert>
          )}
        </SectionedCard.Row>
      ) : admin ? (
        <AdminRows apps={lista.data ?? []} />
      ) : (
        <SectionedCard.Row>
          <Text size="sm">{textoDaContagem(contagem.data ?? { total: 0, ativas: 0 })}</Text>
        </SectionedCard.Row>
      )}
      {ativas !== null && ativas > 0 && (
        <SectionedCard.Row>
          <Text size="xs" c="dimmed">
            <AfetaN n={ativas} />
          </Text>
        </SectionedCard.Row>
      )}
    </SectionedCard>
  );
}

/** "Desativar este agente afeta **N aplicações ativas**: …", no rodapé do card e no modal. */
export function AfetaN({ n }: { n: number }) {
  return (
    <>
      {AFETA_ANTES}
      <b>{aplicacoesAtivas(n)}</b>
      {n === 1 ? AFETA_DEPOIS_UMA : AFETA_DEPOIS}
    </>
  );
}

function textoDaContagem({ total, ativas }: AgentApplicationsCount): ReactNode {
  if (total === 0) return NENHUMA_OUTROS;
  if (ativas === 0) return todasDesativadas(total);
  return (
    <>
      Usado por <b>{usadoPorAtivas(ativas)}</b> ({total} no total).
    </>
  );
}

function AdminRows({ apps }: { apps: AgentApplication[] }) {
  if (apps.length === 0) {
    return (
      <SectionedCard.Row>
        <Text size="sm">{NENHUMA_ADMIN}</Text>
      </SectionedCard.Row>
    );
  }
  return (
    <>
      {apps.map((app) => (
        <SectionedCard.Row key={app.id}>
          <Group justify="space-between" gap="sm" wrap="nowrap" align="flex-start">
            <Stack gap={0} style={{ minWidth: 0 }}>
              <Group gap={6}>
                <Anchor component={Link} to={`/applications/${app.id}`} size="sm" fw={500}>
                  {app.nome}
                </Anchor>
                {!app.ativa && (
                  <Badge color="gray" variant="light" size="sm">
                    Desativada
                  </Badge>
                )}
              </Group>
              <Text size="xs" c="dimmed">
                {operacoesPorExtenso(app.operacoes)}
              </Text>
            </Stack>
            <Text size="xs" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
              {usoDaAplicacao(app.ultimoUso)}
            </Text>
          </Group>
        </SectionedCard.Row>
      ))}
    </>
  );
}
