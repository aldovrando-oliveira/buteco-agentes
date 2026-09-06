import { Alert, Anchor, Button, CopyButton, Group, Stack, Text } from '@mantine/core';
import { Check, Copy, ExternalLink } from 'lucide-react';
import { SectionedCard } from '../../../components/data/SectionedCard';
import type { AgentA2AAddresses } from '../types/agent';

interface AgentA2ACardProps {
  a2a: AgentA2AAddresses | null | undefined;
  isActive: boolean;
}

function AddressRow({ label, value, open }: { label: string; value: string; open?: boolean }) {
  return (
    <Stack gap={2}>
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Group gap="xs" wrap="nowrap" align="center">
        <Text size="xs" ff="monospace" style={{ minWidth: 0, wordBreak: 'break-all' }}>
          {value}
        </Text>
        <Group gap={4} wrap="nowrap" style={{ flexShrink: 0 }}>
          <CopyButton value={value}>
            {({ copied, copy }) => (
              <Button
                size="compact-xs"
                variant="default"
                onClick={copy}
                aria-label={`Copiar ${label}`}
                leftSection={copied ? <Check size={12} /> : <Copy size={12} />}
              >
                {copied ? 'Copiado' : 'Copiar'}
              </Button>
            )}
          </CopyButton>
          {open && (
            <Anchor
              href={value}
              target="_blank"
              rel="noreferrer"
              size="xs"
              aria-label={`Abrir ${label}`}
            >
              <ExternalLink size={14} />
            </Anchor>
          )}
        </Group>
      </Group>
    </Stack>
  );
}

// Os dois endereços públicos pelos quais um sistema externo alcança este
// agente. Vêm prontos da API: a URL pública é configuração do servidor, e
// montá-la aqui a partir do host de onde a página foi servida daria endereço
// certo em desenvolvimento e errado atrás de proxy (design.md da change
// agente-enderecos-a2a, D1).
export function AgentA2ACard({ a2a, isActive }: AgentA2ACardProps) {
  return (
    <SectionedCard title="Protocolo A2A" data-testid="agent-a2a-card">
      <SectionedCard.Body>
        {!a2a ? (
          <Text size="sm" c="dimmed">
            O endereço público do servidor não está configurado, então não há como exibir os
            endereços deste agente.
          </Text>
        ) : (
          <Stack gap="sm">
            <AddressRow label="Endpoint de execução" value={a2a.url} />
            <AddressRow label="Card de descoberta" value={a2a.agentCardUrl} open />
            {/* As duas coisas valem ao mesmo tempo, e o endereço visível
                sozinho sugere o contrário: o card responde para agente
                inativo, e o endpoint de execução rejeita o que ele receber. */}
            {!isActive && (
              <Alert color="yellow" data-testid="a2a-inactive-callout">
                Este agente continua descobrível por estes endereços, mas rejeita as mensagens que
                receber enquanto estiver inativo.
              </Alert>
            )}
          </Stack>
        )}
      </SectionedCard.Body>
    </SectionedCard>
  );
}
