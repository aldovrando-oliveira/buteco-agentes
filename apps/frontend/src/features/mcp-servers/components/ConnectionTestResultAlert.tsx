import { Alert } from '@mantine/core';

export interface ConnectionTestResult {
  success: boolean;
  message: string | null;
}

interface ConnectionTestResultAlertProps {
  result: ConnectionTestResult | undefined;
}

export function ConnectionTestResultAlert({ result }: ConnectionTestResultAlertProps) {
  if (!result) {
    return null;
  }

  return (
    <Alert
      color={result.success ? 'green' : 'red'}
      title={result.success ? 'Conexão bem-sucedida' : 'Falha na conexão'}
    >
      {result.success
        ? (result.message ?? 'A conexão com o servidor MCP foi estabelecida com sucesso.')
        : (result.message ?? 'Não foi possível conectar ao servidor MCP.')}
    </Alert>
  );
}
