import { Stack, Text, Title } from '@mantine/core';
import { AccountDataCard } from '../components/AccountDataCard';
import { AccountPasswordCard } from '../components/AccountPasswordCard';
import { TwoFactorCard } from '../components/TwoFactorCard';
import { AccountSessionsCard } from '../components/AccountSessionsCard';

// Minha conta (`/account`), o quadro `MinhaConta` com `entrega=m1` (design.md da change
// minha-conta-painel, D1). A entrada é o bloco do nome no rodapé da casca, sem item no
// menu. Cada cartão lê os próprios dados: o erro de um não derruba os outros.
export function AccountPage() {
  return (
    <Stack maw={880}>
      <Stack gap={4}>
        <Title order={2}>Minha conta</Title>
        <Text c="dimmed" size="sm">
          Os seus dados, a sua senha, a verificação em 2 etapas e os navegadores em que você está conectado.
        </Text>
      </Stack>
      <AccountDataCard />
      <AccountPasswordCard />
      <TwoFactorCard />
      <AccountSessionsCard />
    </Stack>
  );
}
