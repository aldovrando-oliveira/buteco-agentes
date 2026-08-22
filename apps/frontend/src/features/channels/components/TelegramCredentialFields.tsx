import { PasswordInput, Stack } from '@mantine/core';
import type { UseFormReturnType } from '@mantine/form';
import type { ChannelFormValues } from '../types/channel';

interface TelegramCredentialFieldsProps {
  form: UseFormReturnType<ChannelFormValues>;
  isEditing: boolean;
}

export function TelegramCredentialFields({ form, isEditing }: TelegramCredentialFieldsProps) {
  return (
    <Stack gap="sm">
      <PasswordInput
        label="Token do bot"
        placeholder={
          isEditing ? 'Deixe em branco para manter o token atual' : 'Token do bot do Telegram'
        }
        withAsterisk={!isEditing}
        {...form.getInputProps('telegram.botToken')}
      />
    </Stack>
  );
}
