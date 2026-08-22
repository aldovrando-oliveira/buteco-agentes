import { PasswordInput, Stack, TextInput } from '@mantine/core';
import type { UseFormReturnType } from '@mantine/form';
import type { ChannelFormValues } from '../types/channel';

interface WahaCredentialFieldsProps {
  form: UseFormReturnType<ChannelFormValues>;
  isEditing: boolean;
}

export function WahaCredentialFields({ form, isEditing }: WahaCredentialFieldsProps) {
  const keepPlaceholder = isEditing ? ' (deixe em branco para manter a atual)' : '';

  return (
    <Stack gap="sm">
      <TextInput
        label="URL do serviço"
        placeholder={`http://localhost:3000${keepPlaceholder}`}
        withAsterisk={!isEditing}
        {...form.getInputProps('waha.serviceUrl')}
      />
      <TextInput
        label="Nome da sessão"
        placeholder={`Nome da sessão do WAHA${keepPlaceholder}`}
        withAsterisk={!isEditing}
        {...form.getInputProps('waha.sessionName')}
      />
      <PasswordInput
        label="Token de autenticação"
        placeholder={`Token de autenticação${keepPlaceholder}`}
        withAsterisk={!isEditing}
        {...form.getInputProps('waha.authToken')}
      />
    </Stack>
  );
}
