import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { useForm } from '@mantine/form';
import { theme } from '../../../theme';
import { TelegramCredentialFields } from './TelegramCredentialFields';
import type { ChannelFormValues } from '../types/channel';

const initialValues: ChannelFormValues = {
  name: '',
  agentId: '',
  channelType: 'telegram',
  waha: { serviceUrl: '', sessionName: '', authToken: '' },
  telegram: { botToken: '' },
};

function Wrapper({ isEditing, errors }: { isEditing: boolean; errors?: Record<string, string> }) {
  const form = useForm<ChannelFormValues>({ initialValues, initialErrors: errors });
  return <TelegramCredentialFields form={form} isEditing={isEditing} />;
}

function renderFields(isEditing: boolean, errors?: Record<string, string>) {
  render(
    <MantineProvider theme={theme}>
      <Wrapper isEditing={isEditing} errors={errors} />
    </MantineProvider>,
  );
}

describe('TelegramCredentialFields', () => {
  it('renderiza o campo de token do bot, vazio', () => {
    renderFields(false);

    expect(screen.getByLabelText(/token do bot/i)).toHaveValue('');
  });

  it('em modo edição, o placeholder indica que vazio mantém o token atual', () => {
    renderFields(true);

    expect(screen.getByLabelText(/token do bot/i)).toHaveAttribute(
      'placeholder',
      'Deixe em branco para manter o token atual',
    );
  });

  // #170: o campo de senha com erro é anunciado como inválido. O texto é o da validação
  // do ChannelForm para o token vazio.
  it('com o token obrigatório vazio, o campo é anunciado como inválido', () => {
    renderFields(false, { 'telegram.botToken': 'O token do bot é obrigatório.' });

    expect(screen.getByLabelText(/token do bot/i)).toHaveAttribute('aria-invalid', 'true');
  });
});
