import { useState } from 'react';
import { Alert, Box, Button, Group, NativeSelect, SimpleGrid, Stack, Text, TextInput, Title } from '@mantine/core';
import { isApiFailure } from '../../../utils/failureNotice';
import { ApiError, inviteUser, type InvitationCreated } from '../api/usersApi';
import { useCallerPassword } from '../callerPassword';
import { CallerPasswordField } from './CallerPasswordField';
import { PAPEIS, papelParaCorpo, rotuloDoPapel, type Papel } from '../../../auth/roles';
import { ActionDenied } from '../../../components/feedback/ActionDenied';
import { isRoleRefusal } from '../../../utils/failureNotice';
import { DESCRICAO_DO_PAPEL, ESCOLHA_O_PAPEL, PAPEL_SEM_ESCOLHA } from '../usersTexts';

// "Convidar usuário" dentro do cartão (requisito "Convidar usuário"; design.md da change
// usuarios-convites-identidade-painel, D3). A chamada é direta, sem `useMutation`: a resposta
// traz o link, e o `MutationCache` o guardaria depois de desmontar (D5; lição da #246).

interface InviteFormProps {
  onCreated: (result: InvitationCreated) => void;
  onClose: () => void;
}

interface Erros {
  nome?: string;
  email?: string;
  papel?: string;
}

export function InviteForm({ onCreated, onClose }: InviteFormProps) {
  const [nome, setNome] = useState('');
  const [email, setEmail] = useState('');
  // Sem valor inicial: o papel é obrigatório desde a #184, e a escolha é de quem convida
  // (change papeis-no-painel, D4, decisão do dono Q9).
  const [papel, setPapel] = useState<Papel | ''>('');
  const [recusado, setRecusado] = useState(false);
  const [erros, setErros] = useState<Erros>({});
  const senha = useCallerPassword();

  const enviar = async () => {
    setErros({});
    setRecusado(false);
    // Sem papel, nada sai: o servidor responderia `400`, e a senha seria enviada à toa.
    if (papel === '') {
      setErros({ papel: ESCOLHA_O_PAPEL });
      return;
    }
    const result = await senha.enviar(
      (s) => inviteUser({ nome, email, papel: papelParaCorpo(papel), senha: s }),
      (error) => {
        if (!isApiFailure(error)) return false;
        if (isRoleRefusal(error)) {
          setRecusado(true);
          return true;
        }
        if (error.status === 409) {
          setErros({ email: error.title ?? 'Já existe um usuário com este e-mail.' });
          return true;
        }
        if (error.status === 400 && error instanceof ApiError) {
          const campos = error.problem?.errors ?? {};
          setErros({ nome: campos.nome?.[0], email: campos.email?.[0], papel: campos.papel?.[0] });
          return true;
        }
        return false;
      },
    );
    if (result) onCreated(result);
  };

  return (
    <Box
      component="form"
      aria-labelledby="convidar-titulo"
      noValidate
      px="md"
      py="md"
      bg="var(--mantine-primary-color-light)"
      style={{ borderBottom: '1px solid var(--mantine-color-default-border)' }}
      onSubmit={(event) => {
        event.preventDefault();
        void enviar();
      }}
    >
      <Stack gap="sm">
        <Title order={3} id="convidar-titulo" fz="sm" fw={600}>
          Convidar usuário
        </Title>
        <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="sm">
          <TextInput label="Nome" value={nome} onChange={(event) => setNome(event.currentTarget.value)} error={erros.nome} />
          <TextInput
            label="E-mail"
            type="email"
            value={email}
            onChange={(event) => setEmail(event.currentTarget.value)}
            error={erros.email}
          />
          <NativeSelect
            label="Papel"
            value={papel}
            onChange={(event) => {
              setPapel(event.currentTarget.value as Papel | '');
              setErros((atual) => ({ ...atual, papel: undefined }));
            }}
            data={[{ value: '', label: 'Escolha o papel' }, ...PAPEIS.map((p) => ({ value: p, label: rotuloDoPapel(p) }))]}
            error={erros.papel}
            description={papel === '' ? PAPEL_SEM_ESCOLHA : DESCRICAO_DO_PAPEL[papel]}
            inputWrapperOrder={['label', 'input', 'error', 'description']}
          />
        </SimpleGrid>
        {recusado && <ActionDenied maw={560} />}
        {senha.aviso && (
          <Alert color={senha.aviso.cor} role="alert" maw={560}>
            {senha.aviso.texto}
          </Alert>
        )}
        <CallerPasswordField {...senha.campo} description="Para convidar alguém, confirme que é você." />
        <Text size="xs" c="dimmed">
          O Buteco gera um link de uso único, válido por 72 horas. Você copia o link e envia para a pessoa pelo canal
          que preferir.
        </Text>
        <Group gap="sm">
          <Button type="submit" size="sm" loading={senha.enviando} disabled={senha.bloqueado}>
            Gerar convite
          </Button>
          <Button variant="subtle" size="sm" onClick={onClose} disabled={senha.enviando}>
            Cancelar
          </Button>
        </Group>
      </Stack>
    </Box>
  );
}
