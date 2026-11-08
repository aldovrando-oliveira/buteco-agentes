import { Button, Code, CopyButton, Group } from '@mantine/core';

// A caixa do link de uso único com "Copiar link", comum ao painel do convite e ao da
// redefinição de senha (decisão do dono Q11 da change editar-usuario-painel). Copia o link
// como veio e troca o rótulo por "Copiado ✓". Sem `navigator.clipboard`, o link continua
// visível e selecionável.

export function CopyableLink({ link }: { link: string }) {
  return (
    <Group gap="sm" wrap="nowrap" align="center">
      <Code
        block
        style={{ flex: 1, minWidth: 0, overflowWrap: 'anywhere', whiteSpace: 'pre-wrap' }}
      >
        {link}
      </Code>
      <CopyButton value={link}>
        {({ copied, copy }) => (
          <Button size="sm" color={copied ? 'green' : undefined} onClick={copy} aria-live="polite">
            {copied ? 'Copiado ✓' : 'Copiar link'}
          </Button>
        )}
      </CopyButton>
    </Group>
  );
}
