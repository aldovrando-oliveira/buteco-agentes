export interface Agent {
  id: string;
  name: string;
  instructions: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateAgentInput {
  name: string;
  instructions: string;
}

export type UpdateAgentInput = CreateAgentInput;
