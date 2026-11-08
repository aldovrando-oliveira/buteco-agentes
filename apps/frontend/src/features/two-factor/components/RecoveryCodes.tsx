import { Badge, Button, CopyButton, Group, SimpleGrid, Stack, Text } from '@mantine/core';
import { downloadRecoveryCodes } from '../recoveryCodesFile';

// O passo dos códigos (requisito "Códigos de recuperação mostrados uma vez"): os 10 códigos,
// "Copiar todos" (um por linha), o .txt (gerado no navegador) e o botão final. Usado por Minha
// conta (os códigos novos) e pela etapa do cadastro do /entrar (design.md da change
// verificacao-2-etapas-obrigatoria, D7). Os rótulos são os de cada quadro: "Baixar arquivo .txt" e
// "Concluir" em `MinhaConta`, "Baixar .txt" e "Continuar" no `Main`.

interface RecoveryCodesProps {
  codigos: string[];
  email: string;
  titulo: string;
  onDone: () => void;
  downloadLabel?: string;
  doneLabel?: string;
  /** O selo "Ativa" ao lado do título: em Minha conta sim, no /entrar não. */
  badge?: boolean;
}

export function RecoveryCodes({
  codigos,
  email,
  titulo,
  onDone,
  downloadLabel = 'Baixar arquivo .txt',
  doneLabel = 'Concluir',
  badge = true,
}: RecoveryCodesProps) {
  return (
    <Stack gap="sm">
      <Group gap="xs" role="status">
        {badge && (
          <Badge color="green" variant="light">
            Ativa
          </Badge>
        )}
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
          {downloadLabel}
        </Button>
        <Button onClick={onDone}>{doneLabel}</Button>
      </Group>
    </Stack>
  );
}
