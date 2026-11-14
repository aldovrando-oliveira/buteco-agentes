import { useEffect, useRef, useState } from 'react';
import { Alert, Anchor, Badge, Button, Group, Loader, Stack, Text, TextInput } from '@mantine/core';
import {
  confirmEnrollment,
  startEnrollment,
  type EnrollmentConfirmResult,
  type EnrollmentStartResult,
} from '../api/sessionApi';
import { lockMinutes, lockText } from '../entryNotices';
import {
  CADASTRO_TEXTO,
  CADASTRO_TITULO,
  CODIGO_INVALIDO_NO_CADASTRO,
  ENTRADA_EXPIROU,
  GERANDO_QR,
  INFORME_O_CODIGO,
  INICIANDO,
  MUITAS_TENTATIVAS,
  PRAZO_DO_CADASTRO,
  QR_FALHOU,
  SEM_CONEXAO,
  SENHA_SALVA_COM_CADASTRO,
  VERIFICACAO_LIGADA,
} from '../messages';
import { RecoveryCodes } from '../../two-factor/components/RecoveryCodes';
import { TwoFactorQr } from '../../two-factor/components/TwoFactorQr';

// A etapa do cadastro das 2 etapas no /entrar, do quadro `Main` (estados `cadastro*`; spec
// `operator-login-ui`, "Etapa do cadastro da verificação no /entrar"; design.md da change
// verificacao-2-etapas-obrigatoria, D1 e D7).
//
// - ao montar, chama o `setup` sem token: o cookie pendente da senha prova quem é. "Tentar de
//   novo" chama de novo, e o servidor troca o segredo pendente;
// - o segredo, o `otpauthUri` e os códigos ficam só no estado deste componente: nada no storage,
//   na URL, no `state` do router nem no cache do TanStack Query. As chamadas são diretas, sem
//   `useMutation`, como em Minha conta (design.md da change minha-conta-painel, D5);
// - a falha do `setup` mostra o `cadastro-qr-falhou`; os avisos de `429`, `503` e rede são do
//   `confirm` (a leitura do quadro, D7). O `401` "A entrada expirou…" das duas volta às credenciais.

type Qr =
  | { tipo: 'carregando' }
  | { tipo: 'pronto'; segredo: string; otpauthUri: string }
  | { tipo: 'falhou' };

type Aviso =
  | { tipo: 'senha-salva' }
  | { tipo: 'verde'; texto: string }
  | { tipo: 'bloqueado'; minutos: number | null }
  | { tipo: 'muitas-tentativas' }
  | { tipo: 'erro'; texto: string }
  | null;

interface EnrollmentFormProps {
  /** Vindo das páginas do link, depois da senha criada: o aviso `cadastro-apos-nova-senha`. */
  vindoDoLink?: boolean;
  /**
   * O aviso verde da volta do provedor: "O Google confirmou…" ou "Convite aceito…" (design.md da
   * change telas-login-google, D1). Fica até o fim do cadastro, como o da senha salva.
   */
  avisoVerde?: string;
  /** O `401` "A entrada expirou…": a página volta a e-mail e senha com o aviso. */
  onExpired: () => void;
  /** "Continuar" depois dos códigos: a sessão está completa, e a página segue o fluxo do emissor. */
  onDone: () => void;
  /** "Voltar": a página encerra o cookie pendente no servidor, pelo logout. */
  onBack: () => void;
}

/** O e-mail do rótulo do `otpauthUri` (`Buteco%20Agentes:{e-mail}`), para o cabeçalho do .txt. */
function emailFromUri(otpauthUri: string): string {
  const label = otpauthUri.replace(/^otpauth:\/\/totp\//, '').split('?')[0] ?? '';
  const decoded = decodeURIComponent(label);
  return decoded.includes(':') ? decoded.slice(decoded.indexOf(':') + 1) : decoded;
}

function retryAfterMs(result: { kind: string; status?: number; retryAfter?: number }): number | null {
  return result.kind === 'http' && (result.status === 429 || result.status === 503 || result.status === 423)
    ? (result.retryAfter ?? 1) * 1000
    : null;
}

export function EnrollmentForm({ vindoDoLink, avisoVerde, onExpired, onDone, onBack }: EnrollmentFormProps) {
  const [tentativa, setTentativa] = useState(0);
  const [qr, setQr] = useState<Qr>({ tipo: 'carregando' });
  const [aviso, setAviso] = useState<Aviso>(
    vindoDoLink ? { tipo: 'senha-salva' } : avisoVerde ? { tipo: 'verde', texto: avisoVerde } : null,
  );
  const [codigo, setCodigo] = useState('');
  const [erroDoCodigo, setErroDoCodigo] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [bloqueadoAte, setBloqueadoAte] = useState<number | null>(null);
  const [codigos, setCodigos] = useState<{ lista: string[]; email: string } | null>(null);
  const email = qr.tipo === 'pronto' ? emailFromUri(qr.otpauthUri) : '';

  // Um `setup` por tentativa. No StrictMode do desenvolvimento, o efeito roda duas vezes na
  // montagem: a mesma promessa serve às duas, para não gravar dois segredos e mostrar o primeiro.
  const pedido = useRef<{ tentativa: number; promessa: Promise<EnrollmentStartResult> } | null>(null);
  const expirou = useRef(onExpired);
  useEffect(() => {
    expirou.current = onExpired;
  }, [onExpired]);
  useEffect(() => {
    if (pedido.current?.tentativa !== tentativa) {
      pedido.current = { tentativa, promessa: startEnrollment() };
    }
    let vivo = true;
    void pedido.current.promessa.then((result) => {
      if (!vivo) return;
      if (result.kind === 'ok') {
        setQr({ tipo: 'pronto', segredo: result.segredo, otpauthUri: result.otpauthUri });
        return;
      }
      if (result.kind === 'http' && result.status === 401 && result.title === ENTRADA_EXPIROU) {
        expirou.current();
        return;
      }
      setQr({ tipo: 'falhou' });
      if (result.kind === 'http' && result.status === 423) {
        setAviso({ tipo: 'bloqueado', minutos: lockMinutes(result.retryAfter) });
      }
      const espera = retryAfterMs(result);
      if (espera !== null) setBloqueadoAte(Date.now() + espera);
    });
    return () => {
      vivo = false;
    };
  }, [tentativa]);

  // Depois de 423, 429 e 503, o botão volta quando passa o Retry-After; nada é reenviado sozinho.
  useEffect(() => {
    if (bloqueadoAte === null) return;
    const timer = setTimeout(() => setBloqueadoAte(null), Math.max(0, bloqueadoAte - Date.now()));
    return () => clearTimeout(timer);
  }, [bloqueadoAte]);

  const confirmar = async (valor: string) => {
    setEnviando(true);
    setErroDoCodigo(null);
    if (aviso?.tipo !== 'senha-salva' && aviso?.tipo !== 'verde') setAviso(null);
    const result: EnrollmentConfirmResult = await confirmEnrollment(valor);
    setEnviando(false);

    if (result.kind === 'ok') {
      setCodigos({ lista: result.codigosRecuperacao, email });
      setQr({ tipo: 'carregando' });
      setCodigo('');
      return;
    }
    if (result.kind === 'network') {
      setAviso({ tipo: 'erro', texto: SEM_CONEXAO });
      return;
    }
    switch (result.status) {
      case 401:
        // Só o `title` separa os dois 401, por igualdade com o texto do contrato (#245 D1, Q1).
        if (result.title === ENTRADA_EXPIROU) {
          onExpired();
          return;
        }
        setErroDoCodigo(CODIGO_INVALIDO_NO_CADASTRO);
        return;
      case 423:
        setAviso({ tipo: 'bloqueado', minutos: lockMinutes(result.retryAfter) });
        break;
      case 429:
        setAviso({ tipo: 'muitas-tentativas' });
        break;
      case 503:
        setAviso({ tipo: 'erro', texto: INICIANDO });
        break;
      default:
        setAviso({ tipo: 'erro', texto: result.title ?? 'Não foi possível conferir o código. Tente de novo.' });
    }
    const espera = retryAfterMs(result);
    if (espera !== null) setBloqueadoAte(Date.now() + espera);
  };

  if (codigos !== null) {
    return (
      <RecoveryCodes
        codigos={codigos.lista}
        email={codigos.email}
        titulo={VERIFICACAO_LIGADA}
        downloadLabel="Baixar .txt"
        doneLabel="Continuar"
        badge={false}
        onDone={onDone}
      />
    );
  }

  const semQr = qr.tipo !== 'pronto';

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        const valor = codigo.trim();
        if (!valor) {
          setErroDoCodigo(INFORME_O_CODIGO);
          return;
        }
        void confirmar(valor);
      }}
      noValidate
    >
      <Stack gap="md">
        <Stack gap={4} align="center" ta="center">
          <Text fw={600} fz={15}>
            {CADASTRO_TITULO}
          </Text>
          <Text c="dimmed" fz="sm">
            {CADASTRO_TEXTO}
          </Text>
        </Stack>

        {aviso?.tipo === 'senha-salva' && (
          <Alert color="green" role="status">
            {SENHA_SALVA_COM_CADASTRO}
          </Alert>
        )}
        {aviso?.tipo === 'verde' && (
          <Alert color="green" role="status">
            {aviso.texto}
          </Alert>
        )}
        {aviso?.tipo === 'bloqueado' && (
          <Alert color="yellow" role="alert">
            {lockText(aviso.minutos)}
          </Alert>
        )}
        {aviso?.tipo === 'muitas-tentativas' && (
          <Alert color="yellow" role="alert">
            {MUITAS_TENTATIVAS}
          </Alert>
        )}
        {aviso?.tipo === 'erro' && (
          <Alert color="red" role="alert">
            {aviso.texto}
          </Alert>
        )}

        <Stack gap="xs">
          <Group gap="xs">
            <Badge circle variant="light" aria-hidden>
              1
            </Badge>
            <Text fw={500} size="sm">
              Escaneie o QR com o app
            </Text>
          </Group>
          {qr.tipo === 'pronto' && <TwoFactorQr segredo={qr.segredo} otpauthUri={qr.otpauthUri} />}
          {qr.tipo === 'carregando' && (
            <Group gap="xs" role="status" justify="center">
              <Loader size="xs" />
              <Text size="sm">{GERANDO_QR}</Text>
            </Group>
          )}
          {qr.tipo === 'falhou' && (
            <Group gap="sm" role="alert" justify="center">
              <Text size="sm">{QR_FALHOU}</Text>
              <Button
                size="xs"
                variant="default"
                disabled={bloqueadoAte !== null}
                onClick={() => {
                  setQr({ tipo: 'carregando' });
                  setTentativa((atual) => atual + 1);
                }}
              >
                Tentar de novo
              </Button>
            </Group>
          )}
        </Stack>

        <TextInput
          label={
            <Group gap="xs" component="span">
              <Badge circle variant="light" aria-hidden component="span">
                2
              </Badge>
              <span>Digite o código de 6 dígitos que o app mostra</span>
            </Group>
          }
          value={codigo}
          onChange={(event) => setCodigo(event.currentTarget.value)}
          inputMode="numeric"
          autoComplete="one-time-code"
          placeholder="000 000"
          disabled={semQr}
          error={erroDoCodigo}
          description={PRAZO_DO_CADASTRO}
          inputWrapperOrder={['label', 'input', 'error', 'description']}
          styles={{
            input: {
              textAlign: 'center',
              letterSpacing: '0.2em',
              fontFamily: 'var(--mantine-font-family-monospace)',
              fontSize: 16,
            },
          }}
        />

        <Button type="submit" fullWidth loading={enviando} disabled={semQr || bloqueadoAte !== null}>
          Confirmar
        </Button>

        <Group justify="flex-start" fz="xs">
          <Anchor component="button" type="button" fz="xs" onClick={onBack}>
            Voltar
          </Anchor>
        </Group>
      </Stack>
    </form>
  );
}
