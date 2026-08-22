export type ChannelType = 'waha' | 'telegram';

export interface Channel {
  id: string;
  channelType: ChannelType;
  name: string;
  agentId: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
  webhookUrl: string;
}

export interface WahaCredentialInput {
  serviceUrl: string;
  sessionName: string;
  authToken: string;
}

export interface TelegramCredentialInput {
  botToken: string;
}

export interface ChannelFormValues {
  name: string;
  agentId: string;
  channelType: ChannelType;
  waha: WahaCredentialInput;
  telegram: TelegramCredentialInput;
}

export interface CreateChannelInput {
  name: string;
  channelType: ChannelType;
  agentId: string;
  credential: string;
}

export interface UpdateChannelInput {
  name: string;
  agentId: string;
  credential?: string;
}
