import { useState, type ReactNode } from 'react';
import { Alert, Badge, Button, Group, Stack, Text, TextInput } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError, getAccount, getTwoFactor, regenerateRecoveryCodes, type TwoFactorStatus } from '../api/accountApi';
import { formatDate } from '../../../utils/panelDates';
import { CODIGO_INVALIDO, INFORME_O_CODIGO } from '../../auth/messages';
import { blockedUntil, failureNotice, type FailureNotice } from '../../../utils/failureNotice';
import { useBlockedUntil } from '../../../hooks/useBlockedUntil';
import { AccountCard, CardLoadError, CardLoading } from './AccountCard';
import { ACCOUNT_QUERY_KEY, TWO_FACTOR_QUERY_KEY } from '../queryKeys';
import { fewCodesText, NAO_CONFIGURADA_TEXTO, remainingText, TROCOU_DE_CELULAR } from '../accountTexts';
import { RecoveryCodes } from '../../two-factor/components/RecoveryCodes';

// O cartão "Verificação em 2 etapas" (design.md da change minha-conta-painel, D4 e D5; e da change
// verificacao-2-etapas-obrigatoria, D7). A verificação é obrigatória e o cadastro é só o do login:
// o cartão mostra a situação e gera novos códigos de recuperação. Sem "Ligar verificação" e sem
// "Remover verificação" (decisão do dono, Q5); quem troca de celular pede a outro usuário que
// redefina a verificação.
//
// Os códigos novos ficam SÓ no estado deste componente: nada vai para storage, URL, `state` do
// router ou o cache do TanStack Query. Por isso a chamada que os traz é direta, e não
// `useMutation`: o `MutationCache` guardaria o `data` da mutação até o `gcTime`, mesmo depois de
// sair da tela. Sair do passo, ou da página, leva tudo junto.

type Passo = { tipo: 'estado' } | { tipo: 'codigos'; codigos: string[] } | { tipo: 'regenerar' };

export function TwoFactorCard() {
  const queryClient = useQueryClient();
  const status = useQuery({ queryKey: TWO_FACTOR_QUERY_KEY, queryFn: getTwoFactor });
  const account = useQuery({ queryKey: ACCOUNT_QUERY_KEY, queryFn: getAccount });
  const [passo, setPasso] = useState<Passo>({ tipo: 'estado' });
  const [aviso, setAviso] = useState<FailureNotice | null>(null);
  const [erroDoCampo, setErroDoCampo] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [bloqueado, setBloqueadoAte] = useBlockedUntil();

  const ir = (proximo: Passo) => {
    setAviso(null);
    setErroDoCampo(null);
    setPasso(proximo);
  };

  const reler = async () => {
    await queryClient.invalidateQueries({ queryKey: TWO_FACTOR_QUERY_KEY });
    await queryClient.invalidateQueries({ queryKey: ACCOUNT_QUERY_KEY });
  };

  const regenerar = async (codigo: string) => {
    setAviso(null);
    setErroDoCampo(null);
    setEnviando(true);
    try {
      const { codigosRecuperacao } = await regenerateRecoveryCodes(codigo);
      ir({ tipo: 'codigos', codigos: codigosRecuperacao });
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) setErroDoCampo(error.title ?? CODIGO_INVALIDO);
      else if (error instanceof ApiError && (error.status === 409 || error.status === 403)) {
        // A verificação foi redefinida em outro lugar: o cartão relê e vai ao estado do GET.
        await reler();
        ir({ tipo: 'estado' });
      } else {
        setAviso(failureNotice(error));
        setBloqueadoAte(blockedUntil(error));
      }
    } finally {
      setEnviando(false);
    }
  };

  const concluir = async () => {
    await reler();
    ir({ tipo: 'estado' });
  };

  const avisoDoCartao = aviso && (
    <Alert color={aviso.cor} role="alert">
      {aviso.texto}
    </Alert>
  );

  return (
    <AccountCard title="Verificação em 2 etapas">
      {status.isPending ? (
        <CardLoading label="Carregando a verificação em 2 etapas" />
      ) : status.isError ? (
        <CardLoadError error={status.error} onRetry={() => void status.refetch()} />
      ) : passo.tipo === 'estado' ? (
        <TwoFactorState status={status.data} onRegenerate={() => ir({ tipo: 'regenerar' })} notice={avisoDoCartao} />
      ) : passo.tipo === 'codigos' ? (
        <RecoveryCodes
          codigos={passo.codigos}
          email={account.data?.email ?? ''}
          titulo="Novos códigos gerados."
          onDone={() => void concluir()}
        />
      ) : (
        <CodeStep
          notice={avisoDoCartao}
          error={erroDoCampo}
          submitting={enviando}
          disabled={bloqueado}
          onSubmit={(codigo) => void regenerar(codigo)}
          onCancel={() => ir({ tipo: 'estado' })}
        />
      )}
    </AccountCard>
  );
}

function TwoFactorState({
  status,
  onRegenerate,
  notice,
}: {
  status: TwoFactorStatus;
  onRegenerate: () => void;
  notice: ReactNode;
}) {
  if (!status.ativa) {
    // Só no intervalo em que o access token de uma sessão de antes da obrigatoriedade ainda vale
    // (design.md da change verificacao-2-etapas-obrigatoria, D4): o próximo login pede o cadastro.
    return (
      <Stack gap="sm">
        {notice}
        <Stack gap={4}>
          <Badge color="yellow" variant="light" w="fit-content">
            Não configurada
          </Badge>
          <Text size="sm" c="dimmed">
            {NAO_CONFIGURADA_TEXTO}
          </Text>
        </Stack>
      </Stack>
    );
  }

  const poucos = fewCodesText(status.codigosRestantes);
  return (
    <Stack gap="sm">
      {notice}
      <Group justify="space-between" wrap="nowrap" align="center">
        <Stack gap={4}>
          <Group gap="xs">
            <Badge color="green" variant="light">
              Ativa
            </Badge>
            {status.ativadaEm && (
              <Text size="sm" c="dimmed">
                desde {formatDate(status.ativadaEm)}
              </Text>
            )}
          </Group>
          <Text size="sm" c="dimmed">
            {remainingText(status.codigosRestantes)}
          </Text>
        </Stack>
        <Button variant="default" onClick={onRegenerate}>
          Gerar novos códigos
        </Button>
      </Group>
      <Text size="sm" c="dimmed">
        {TROCOU_DE_CELULAR}
      </Text>
      {poucos && (
        <Alert color="yellow" role="status">
          {poucos}
        </Alert>
      )}
    </Stack>
  );
}

interface StepProps {
  notice: ReactNode;
  error: string | null;
  submitting: boolean;
  disabled: boolean;
  onSubmit: (value: string) => void;
  onCancel: () => void;
}

function CodeStep({ notice, error, submitting, disabled, onSubmit, onCancel }: StepProps) {
  const [codigo, setCodigo] = useState('');
  const [vazio, setVazio] = useState(false);
  return (
    <form
      noValidate
      onSubmit={(event) => {
        event.preventDefault();
        // Só as pontas: o código de recuperação tem hífens, e o do app pode vir com espaço no meio.
        const limpo = codigo.trim();
        setVazio(limpo === '');
        if (limpo !== '') onSubmit(limpo);
      }}
    >
      <Stack gap="sm" maw={420}>
        <Stack gap={2}>
          <Text fw={600} size="sm">
            Gerar novos códigos de recuperação
          </Text>
          <Text size="sm" c="dimmed">
            Os códigos atuais deixam de valer.
          </Text>
        </Stack>
        {notice}
        <TextInput
          label="Código do app ou de recuperação"
          inputMode="text"
          autoComplete="one-time-code"
          autoCapitalize="characters"
          spellCheck={false}
          value={codigo}
          onChange={(event) => setCodigo(event.currentTarget.value)}
          error={vazio ? INFORME_O_CODIGO : error}
          aria-invalid={vazio || error ? true : undefined}
        />
        <Group gap="sm">
          <Button type="submit" loading={submitting} disabled={disabled}>
            Gerar códigos
          </Button>
          <Button variant="subtle" onClick={onCancel} disabled={submitting}>
            Cancelar
          </Button>
        </Group>
      </Stack>
    </form>
  );
}
