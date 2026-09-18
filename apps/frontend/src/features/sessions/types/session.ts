export type MessageDirection = 'Inbound' | 'Outbound';
export type MessageDeliveryStatus = 'Sent' | 'Failed';
export type MessageDispatchStatus = 'Pending' | 'Dispatching' | 'Failed' | 'Completed';
export type MessageContentType = 'Text' | 'Image' | 'Audio' | 'Document';

export interface MessagePreview {
  direction: MessageDirection;
  content: string;
  occurredAt: string;
}

export interface ChannelSession {
  sessionId: string;
  contactId: string;
  contactExternalId: string;
  contactDisplayName: string | null;
  lastActivityAt: string;
  lastMessage: MessagePreview | null;
}

export interface SessionMessage {
  id: string;
  direction: MessageDirection;
  content: string;
  contentType: MessageContentType;
  occurredAt: string;
  externalId: string | null;
  deliveryStatus: MessageDeliveryStatus | null;
  deliveryFailureReason: string | null;
  dispatchStatus: MessageDispatchStatus | null;
}

// Os nomes de campo são os do contrato, e carregam a semântica que o backend
// fixou: `startedCount` é "sessões INICIADAS no período" (não "ativas"), e
// `inboundCount` é "mensagens de ENTRADA distintas" — o contrato recusou
// "recebidas" para o campo por ser ambíguo quanto ao ponto de vista, e reservou
// o termo para a tela (specs inbox-session-period-summary e
// inbox-message-period-summary).
export interface SessionsSummary {
  startedCount: number;
}

export interface MessagesSummary {
  inboundCount: number;
}
