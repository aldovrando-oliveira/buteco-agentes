import { Stack } from '@mantine/core';
import { PasswordField } from '../../../components/forms/PasswordField';
import type { UseFormReturnType } from '@mantine/form';
import type { ChannelFormValues } from '../types/channel';

interface TelegramCredentialFieldsProps {
  form: UseFormReturnType<ChannelFormValues>;
  isEditing: boolean;
}

export function TelegramCredentialFields({ form, isEditing }: TelegramCredentialFieldsProps) {
  return (
    <Stack gap="sm">
      <PasswordField
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
