import { Anchor, Box, Button, Group, Stack, Text } from '@mantine/core';
import { Link } from 'react-router';
import { SectionedCard } from '../../../components/data/SectionedCard';
import type { FailureNotice } from '../../../utils/failureNotice';
import type { UserDetail } from '../api/usersApi';
import { confirmEndSessions, sessionsLine } from '../userDetailTexts';
import { ConfirmBar } from './ConfirmBar';

// As sessões de outro usuário: a contagem e a última atividade, que vêm no detalhe; a lista
// não existe (#219). Encerrar não pede senha (requisito "Sessões de outro usuário"; design.md
// da change editar-usuario-painel, D10). O próprio vê só a contagem (Q12) e vai a Minha conta.

interface UserSessionsCardProps {
  user: UserDetail;
  proprio: boolean;
  confirmando: boolean;
  enviando: boolean;
  falha: FailureNotice | null;
  /** "N sessões encerradas.", depois do `200`, até a próxima ação. */
  encerradas: string | null;
  onAsk: () => void;
  onConfirm: () => void;
  onCancel: () => void;
}

export function UserSessionsCard({
  user,
  proprio,
  confirmando,
  enviando,
  falha,
  encerradas,
  onAsk,
  onConfirm,
  onCancel,
}: UserSessionsCardProps) {
  const ativas = user.sessoes.ativas;
  return (
    <SectionedCard title="Sessões">
      <Group justify="space-between" px="md" py="sm" gap="sm">
        <Stack gap={2}>
          <Text size="sm" fw={600}>
            {sessionsLine(user, proprio)}
          </Text>
          <Text size="sm" c="dimmed">
            {proprio
              ? 'As suas sessões aparecem em Minha conta, onde você pode encerrar cada uma.'
              : 'Encerrar faz a pessoa precisar entrar de novo em todos os navegadores. Use se suspeitar de acesso indevido.'}
          </Text>
        </Stack>
        {proprio ? (
          <Anchor component={Link} to="/account" size="sm">
            Ver em Minha conta
          </Anchor>
        ) : (
          ativas >= 1 && (
            <Button size="xs" variant="default" disabled={confirmando} onClick={onAsk}>
              Encerrar todas as sessões
            </Button>
          )
        )}
      </Group>
      {confirmando && (
        <ConfirmBar
          texto={confirmEndSessions(ativas, user.nome)}
          confirmLabel="Encerrar"
          onConfirm={onConfirm}
          onCancel={onCancel}
          enviando={enviando}
          falha={falha}
        />
      )}
      {encerradas && (
        <Box
          role="status"
          px="md"
          py="xs"
          bg="var(--mantine-color-green-light)"
          c="var(--mantine-color-green-light-color)"
        >
          <Text size="sm">{encerradas}</Text>
        </Box>
      )}
    </SectionedCard>
  );
}
