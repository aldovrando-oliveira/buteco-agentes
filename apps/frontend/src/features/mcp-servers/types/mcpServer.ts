export type McpServerAuthType = 'None' | 'BearerToken';

export interface McpServer {
  id: string;
  name: string;
  description: string;
  url: string;
  authType: McpServerAuthType;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateMcpServerInput {
  name: string;
  description: string;
  url: string;
  authType: McpServerAuthType;
  credential?: string;
}

export type UpdateMcpServerInput = CreateMcpServerInput;

export interface TestMcpServerConfigInput {
  url: string;
  authType: McpServerAuthType;
  credential?: string;
}

export type McpConnectionTestFailureReason =
  'HostUnreachable' | 'CredentialRejected' | 'CredentialDecryptionFailed' | 'Unknown';

export interface McpConnectionTestResult {
  success: boolean;
  failureReason: McpConnectionTestFailureReason | null;
  message: string | null;
}

export interface McpServerTool {
  name: string;
  description: string;
}

export interface McpServerToolsResult {
  success: boolean;
  tools: McpServerTool[] | null;
  failureReason: McpConnectionTestFailureReason | null;
  message: string | null;
}
