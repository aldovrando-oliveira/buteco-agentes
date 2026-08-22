import { Stack, Text } from '@mantine/core';
import { MessageBubble } from './MessageBubble';
import type { SessionMessage } from '../types/session';

interface MessageTimelineProps {
  messages: SessionMessage[];
}

export function MessageTimeline({ messages }: MessageTimelineProps) {
  if (messages.length === 0) {
    return <Text c="dimmed">Esta sessão ainda não tem nenhuma mensagem.</Text>;
  }

  return (
    <Stack gap="sm">
      {messages.map((message) => (
        <MessageBubble key={message.id} message={message} />
      ))}
    </Stack>
  );
}
