import { Box, Stack, Text, Title } from '@mantine/core';
import { useLocation } from 'react-router';
import { papelDaResposta, rotuloDoPapel } from '../../../auth/roles';
import { AccountDataCard } from '../components/AccountDataCard';
import { AccountPasswordCard } from '../components/AccountPasswordCard';
import { TwoFactorCard } from '../components/TwoFactorCard';
import { AccountSessionsCard } from '../components/AccountSessionsCard';

// Minha conta (`/account`), o quadro `MinhaConta` com `entrega=m1` (design.md da change
// minha-conta-painel, D1). A entrada é o bloco do nome no rodapé da casca, sem item no
// menu. Cada cartão lê os próprios dados: o erro de um não derruba os outros.
/**
 * O papel novo que o rebaixamento de si mesmo traz no estado da navegação, e não na URL
 * (spec `panel-account-page`, "Aviso de papel novo depois de rebaixar a si mesmo"). Admin e
 * papel desconhecido não têm aviso; recarregar a página perde o estado, e o aviso.
 */
function papelNovoDoEstado(state: unknown) {
  const papel = papelDaResposta((state as { papelNovo?: unknown } | null)?.papelNovo);
  return papel === 'admin' ? null : papel;
}

export function AccountPage() {
  const papelNovo = papelNovoDoEstado(useLocation().state);
  return (
    <Stack maw={880}>
      <Stack gap={4}>
        <Title order={2}>Minha conta</Title>
        <Text c="dimmed" size="sm">
          Os seus dados, a sua senha, os códigos de recuperação e os navegadores em que você está conectado.
        </Text>
      </Stack>
      {papelNovo && (
        <Box
          role="status"
          px="md"
          py="sm"
          bg="var(--mantine-primary-color-light)"
          c="var(--mantine-primary-color-light-color)"
          style={{ borderRadius: 'var(--mantine-radius-md)' }}
        >
          O seu papel agora é {rotuloDoPapel(papelNovo)}.
        </Box>
      )}
      <AccountDataCard />
      <AccountPasswordCard />
      <TwoFactorCard />
      <AccountSessionsCard />
    </Stack>
  );
}
