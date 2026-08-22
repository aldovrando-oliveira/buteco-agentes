import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { MessageTimeline } from './MessageTimeline';
import type { SessionMessage } from '../types/session';

const message: SessionMessage = {
  id: '11111111-1111-1111-1111-111111111111',
  direction: 'Inbound',
  content: 'Olá',
  contentType: 'Text',
  occurredAt: '2026-08-20T12:00:00Z',
  externalId: null,
  deliveryStatus: null,
  deliveryFailureReason: null,
  dispatchStatus: 'Completed',
};

function renderTimeline(messages: SessionMessage[]) {
  return render(
    <MantineProvider theme={theme}>
      <MessageTimeline messages={messages} />
    </MantineProvider>,
  );
}

describe('MessageTimeline', () => {
  it('sessão sem nenhuma mensagem renderiza o estado vazio, não erro nem tela em branco', () => {
    renderTimeline([]);

    expect(screen.getByText('Esta sessão ainda não tem nenhuma mensagem.')).toBeInTheDocument();
  });

  it('renderiza uma MessageBubble por mensagem', () => {
    renderTimeline([message]);

    expect(screen.getByText('Olá')).toBeInTheDocument();
  });
});
