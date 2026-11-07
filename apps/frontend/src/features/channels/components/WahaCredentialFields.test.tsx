import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { useForm } from '@mantine/form';
import { theme } from '../../../theme';
import { WahaCredentialFields } from './WahaCredentialFields';
import type { ChannelFormValues } from '../types/channel';

const initialValues: ChannelFormValues = {
  name: '',
  agentId: '',
  channelType: 'waha',
  waha: { serviceUrl: '', sessionName: '', authToken: '' },
  telegram: { botToken: '' },
};

function Wrapper({ isEditing, errors }: { isEditing: boolean; errors?: Record<string, string> }) {
  const form = useForm<ChannelFormValues>({ initialValues, initialErrors: errors });
  return <WahaCredentialFields form={form} isEditing={isEditing} />;
}

function renderFields(isEditing: boolean, errors?: Record<string, string>) {
  render(
    <MantineProvider theme={theme}>
      <Wrapper isEditing={isEditing} errors={errors} />
    </MantineProvider>,
  );
}

describe('WahaCredentialFields', () => {
  it('renderiza os três campos de credencial do WAHA, todos vazios', () => {
    renderFields(false);

    expect(screen.getByLabelText(/url do serviço/i)).toHaveValue('');
    expect(screen.getByLabelText(/nome da sessão/i)).toHaveValue('');
    expect(screen.getByLabelText(/token de autenticação/i)).toHaveValue('');
  });

  it('em modo edição, os placeholders indicam que vazio mantém a credencial atual', () => {
    renderFields(true);

    expect(screen.getByLabelText(/url do serviço/i)).toHaveAttribute(
      'placeholder',
      expect.stringContaining('deixe em branco para manter a atual'),
    );
    expect(screen.getByLabelText(/nome da sessão/i)).toHaveAttribute(
      'placeholder',
      expect.stringContaining('deixe em branco para manter a atual'),
    );
    expect(screen.getByLabelText(/token de autenticação/i)).toHaveAttribute(
      'placeholder',
      expect.stringContaining('deixe em branco para manter a atual'),
    );
  });

  // #170: o campo de senha com erro é anunciado como inválido. O texto é o da validação
  // do ChannelForm para o token vazio.
  it('com o token obrigatório vazio, o campo é anunciado como inválido', () => {
    renderFields(false, { 'waha.authToken': 'O token de autenticação é obrigatório.' });

    expect(screen.getByLabelText(/token de autenticação/i)).toHaveAttribute('aria-invalid', 'true');
  });
});
