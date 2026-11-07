import { Badge, Button, CopyButton, Group, SimpleGrid, Stack, Text } from '@mantine/core';
import { downloadRecoveryCodes } from '../recoveryCodesFile';

// O passo dos códigos (requisito "Códigos de recuperação mostrados uma vez"): os 10 códigos,
// "Copiar todos" (um por linha), "Baixar arquivo .txt" (gerado no navegador) e "Concluir".

interface RecoveryCodesProps {
  codigos: string[];
  email: string;
  titulo: string;
  onDone: () => void;
}

export function RecoveryCodes({ codigos, email, titulo, onDone }: RecoveryCodesProps) {
  return (
    <Stack gap="sm">
      <Group gap="xs" role="status">
        <Badge color="green" variant="light">
          Ativa
        </Badge>
        <Text fw={600} size="sm">
          {titulo}
        </Text>
      </Group>
      <Text size="sm">
        Guarde estes 10 códigos de recuperação num lugar seguro, fora do celular. Cada um vale uma vez e serve
        para entrar se você perder o app autenticador.
      </Text>
      <SimpleGrid cols={2} spacing={6} maw={420} aria-label="Códigos de recuperação" role="list">
        {codigos.map((codigo) => (
          <Text key={codigo} ff="monospace" size="sm" role="listitem">
            {codigo}
          </Text>
        ))}
      </SimpleGrid>
      <Text size="sm" c="dimmed">
        O Buteco não mostra estes códigos de novo.
      </Text>
      <Group gap="sm">
        <CopyButton value={codigos.join('\n')}>
          {({ copied, copy }) => (
            <Button variant="default" onClick={copy}>
              {copied ? 'Copiados' : 'Copiar todos'}
            </Button>
          )}
        </CopyButton>
        <Button variant="default" onClick={() => downloadRecoveryCodes(codigos, email)}>
          Baixar arquivo .txt
        </Button>
        <Button onClick={onDone}>Concluir</Button>
      </Group>
    </Stack>
  );
}
