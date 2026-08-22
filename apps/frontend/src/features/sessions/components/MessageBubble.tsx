import { Badge, Group, Paper, Stack, Text } from '@mantine/core';
import type { SessionMessage } from '../types/session';

const dispatchStatusLabels: Partial<Record<string, { label: string; color: string }>> = {
  Pending: { label: 'Pendente', color: 'gray' },
  Dispatching: { label: 'Processando', color: 'blue' },
  Failed: { label: 'Falha no processamento', color: 'red' },
  Completed: { label: 'Concluído', color: 'green' },
};

const contentTypeLabels: Partial<Record<string, string>> = {
  Image: 'Imagem',
  Audio: 'Áudio',
  Document: 'Documento',
};

const FAILURE_REASON_MAX_LENGTH = 140;

function truncateFailureReason(reason: string): string {
  const collapsed = reason.replace(/\s+/g, ' ').trim();
  return collapsed.length > FAILURE_REASON_MAX_LENGTH
    ? `${collapsed.slice(0, FAILURE_REASON_MAX_LENGTH)}…`
    : collapsed;
}

interface MessageContentProps {
  message: SessionMessage;
}

function MessageContent({ message }: MessageContentProps) {
  if (message.contentType === 'Text') {
    return <Text size="sm">{message.content}</Text>;
  }

  const mediaLabel = contentTypeLabels[message.contentType] ?? 'Tipo não reconhecido';

  return (
    <Stack gap={4}>
      <Badge color="gray" variant="light">
        {mediaLabel}
      </Badge>
      <Text size="sm">{message.content}</Text>
    </Stack>
  );
}

function OutboundStatus({ message }: MessageContentProps) {
  if (message.deliveryStatus === 'Sent') {
    return <Badge color="teal">Enviado</Badge>;
  }

  if (message.deliveryStatus === 'Failed') {
    return (
      <Stack gap={4} align="flex-end">
        <Badge color="red">Falha no envio</Badge>
        {message.deliveryFailureReason && (
          <Text size="xs" c="red" role="alert">
            {truncateFailureReason(message.deliveryFailureReason)}
          </Text>
        )}
      </Stack>
    );
  }

  return <Badge color="gray">Status desconhecido</Badge>;
}

function InboundStatus({ message }: MessageContentProps) {
  const known = message.dispatchStatus ? dispatchStatusLabels[message.dispatchStatus] : undefined;

  if (!known) {
    return <Badge color="gray">Status desconhecido</Badge>;
  }

  return <Badge color={known.color}>{known.label}</Badge>;
}

interface MessageBubbleProps {
  message: SessionMessage;
}

export function MessageBubble({ message }: MessageBubbleProps) {
  if (message.direction !== 'Inbound' && message.direction !== 'Outbound') {
    return (
      <Paper withBorder p="sm">
        <Stack gap={4}>
          <Badge color="gray" variant="light">
            Direção desconhecida
          </Badge>
          <MessageContent message={message} />
        </Stack>
      </Paper>
    );
  }

  const isOutbound = message.direction === 'Outbound';

  return (
    <Paper withBorder p="sm" ml={isOutbound ? 'auto' : 0} mr={isOutbound ? 0 : 'auto'} maw="70%">
      <Stack gap={4}>
        <MessageContent message={message} />
        <Group justify={isOutbound ? 'flex-end' : 'flex-start'}>
          {isOutbound ? <OutboundStatus message={message} /> : <InboundStatus message={message} />}
        </Group>
      </Stack>
    </Paper>
  );
}
