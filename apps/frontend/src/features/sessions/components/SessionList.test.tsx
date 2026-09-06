import { describe, expect, it, vi } from 'vitest';
import type { ComponentProps } from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { SessionList } from './SessionList';
import type { ChannelSession } from '../types/session';

const sessionWithName: ChannelSession = {
  sessionId: '11111111-1111-1111-1111-111111111111',
  contactId: '22222222-2222-2222-2222-222222222222',
  contactExternalId: '5511999999999',
  contactDisplayName: 'Maria',
  lastActivityAt: '2026-08-20T12:00:00Z',
  lastMessage: {
    direction: 'Inbound',
    content: 'Olá, tudo bem?',
    occurredAt: '2026-08-20T12:00:00Z',
  },
};

const sessionWithoutName: ChannelSession = {
  ...sessionWithName,
  sessionId: '33333333-3333-3333-3333-333333333333',
  contactDisplayName: null,
};

const sessionWithoutPreview: ChannelSession = {
  ...sessionWithName,
  sessionId: '44444444-4444-4444-4444-444444444444',
  lastMessage: null,
};

function renderList(props: Partial<ComponentProps<typeof SessionList>> = {}) {
  return render(
    <MantineProvider theme={theme}>
      <SessionList sessions={[]} onSelect={vi.fn()} {...props} />
    </MantineProvider>,
  );
}

describe('SessionList', () => {
  it('renderiza o nome de exibição do contato quando existe', () => {
    renderList({ sessions: [sessionWithName] });

    expect(screen.getByText('Maria')).toBeInTheDocument();
  });

  it('renderiza o ContactExternalId quando ContactDisplayName é nulo', () => {
    renderList({ sessions: [sessionWithoutName] });

    expect(screen.getByText(sessionWithoutName.contactExternalId)).toBeInTheDocument();
  });

  it('renderiza a prévia da última mensagem quando existe', () => {
    renderList({ sessions: [sessionWithName] });

    expect(screen.getByText('Olá, tudo bem?')).toBeInTheDocument();
  });

  it('renderiza o instante de última atividade', () => {
    renderList({ sessions: [sessionWithName] });

    expect(
      screen.getByText(new Date(sessionWithName.lastActivityAt).toLocaleString('pt-BR')),
    ).toBeInTheDocument();
  });

  it('renderiza a linha normalmente, sem erro, quando LastMessage é nulo', () => {
    renderList({ sessions: [sessionWithoutPreview] });

    expect(screen.getByText('Maria')).toBeInTheDocument();
    expect(
      screen.getByText(new Date(sessionWithoutPreview.lastActivityAt).toLocaleString('pt-BR')),
    ).toBeInTheDocument();
  });

  it('canal sem sessão nenhuma renderiza estado vazio', () => {
    renderList({ sessions: [] });

    expect(screen.getByText('Nenhuma sessão para este canal ainda.')).toBeInTheDocument();
  });

  it('clicar em uma sessão chama onSelect com o sessionId', async () => {
    const onSelect = vi.fn();
    const user = userEvent.setup();
    renderList({ sessions: [sessionWithName], onSelect });

    await user.click(screen.getByText('Maria'));

    expect(onSelect).toHaveBeenCalledWith(sessionWithName.sessionId);
  });
});
