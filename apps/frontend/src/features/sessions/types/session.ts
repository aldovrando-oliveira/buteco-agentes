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
