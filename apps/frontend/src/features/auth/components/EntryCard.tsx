import type { ReactNode } from 'react';
import { Center, Paper, Stack, Text } from '@mantine/core';

interface EntryCardProps {
  children: ReactNode;
  /** O texto abaixo do cartão, fora dele, como nos quadros. */
  footer?: ReactNode;
  /** 380 no /entrar e 400 nas páginas do link, como nos quadros. */
  width?: number;
}

// O cartão das páginas sem sessão: /entrar (quadro `Main`), /convite e /redefinir-senha
// (quadros `AceitarConviteSemProvedor` e `RedefinirSenha`).
export function EntryCard({ children, footer, width = 380 }: EntryCardProps) {
  return (
    <Center mih="100vh" p="md">
      <Stack gap="md" w="100%" maw={width}>
        <Paper withBorder shadow="sm" p="xl">
          {children}
        </Paper>
        {footer && (
          <Text ta="center" fz="xs" c="dimmed">
            {footer}
          </Text>
        )}
      </Stack>
    </Center>
  );
}
