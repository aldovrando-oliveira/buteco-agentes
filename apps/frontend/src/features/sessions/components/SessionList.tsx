import { Stack, Text, UnstyledButton } from '@mantine/core';
import type { ChannelSession } from '../types/session';

interface SessionListProps {
  sessions: ChannelSession[];
  selectedSessionId?: string;
  onSelect: (sessionId: string) => void;
}

export function SessionList({ sessions, selectedSessionId, onSelect }: SessionListProps) {
  if (sessions.length === 0) {
    return <Text c="dimmed">Nenhuma sessão para este canal ainda.</Text>;
  }

  return (
    <Stack gap={0}>
      {sessions.map((session) => {
        const label = session.contactDisplayName ?? session.contactExternalId;
        const isSelected = session.sessionId === selectedSessionId;

        return (
          <UnstyledButton
            key={session.sessionId}
            onClick={() => onSelect(session.sessionId)}
            p="sm"
            bg={isSelected ? 'gray.1' : undefined}
            aria-current={isSelected ? 'true' : undefined}
          >
            <Stack gap={2}>
              <Text fw={600}>{label}</Text>
              {session.lastMessage && (
                <Text size="sm" c="dimmed" lineClamp={1}>
                  {session.lastMessage.content}
                </Text>
              )}
              <Text size="xs" c="dimmed">
                {new Date(session.lastActivityAt).toLocaleString('pt-BR')}
              </Text>
            </Stack>
          </UnstyledButton>
        );
      })}
    </Stack>
  );
}
