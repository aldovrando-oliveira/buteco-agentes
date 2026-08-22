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

function Wrapper({ isEditing }: { isEditing: boolean }) {
  const form = useForm<ChannelFormValues>({ initialValues });
  return <WahaCredentialFields form={form} isEditing={isEditing} />;
}

function renderFields(isEditing: boolean) {
  render(
    <MantineProvider theme={theme}>
      <Wrapper isEditing={isEditing} />
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
});
