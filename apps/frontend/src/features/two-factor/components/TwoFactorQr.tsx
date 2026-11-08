import { Box, Button, CopyButton, Group, Stack, Text } from '@mantine/core';
import { QRCodeSVG } from 'qrcode.react';
import { groupSecret } from '../secretText';

// O QR e o segredo da etapa do cadastro no /entrar (spec `operator-login-ui`; design.md da change
// minha-conta-painel, D5, e da change verificacao-2-etapas-obrigatoria, D7), na coluna do cartão
// de entrada, como no quadro `Main`. O QR sai do `otpauthUri` no navegador, pelo `QRCodeSVG` do
// qrcode.react 4.2.0 (ISC), que monta o SVG por React.createElement, sem innerHTML. Sem
// `imageSettings`: nada de `<image>` dentro do SVG. Módulos escuros sobre branco, com a zona
// quieta, também no tema escuro: o QR invertido falha em parte dos apps autenticadores.

export function TwoFactorQr({ segredo, otpauthUri }: { segredo: string; otpauthUri: string }) {
  return (
    <Stack gap="xs" align="center">
      <Box bg="#fff" p={6} style={{ borderRadius: 6, lineHeight: 0 }}>
        <QRCodeSVG
          value={otpauthUri}
          size={164}
          marginSize={2}
          bgColor="#ffffff"
          fgColor="#191a1c"
          title="QR para o app autenticador"
          aria-label="QR para o app autenticador"
        />
      </Box>
      <Text size="sm" c="dimmed" ta="center">
        Ele aparece no app como <b>Buteco Agentes</b>, com o seu e-mail. Sem câmera? Digite este código no app:
      </Text>
      <Group gap="xs" wrap="nowrap" justify="center">
        <Text component="code" ff="monospace" size="sm" data-testid="segredo">
          {groupSecret(segredo)}
        </Text>
        <CopyButton value={segredo}>
          {({ copied, copy }) => (
            <Button size="xs" variant="default" onClick={copy}>
              {copied ? 'Copiado' : 'Copiar'}
            </Button>
          )}
        </CopyButton>
      </Group>
    </Stack>
  );
}
