import { useState } from 'react';
import { Alert, Box, Button, Group, SimpleGrid, Stack, Text, TextInput } from '@mantine/core';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { failureNotice, isApiFailure } from '../../../utils/failureNotice';
import { ACCOUNT_QUERY_KEY } from '../../account/queryKeys';
import { ApiError, updateUser, type UserDetail } from '../api/usersApi';
import { USERS_QUERY_KEY, userQueryKey } from '../queryKeys';

// O cartão Dados de Editar usuário (requisito "Cartão Dados de Editar usuário"; design.md da
// change editar-usuario-painel, D5). O rascunho só existe enquanto difere do lido: voltar o texto
// ao original desabilita "Salvar", e sem rascunho o formulário mostra a leitura mais nova. A
// resposta não tem segredo, então vai pelo `useMutation` e direto para o cache do detalhe. Para o
// próprio, Minha conta é relida; o rodapé da casca muda só na renovação seguinte (Q13).

interface UserDataCardProps {
  user: UserDetail;
  proprio: boolean;
  /** O usuário sumiu entre a leitura e o `PUT` (`404`). */
  onGone: () => void;
}

interface Erros {
  nome?: string;
  email?: string;
}

export function UserDataCard({ user, proprio, onGone }: UserDataCardProps) {
  const queryClient = useQueryClient();
  const [rascunho, setRascunho] = useState<{ nome: string; email: string } | null>(null);
  const [erros, setErros] = useState<Erros>({});
  const [salvo, setSalvo] = useState(false);

  const nome = rascunho?.nome ?? user.nome;
  const email = rascunho?.email ?? user.email;
  const sujo = nome !== user.nome || email !== user.email;

  const salvar = useMutation({
    mutationFn: () => updateUser(user.id, { nome, email }),
    onSuccess: (detalhe) => {
      queryClient.setQueryData(userQueryKey(user.id), detalhe);
      void queryClient.invalidateQueries({ queryKey: USERS_QUERY_KEY, exact: true });
      if (proprio) void queryClient.invalidateQueries({ queryKey: ACCOUNT_QUERY_KEY });
      setRascunho(null);
      setSalvo(true);
    },
    onError: (error) => {
      if (!isApiFailure(error)) return;
      if (error.status === 409)
        setErros({ email: error.title ?? 'Já existe um usuário com este e-mail.' });
      else if (error.status === 400 && error instanceof ApiError) {
        const campos = error.problem?.errors ?? {};
        setErros({ nome: campos.nome?.[0], email: campos.email?.[0] });
      } else if (error.status === 404) onGone();
    },
  });

  // O aviso do cartão é só para o que não vai em campo nem leva a página a "inexistente".
  const erro = salvar.error;
  const avisoNoCartao =
    erro && !(isApiFailure(erro) && [400, 404, 409].includes(erro.status))
      ? failureNotice(erro)
      : null;

  const editar = (campo: 'nome' | 'email', valor: string) => {
    setSalvo(false);
    setErros({});
    salvar.reset();
    setRascunho({ nome, email, [campo]: valor });
  };

  return (
    <SectionedCard title="Dados">
      <Box
        component="form"
        noValidate
        px="md"
        py="md"
        onSubmit={(event) => {
          event.preventDefault();
          if (sujo) salvar.mutate();
        }}
      >
        <Stack gap="md">
          <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="md">
            <TextInput
              label="Nome"
              value={nome}
              onChange={(e) => editar('nome', e.currentTarget.value)}
              error={erros.nome}
            />
            <TextInput
              label="E-mail"
              type="email"
              value={email}
              onChange={(e) => editar('email', e.currentTarget.value)}
              error={erros.email}
              description="É o e-mail de entrada no painel."
              inputWrapperOrder={['label', 'input', 'error', 'description']}
            />
          </SimpleGrid>
          {avisoNoCartao && (
            <Alert color={avisoNoCartao.cor} maw={560}>
              {avisoNoCartao.texto}
            </Alert>
          )}
          <Group gap="sm">
            <Button type="submit" size="sm" disabled={!sujo} loading={salvar.isPending}>
              Salvar alterações
            </Button>
            {sujo && (
              <Button
                variant="subtle"
                color="gray"
                size="sm"
                disabled={salvar.isPending}
                onClick={() => {
                  setRascunho(null);
                  setErros({});
                  salvar.reset();
                }}
              >
                Descartar
              </Button>
            )}
            {salvo && (
              <Text size="xs" c="green" role="status">
                Alterações salvas.
              </Text>
            )}
          </Group>
        </Stack>
      </Box>
    </SectionedCard>
  );
}
