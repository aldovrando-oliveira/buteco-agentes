import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { ConnectionTestResultAlert, type ConnectionTestResult } from './ConnectionTestResultAlert';

function renderAlert(result: ConnectionTestResult | undefined) {
  return render(
    <MantineProvider theme={theme}>
      <ConnectionTestResultAlert result={result} />
    </MantineProvider>,
  );
}

describe('ConnectionTestResultAlert', () => {
  it('não renderiza nada quando não há resultado', () => {
    renderAlert(undefined);

    expect(screen.queryByTestId('connection-test-result')).not.toBeInTheDocument();
  });

  it('exibe uma indicação de sucesso, sem afirmar quantidade de tools', () => {
    renderAlert({ success: true, failureReason: null, message: null });

    expect(screen.getByText('Conexão bem-sucedida')).toBeInTheDocument();
    expect(screen.getByText('A conexão com o servidor MCP foi estabelecida.')).toBeInTheDocument();
    expect(screen.queryByText(/tools disponíveis/i)).not.toBeInTheDocument();
  });

  it('explica host inalcançável indicando o que verificar', () => {
    renderAlert({ success: false, failureReason: 'HostUnreachable', message: null });

    expect(screen.getByText(/Host inalcançável/)).toBeInTheDocument();
    expect(screen.getByText(/Verifique o endereço e se o servidor está no ar/)).toBeInTheDocument();
    expect(screen.getByTestId('connection-test-failure-reason')).toHaveTextContent(
      'HostUnreachable',
    );
  });

  it('explica credencial rejeitada indicando gerar um novo token', () => {
    renderAlert({ success: false, failureReason: 'CredentialRejected', message: null });

    expect(screen.getByText(/Credencial rejeitada/)).toBeInTheDocument();
    expect(screen.getByText(/Gere um novo token/)).toBeInTheDocument();
  });

  it('explica falha ao decifrar a credencial indicando a mudança da chave do ambiente', () => {
    renderAlert({ success: false, failureReason: 'CredentialDecryptionFailed', message: null });

    expect(screen.getByText(/chave de criptografia do ambiente mudou/)).toBeInTheDocument();
    expect(screen.getByText(/Salve a credencial novamente/)).toBeInTheDocument();
  });

  it('explica a falha desconhecida', () => {
    renderAlert({ success: false, failureReason: 'Unknown', message: null });

    expect(screen.getByText('Falha desconhecida ao conectar ao servidor MCP.')).toBeInTheDocument();
  });

  it('exibe uma explicação genérica quando a falha não traz motivo', () => {
    renderAlert({ success: false, failureReason: null, message: null });

    expect(screen.getByText('Não foi possível conectar ao servidor MCP.')).toBeInTheDocument();
    expect(screen.queryByTestId('connection-test-failure-reason')).not.toBeInTheDocument();
  });

  it('exibe a mensagem da API como detalhe secundário, sem substituir a explicação', () => {
    renderAlert({
      success: false,
      failureReason: 'HostUnreachable',
      message: 'Não foi possível conectar ao servidor MCP: Connection refused.',
    });

    expect(screen.getByTestId('connection-test-api-message')).toHaveTextContent(
      'Connection refused',
    );
    expect(screen.getByText(/Host inalcançável/)).toBeInTheDocument();
  });

  it('indica quando o teste foi feito e que o resultado não é persistido', () => {
    renderAlert({
      success: true,
      failureReason: null,
      message: null,
      testedAt: new Date('2026-09-05T14:30:00'),
    });

    expect(screen.getByText(/testado às/)).toBeInTheDocument();
    expect(screen.getByText(/resultado não é persistido/)).toBeInTheDocument();
  });

  it('indica não persistência também em falha', () => {
    renderAlert({ success: false, failureReason: 'Unknown', message: null });

    expect(screen.getByText(/resultado não é persistido/)).toBeInTheDocument();
  });
});
