import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { MessageBubble } from './MessageBubble';
import type { SessionMessage } from '../types/session';

const baseMessage: SessionMessage = {
  id: '11111111-1111-1111-1111-111111111111',
  direction: 'Outbound',
  content: 'Olá, como posso ajudar?',
  contentType: 'Text',
  occurredAt: '2026-08-20T12:00:00Z',
  externalId: null,
  deliveryStatus: 'Sent',
  deliveryFailureReason: null,
  dispatchStatus: null,
};

function renderBubble(message: SessionMessage) {
  return render(
    <MantineProvider theme={theme}>
      <MessageBubble message={message} />
    </MantineProvider>,
  );
}

describe('MessageBubble', () => {
  it('saída Sent renderiza exatamente um indicador de envio, sem um segundo', () => {
    renderBubble({ ...baseMessage, deliveryStatus: 'Sent' });

    expect(screen.getByText('Enviado')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('saída Failed renderiza o indicador de erro com o motivo acessível', () => {
    renderBubble({
      ...baseMessage,
      deliveryStatus: 'Failed',
      deliveryFailureReason: 'Timeout ao conectar no provedor',
    });

    expect(screen.getByText('Falha no envio')).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('Timeout ao conectar no provedor');
  });

  it('os quatro valores de DispatchStatus renderizam indicadores distinguíveis entre si', () => {
    const labels: Record<SessionMessage['dispatchStatus'] & string, string> = {
      Pending: 'Pendente',
      Dispatching: 'Processando',
      Failed: 'Falha no processamento',
      Completed: 'Concluído',
    };

    (Object.keys(labels) as (keyof typeof labels)[]).forEach((status) => {
      const { unmount } = renderBubble({
        ...baseMessage,
        direction: 'Inbound',
        dispatchStatus: status,
        deliveryStatus: null,
      });

      expect(screen.getByText(labels[status])).toBeInTheDocument();
      unmount();
    });
  });

  it('mensagem de mídia renderiza o marcador com o tipo, sem tentar carregar binário', () => {
    renderBubble({ ...baseMessage, contentType: 'Image', content: 'foto.jpg' });

    expect(screen.getByText('Imagem')).toBeInTheDocument();
    expect(screen.getByText('foto.jpg')).toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });

  it('um DispatchStatus fora do union conhecido renderiza o indicador neutro, não o de Completed', () => {
    renderBubble({
      ...baseMessage,
      direction: 'Inbound',
      deliveryStatus: null,
      dispatchStatus: 'AlgumValorNovo' as SessionMessage['dispatchStatus'],
    });

    expect(screen.getByText('Status desconhecido')).toBeInTheDocument();
    expect(screen.queryByText('Concluído')).not.toBeInTheDocument();
  });

  it('um DeliveryStatus fora do union conhecido renderiza o indicador neutro, não o de Sent', () => {
    renderBubble({
      ...baseMessage,
      deliveryStatus: 'AlgumValorNovo' as SessionMessage['deliveryStatus'],
    });

    expect(screen.getByText('Status desconhecido')).toBeInTheDocument();
    expect(screen.queryByText('Enviado')).not.toBeInTheDocument();
  });

  it('um ContentType fora do union conhecido renderiza o marcador genérico, não uma tag de mídia real', () => {
    renderBubble({
      ...baseMessage,
      contentType: 'AlgumTipoNovo' as SessionMessage['contentType'],
      content: 'conteúdo bruto',
    });

    expect(screen.getByText('Tipo não reconhecido')).toBeInTheDocument();
    expect(screen.getByText('conteúdo bruto')).toBeInTheDocument();
  });

  it('um Direction fora do union conhecido renderiza a mensagem em forma neutra, sem bloco de dispatch nem de delivery', () => {
    renderBubble({
      ...baseMessage,
      direction: 'AlgumaDirecaoNova' as SessionMessage['direction'],
      deliveryStatus: 'Sent',
      dispatchStatus: 'Completed',
    });

    expect(screen.getByText('Direção desconhecida')).toBeInTheDocument();
    expect(screen.queryByText('Enviado')).not.toBeInTheDocument();
    expect(screen.queryByText('Concluído')).not.toBeInTheDocument();
    expect(screen.queryByText('Status desconhecido')).not.toBeInTheDocument();
  });
});
