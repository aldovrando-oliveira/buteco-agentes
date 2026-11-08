import { useState, type ReactNode } from 'react';
import { Alert, Badge, Button, Group, Stack, Text, TextInput } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { PasswordField } from '../../../components/forms/PasswordField';
import {
  ApiError,
  confirmTwoFactor,
  getAccount,
  getTwoFactor,
  regenerateRecoveryCodes,
  removeTwoFactor,
  setupTwoFactor,
  type TwoFactorSetup,
  type TwoFactorStatus,
} from '../api/accountApi';
import { formatDate } from '../../../utils/panelDates';
import { CODIGO_INVALIDO, INFORME_O_CODIGO } from '../../auth/messages';
import { blockedUntil, failureNotice, type FailureNotice } from '../../../utils/failureNotice';
import { useBlockedUntil } from '../../../hooks/useBlockedUntil';
import { AccountCard, CardLoadError, CardLoading } from './AccountCard';
import { ACCOUNT_QUERY_KEY, TWO_FACTOR_QUERY_KEY } from '../queryKeys';
import { CADASTRO_VENCEU, CONFIRMA_CODIGO_INVALIDO, fewCodesText, remainingText } from '../accountTexts';
import { RecoveryCodes } from './RecoveryCodes';
import { TwoFactorQr } from './TwoFactorQr';

// O cartão "Verificação em 2 etapas" (design.md da change minha-conta-painel, D4 e D5).
//
// O segredo, o otpauthUri e os códigos de recuperação ficam SÓ no estado deste componente:
// nada vai para storage, URL, `state` do router ou o cache do TanStack Query. Por isso as
// quatro chamadas que os trazem ou os consomem são chamadas diretas, e não `useMutation`: o
// `MutationCache` guardaria o `data` da mutação até o `gcTime`, mesmo depois de sair da tela.
// Sair do passo, ou da página, leva tudo junto.

type Passo =
  | { tipo: 'estado' }
  | { tipo: 'senha' }
  | { tipo: 'qr'; setup: TwoFactorSetup }
  | { tipo: 'codigos'; codigos: string[]; novos: boolean }
  | { tipo: 'regenerar' }
  | { tipo: 'remover' };

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

  /** A falha comum dos passos: `409` e `403` releem o estado; o resto vira o aviso do cartão. */
  const falhou = async (error: unknown) => {
    if (error instanceof ApiError && (error.status === 409 || error.status === 403)) {
      await reler();
      ir({ tipo: 'estado' });
      return;
    }
    setAviso(failureNotice(error));
    setBloqueadoAte(blockedUntil(error));
  };

  const enviar = async (acao: () => Promise<void>) => {
    setAviso(null);
    setErroDoCampo(null);
    setEnviando(true);
    try {
      await acao();
    } finally {
      setEnviando(false);
    }
  };

  const comecar = (senha: string) =>
    enviar(async () => {
      try {
        ir({ tipo: 'qr', setup: await setupTwoFactor(senha) });
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) setErroDoCampo(error.title ?? 'Senha inválida.');
        else await falhou(error);
      }
    });

  const confirmar = (codigo: string) =>
    enviar(async () => {
      try {
        const { codigosRecuperacao } = await confirmTwoFactor(codigo);
        ir({ tipo: 'codigos', codigos: codigosRecuperacao, novos: false });
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) setErroDoCampo(CONFIRMA_CODIGO_INVALIDO);
        else if (error instanceof ApiError && error.status === 400) setAviso({ texto: CADASTRO_VENCEU, cor: 'red' });
        else await falhou(error);
      }
    });

  const regenerar = (codigo: string) =>
    enviar(async () => {
      try {
        const { codigosRecuperacao } = await regenerateRecoveryCodes(codigo);
        ir({ tipo: 'codigos', codigos: codigosRecuperacao, novos: true });
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) setErroDoCampo(error.title ?? CODIGO_INVALIDO);
        else await falhou(error);
      }
    });

  const remover = (codigo: string) =>
    enviar(async () => {
      try {
        await removeTwoFactor(codigo);
        await reler();
        ir({ tipo: 'estado' });
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) setErroDoCampo(error.title ?? CODIGO_INVALIDO);
        else await falhou(error);
      }
    });

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
        <TwoFactorState
          status={status.data}
          onEnable={() => ir({ tipo: 'senha' })}
          onRegenerate={() => ir({ tipo: 'regenerar' })}
          onRemove={() => ir({ tipo: 'remover' })}
          notice={avisoDoCartao}
        />
      ) : passo.tipo === 'senha' ? (
        <PasswordStep
          notice={avisoDoCartao}
          error={erroDoCampo}
          submitting={enviando}
          disabled={bloqueado}
          onSubmit={comecar}
          onCancel={() => ir({ tipo: 'estado' })}
        />
      ) : passo.tipo === 'qr' ? (
        <QrStep
          setup={passo.setup}
          notice={avisoDoCartao}
          error={erroDoCampo}
          submitting={enviando}
          disabled={bloqueado}
          onSubmit={confirmar}
          onCancel={() => ir({ tipo: 'estado' })}
        />
      ) : passo.tipo === 'codigos' ? (
        <RecoveryCodes
          codigos={passo.codigos}
          email={account.data?.email ?? ''}
          titulo={passo.novos ? 'Novos códigos gerados.' : 'Verificação ligada.'}
          onDone={() => void concluir()}
        />
      ) : (
        <CodeStep
          key={passo.tipo}
          title={passo.tipo === 'regenerar' ? 'Gerar novos códigos de recuperação' : 'Remover a verificação em 2 etapas'}
          text={
            passo.tipo === 'regenerar'
              ? 'Os códigos atuais deixam de valer.'
              : 'Depois disso, só a senha protege a sua conta, e os códigos de recuperação deixam de valer.'
          }
          submitLabel={passo.tipo === 'regenerar' ? 'Gerar códigos' : 'Remover verificação'}
          danger={passo.tipo === 'remover'}
          notice={avisoDoCartao}
          error={erroDoCampo}
          submitting={enviando}
          disabled={bloqueado}
          onSubmit={passo.tipo === 'regenerar' ? regenerar : remover}
          onCancel={() => ir({ tipo: 'estado' })}
        />
      )}
    </AccountCard>
  );
}

function TwoFactorState({
  status,
  onEnable,
  onRegenerate,
  onRemove,
  notice,
}: {
  status: TwoFactorStatus;
  onEnable: () => void;
  onRegenerate: () => void;
  onRemove: () => void;
  notice: ReactNode;
}) {
  if (!status.ativa) {
    return (
      <Stack gap="sm">
        {notice}
        <Group justify="space-between" wrap="nowrap" align="center">
          <Stack gap={4}>
            <Badge color="yellow" variant="light" w="fit-content">
              Não configurada
            </Badge>
            <Text size="sm" c="dimmed">
              {status.cadastroDisponivel
                ? 'Com a verificação ligada, além da senha você informa um código do app autenticador do celular.'
                : 'O cadastro da verificação em 2 etapas ainda não está disponível neste painel.'}
            </Text>
          </Stack>
          <Button onClick={onEnable} disabled={!status.cadastroDisponivel} variant={status.cadastroDisponivel ? 'filled' : 'default'}>
            Ligar verificação
          </Button>
        </Group>
      </Stack>
    );
  }

  // A regeneração e a remoção valem com qualquer valor de `cadastroDisponivel` (two-factor-totp).
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
        <Group gap="xs" wrap="nowrap">
          <Button variant="default" onClick={onRegenerate}>
            Gerar novos códigos
          </Button>
          <Button variant="subtle" color="red" onClick={onRemove}>
            Remover verificação
          </Button>
        </Group>
      </Group>
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

function PasswordStep({ notice, error, submitting, disabled, onSubmit, onCancel }: StepProps) {
  const [senha, setSenha] = useState('');
  return (
    <form
      noValidate
      onSubmit={(event) => {
        event.preventDefault();
        onSubmit(senha);
      }}
    >
      <Stack gap="sm" maw={420}>
        <Stack gap={2}>
          <Text fw={600} size="sm">
            Ligar a verificação em 2 etapas
          </Text>
          <Text size="sm" c="dimmed">
            Você vai precisar de um app autenticador no celular, como Google Authenticator, Microsoft Authenticator
            ou 1Password.
          </Text>
        </Stack>
        {notice}
        <PasswordField
          label="Sua senha"
          autoComplete="current-password"
          description="Para começar, confirme que é você."
          value={senha}
          onChange={(event) => setSenha(event.currentTarget.value)}
          error={error}
        />
        <Group gap="sm">
          <Button type="submit" loading={submitting} disabled={disabled}>
            Continuar
          </Button>
          <Button variant="subtle" onClick={onCancel} disabled={submitting}>
            Cancelar
          </Button>
        </Group>
      </Stack>
    </form>
  );
}

function QrStep({ setup, notice, error, submitting, disabled, onSubmit, onCancel }: StepProps & { setup: TwoFactorSetup }) {
  const [codigo, setCodigo] = useState('');
  const [vazio, setVazio] = useState(false);
  return (
    <form
      noValidate
      onSubmit={(event) => {
        event.preventDefault();
        const limpo = codigo.trim();
        setVazio(limpo === '');
        if (limpo !== '') onSubmit(limpo);
      }}
    >
      <Stack gap="md">
        <Group align="flex-start" gap="sm" wrap="nowrap">
          <Badge circle size="lg" variant="light">
            1
          </Badge>
          <TwoFactorQr segredo={setup.segredo} otpauthUri={setup.otpauthUri} />
        </Group>
        <Group align="flex-start" gap="sm" wrap="nowrap">
          <Badge circle size="lg" variant="light">
            2
          </Badge>
          <Stack gap="sm" maw={360} style={{ flex: 1 }}>
            <TextInput
              label="Digite o código de 6 dígitos que o app mostra"
              inputMode="numeric"
              autoComplete="one-time-code"
              placeholder="000 000"
              value={codigo}
              onChange={(event) => setCodigo(event.currentTarget.value)}
              error={vazio ? INFORME_O_CODIGO : error}
              aria-invalid={vazio || error ? true : undefined}
            />
            {notice}
            <Text size="xs" c="dimmed">
              Este cadastro vale por 10 minutos. Depois disso, comece de novo.
            </Text>
            <Group gap="sm">
              <Button type="submit" loading={submitting} disabled={disabled}>
                Confirmar
              </Button>
              <Button variant="subtle" onClick={onCancel} disabled={submitting}>
                Cancelar
              </Button>
            </Group>
          </Stack>
        </Group>
      </Stack>
    </form>
  );
}

function CodeStep({
  title,
  text,
  submitLabel,
  danger,
  notice,
  error,
  submitting,
  disabled,
  onSubmit,
  onCancel,
}: StepProps & { title: string; text: string; submitLabel: string; danger: boolean }) {
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
            {title}
          </Text>
          <Text size="sm" c="dimmed">
            {text}
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
          <Button type="submit" color={danger ? 'red' : undefined} loading={submitting} disabled={disabled}>
            {submitLabel}
          </Button>
          <Button variant="subtle" onClick={onCancel} disabled={submitting}>
            Cancelar
          </Button>
        </Group>
      </Stack>
    </form>
  );
}
