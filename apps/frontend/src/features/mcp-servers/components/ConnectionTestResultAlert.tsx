import { Alert, Code, Group, Stack, Text } from '@mantine/core';
import type { McpConnectionTestFailureReason } from '../types/mcpServer';

export interface ConnectionTestResult {
  success: boolean;
  failureReason?: McpConnectionTestFailureReason | null;
  message: string | null;
  // Momento em que o teste foi executado, mantido apenas em memória: a API
  // não persiste resultado de teste, e a interface diz isso para que
  // ninguém leia a informação como histórico ou monitoramento.
  testedAt?: Date;
}

interface ConnectionTestResultAlertProps {
  result: ConnectionTestResult | undefined;
}

// A API devolve `failureReason` estruturado e uma `message` que concatena
// texto de exceção. A orientação vem daqui, em linguagem do operador; a
// mensagem da API fica como detalhe secundário, porque distinguir falha de
// DNS de recusa de conexão costuma ser o que resolve o caso (Decision 3 do
// design.md da change frontend-mcp-servidor-uso-e-diagnostico).
const failureExplanations: Record<McpConnectionTestFailureReason, string> = {
  HostUnreachable:
    'Host inalcançável — a URL não respondeu. Verifique o endereço e se o servidor está no ar.',
  CredentialRejected:
    'Credencial rejeitada — o servidor recusou a credencial enviada. Gere um novo token e salve a credencial novamente.',
  CredentialDecryptionFailed:
    'Não foi possível decifrar a credencial — a chave de criptografia do ambiente mudou desde que ela foi salva. Salve a credencial novamente.',
  Unknown: 'Falha desconhecida ao conectar ao servidor MCP.',
};

export function ConnectionTestResultAlert({ result }: ConnectionTestResultAlertProps) {
  if (!result) {
    return null;
  }

  const explanation = result.success
    ? 'A conexão com o servidor MCP foi estabelecida.'
    : result.failureReason
      ? failureExplanations[result.failureReason]
      : 'Não foi possível conectar ao servidor MCP.';

  const testedAtLabel = result.testedAt
    ? `testado às ${result.testedAt.toLocaleTimeString('pt-BR')}`
    : 'testado agora';

  return (
    <Alert
      color={result.success ? 'green' : 'red'}
      title={result.success ? 'Conexão bem-sucedida' : 'Falha na conexão'}
      data-testid="connection-test-result"
    >
      <Stack gap={6}>
        <Group gap="xs" wrap="nowrap" align="flex-start">
          {!result.success && result.failureReason && (
            <Code data-testid="connection-test-failure-reason">{result.failureReason}</Code>
          )}
          <Text size="sm">{explanation}</Text>
        </Group>

        {!result.success && result.message && (
          <Text size="xs" c="dimmed" data-testid="connection-test-api-message">
            {result.message}
          </Text>
        )}

        <Text size="xs" c="dimmed">
          {testedAtLabel} · resultado não é persistido
        </Text>
      </Stack>
    </Alert>
  );
}
