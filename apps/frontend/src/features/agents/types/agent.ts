export interface Agent {
  id: string;
  name: string;
  instructions: string;
  createdAt: string;
  updatedAt: string;
}

export interface CreateAgentInput {
  name: string;
  instructions: string;
}
