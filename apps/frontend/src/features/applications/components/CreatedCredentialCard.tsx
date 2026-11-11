import { useState } from 'react';
import { Button, Group, Paper, Stack, Text, Title } from '@mantine/core';
import type { CreatedApplication } from '../types';
import { SE_SE_PERDER } from '../applicationsTexts';
import { CopyableValue, OnlyNowNotice, SavedGate } from './SecretOnce';

// O cartão "Aplicação criada" no lugar do formulário, em `/applications/new` (quadro
// `AplicacaoCredencial`, versão 66; spec `panel-external-applications`, "Credencial criada,
// mostrada uma vez"). Sem modal. "Ir para a aplicação" só depois da caixa; sair por outro caminho
// não é bloqueado (Q17). O segredo chega por prop do `useState` da página e morre com ela.

interface CreatedCredentialCardProps {
  criada: CreatedApplication;
  onIr: () => void;
}

export function CreatedCredentialCard({ criada, onIr }: CreatedCredentialCardProps) {
  const [guardei, setGuardei] = useState(false);
  return (
    <Paper withBorder radius="md" maw={680} p="lg" aria-labelledby="aplicacao-criada">
      <Stack gap="md">
        <Stack gap={4}>
          <Title order={2} id="aplicacao-criada" fz={16} fw={600}>
            Aplicação criada
          </Title>
          <Text size="sm" c="dimmed">
            Entregue estes dados ao responsável pelo <b>{criada.nome}</b> por um canal seguro.
          </Text>
        </Stack>
        <CopyableValue label="Client ID" value={criada.clientId} />
        <CopyableValue label="Segredo" value={criada.segredo.valor} />
        <CopyableValue label="Endpoint de token" value={criada.endpointToken} />
        <OnlyNowNotice complemento={SE_SE_PERDER} />
        <SavedGate guardei={guardei} onGuardei={setGuardei}>
          {(desabilitado) => (
            <Group justify="flex-end">
              <Button onClick={onIr} disabled={desabilitado}>
                Ir para a aplicação
              </Button>
            </Group>
          )}
        </SavedGate>
      </Stack>
    </Paper>
  );
}
