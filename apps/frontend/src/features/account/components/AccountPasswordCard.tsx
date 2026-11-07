import { useState } from 'react';
import { Alert, Button, Group, Stack, Text } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { PasswordField } from '../../../components/forms/PasswordField';
import { logout } from '../../../auth/session';
import { ApiError, changePassword, getAccount } from '../api/accountApi';
import { POLITICA_DE_SENHA, SENHAS_DIFERENTES } from '../../auth/messages';
import { passwordFitsPolicy } from '../../auth/passwordPolicy';
import { markPasswordChanged } from '../../auth/passwordChangedNotice';
import { blockedUntil, failureNotice, type FailureNotice } from '../cardFailure';
import { useBlockedUntil } from '../useBlockedUntil';
import { AccountCard } from './AccountCard';
import { ACCOUNT_QUERY_KEY } from '../queryKeys';

// O cartão "Senha" (requisito "Troca da própria senha"; design.md da change
// minha-conta-painel, D2 e D3). Trocar a senha revoga todas as sessões da pessoa, inclusive
// esta (#219 D3): depois do 204, grava o aviso do /entrar e sai pelo mesmo logout do "Sair".
// A ordem importa: o logout navega, e o aviso tem de estar gravado antes.

interface Erros {
  atual?: string;
  nova?: string;
  repeticao?: string;
}

export function AccountPasswordCard() {
  const account = useQuery({ queryKey: ACCOUNT_QUERY_KEY, queryFn: getAccount });
  const [aberto, setAberto] = useState(false);
  const [atual, setAtual] = useState('');
  const [nova, setNova] = useState('');
  const [repeticao, setRepeticao] = useState('');
  const [erros, setErros] = useState<Erros>({});
  const [aviso, setAviso] = useState<FailureNotice | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [saindo, setSaindo] = useState(false);
  const [bloqueado, setBloqueadoAte] = useBlockedUntil();

  const fechar = () => {
    setAberto(false);
    setAtual('');
    setNova('');
    setRepeticao('');
    setErros({});
    setAviso(null);
  };

  const enviar = async () => {
    const locais: Erros = {};
    if (!passwordFitsPolicy(nova)) locais.nova = POLITICA_DE_SENHA;
    else if (repeticao !== nova) locais.repeticao = SENHAS_DIFERENTES;
    setErros(locais);
    setAviso(null);
    if (locais.nova || locais.repeticao) return;

    setEnviando(true);
    try {
      await changePassword(atual, nova);
    } catch (error) {
      setEnviando(false);
      if (error instanceof ApiError && error.status === 401) {
        setErros({ atual: error.title ?? 'Senha atual inválida.' });
        return;
      }
      if (error instanceof ApiError && error.status === 400) {
        setErros({ nova: error.title ?? POLITICA_DE_SENHA });
        return;
      }
      setAviso(failureNotice(error));
      setBloqueadoAte(blockedUntil(error));
      return;
    }
    markPasswordChanged();
    setSaindo(true);
    await logout();
  };

  return (
    <AccountCard
      title="Senha"
      action={
        !aberto && (
          <Button size="xs" onClick={() => setAberto(true)}>
            Trocar senha
          </Button>
        )
      }
    >
      {!aberto ? (
        <Text size="sm">
          Você entra com{' '}
          <Text span ff="monospace" size="sm">
            {account.data?.email ?? '…'}
          </Text>{' '}
          e a sua senha.
        </Text>
      ) : (
        <form
          noValidate
          onSubmit={(event) => {
            event.preventDefault();
            void enviar();
          }}
        >
          <Stack gap="sm" maw={420}>
            {aviso && (
              <Alert color={aviso.cor} role="alert">
                {aviso.texto}
              </Alert>
            )}
            <PasswordField
              label="Senha atual"
              autoComplete="current-password"
              value={atual}
              onChange={(event) => setAtual(event.currentTarget.value)}
              error={erros.atual}
            />
            <PasswordField
              label="Nova senha"
              autoComplete="new-password"
              description="De 12 a 128 caracteres."
              value={nova}
              onChange={(event) => setNova(event.currentTarget.value)}
              error={erros.nova}
            />
            <PasswordField
              label="Repita a nova senha"
              autoComplete="new-password"
              value={repeticao}
              onChange={(event) => setRepeticao(event.currentTarget.value)}
              error={erros.repeticao}
            />
            <Text size="sm" c="dimmed">
              Ao trocar, você sai de todos os navegadores, inclusive deste, e entra de novo com a nova senha.
            </Text>
            <Group gap="sm">
              <Button type="submit" loading={enviando || saindo} disabled={bloqueado || saindo}>
                {saindo ? 'Saindo…' : 'Trocar senha'}
              </Button>
              <Button variant="subtle" onClick={fechar} disabled={enviando || saindo}>
                Cancelar
              </Button>
            </Group>
          </Stack>
        </form>
      )}
    </AccountCard>
  );
}
