export type McpServerAuthType = 'None' | 'BearerToken' | 'CustomHeader';

export interface McpServer {
  id: string;
  name: string;
  description: string;
  url: string;
  authType: McpServerAuthType;
  // Nome do header do tipo CustomHeader; nulo nos demais. A API sempre devolve
  // o campo (McpServerResponse, change mcp-autenticacao-header-customizado, D2).
  authHeaderName: string | null;
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
  // Enviado só com CustomHeader (design.md, D4).
  authHeaderName?: string;
}

export type UpdateMcpServerInput = CreateMcpServerInput;

export interface TestMcpServerConfigInput {
  url: string;
  authType: McpServerAuthType;
  credential?: string;
  authHeaderName?: string;
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
