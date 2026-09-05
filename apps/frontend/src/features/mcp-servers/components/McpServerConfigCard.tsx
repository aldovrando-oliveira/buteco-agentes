import { Card, Group, Stack, Text } from '@mantine/core';
import type { McpServer } from '../types/mcpServer';

interface McpServerConfigCardProps {
  mcpServer: McpServer;
}

const authTypeLabels: Record<McpServer['authType'], string> = {
  None: 'Nenhuma',
  BearerToken: 'Bearer Token',
};

function ConfigRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <Group justify="space-between" gap="sm" wrap="nowrap" align="flex-start">
      <Text size="sm" c="dimmed" style={{ flexShrink: 0 }}>
        {label}
      </Text>
      {children}
    </Group>
  );
}

export function McpServerConfigCard({ mcpServer }: McpServerConfigCardProps) {
  return (
    <Card withBorder>
      <Stack gap="sm">
        <Text size="xs" fw={600} tt="uppercase" c="dimmed">
          Configuração
        </Text>

        <ConfigRow label="Url">
          <Text size="sm" ff="monospace" ta="right" style={{ wordBreak: 'break-all' }}>
            {mcpServer.url}
          </Text>
        </ConfigRow>

        <ConfigRow label="Autenticação">
          <Text size="sm" ta="right">
            {authTypeLabels[mcpServer.authType]}
          </Text>
        </ConfigRow>

        {/* A API nunca devolve o valor da credencial. A linha existe para
            responder se existe credencial salva, e diz que está cifrada
            para que a máscara não seja lida como truncamento (Decision 7
            do design.md da change frontend-mcp-servidor-uso-e-diagnostico). */}
        {mcpServer.authType !== 'None' && (
          <ConfigRow label="Credencial">
            <Text size="sm" c="dimmed" ta="right" data-testid="credential-row">
              •••••••• cifrada, nunca devolvida pela API
            </Text>
          </ConfigRow>
        )}

        <ConfigRow label="Criado em">
          <Text size="sm" ta="right">
            {new Date(mcpServer.createdAt).toLocaleString('pt-BR')}
          </Text>
        </ConfigRow>

        <ConfigRow label="Atualizado em">
          <Text size="sm" ta="right">
            {new Date(mcpServer.updatedAt).toLocaleString('pt-BR')}
          </Text>
        </ConfigRow>
      </Stack>
    </Card>
  );
}
