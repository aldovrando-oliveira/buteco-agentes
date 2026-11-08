import { Button, Group, Stack, Text } from '@mantine/core';
import { SectionedCard } from '../../../components/data/SectionedCard';
import type { FailureNotice } from '../../../utils/failureNotice';
import type { UserDetail } from '../api/usersApi';
import { confirmResetTwoFactor } from '../userDetailTexts';
import { ConfirmBar } from './ConfirmBar';
import { Selo } from './Selo';

// A verificação em 2 etapas de outro usuário (requisito "Verificação em 2 etapas de outro
// usuário"; design.md da change editar-usuario-painel, D9). Sem a data de ativação, que saiu do
// quadro. O próprio gerencia a dele em Minha conta, com um código.

interface UserTwoFactorCardProps {
  user: UserDetail;
  proprio: boolean;
  confirmando: boolean;
  enviando: boolean;
  falha: FailureNotice | null;
  onAsk: () => void;
  onConfirm: () => void;
  onCancel: () => void;
}

export function UserTwoFactorCard({
  user,
  proprio,
  confirmando,
  enviando,
  falha,
  onAsk,
  onConfirm,
  onCancel,
}: UserTwoFactorCardProps) {
  const ativa = user.duasEtapas;
  const texto = proprio
    ? 'Gerencie a sua em Minha conta.'
    : ativa
      ? 'Use Redefinir se a pessoa perdeu o celular e os códigos de recuperação.'
      : 'Cada pessoa liga a própria verificação em Minha conta.';

  return (
    <SectionedCard title="Verificação em 2 etapas">
      <Group justify="space-between" px="md" py="sm" gap="sm">
        <Stack gap={2}>
          <div>
            <Selo texto={ativa ? 'Ativa' : 'Não configurada'} tom={ativa ? 'green' : 'yellow'} />
          </div>
          <Text size="sm" c="dimmed">
            {texto}
          </Text>
        </Stack>
        {!proprio && (
          <Button size="xs" variant="default" disabled={!ativa || confirmando} onClick={onAsk}>
            Redefinir verificação
          </Button>
        )}
      </Group>
      {confirmando && (
        <ConfirmBar
          texto={confirmResetTwoFactor(user.nome)}
          confirmLabel="Redefinir"
          onConfirm={onConfirm}
          onCancel={onCancel}
          enviando={enviando}
          falha={falha}
        />
      )}
    </SectionedCard>
  );
}
