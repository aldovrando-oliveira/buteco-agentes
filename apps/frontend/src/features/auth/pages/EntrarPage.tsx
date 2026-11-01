import { useEffect, useState } from 'react';
import { Alert, Center, Paper, Stack, Title, VisuallyHidden } from '@mantine/core';
import { useSearchParams } from 'react-router';
import { Logo } from '../../../components/brand/Logo';
import { beginLogin, followableAuthorizeReturn, safeReturnPath } from '../../../auth/loginFlow';
import { hasSecureContextApis } from '../../../auth/secureContext';
import { authRuntime } from '../../../auth/session';
import { createSession } from '../api/sessionApi';
import type { SessionInput, SessionResult } from '../api/sessionApi';
import { EntrarForm } from '../components/EntrarForm';
import { FALHA_NA_ENTRADA, INICIANDO } from '../messages';

type Aviso =
  | { tipo: 'erro'; texto: string }
  | { tipo: 'bloqueado'; minutos: number | null }
  | null;

const INCOMPLETO = 'Informe e-mail e senha.';

/** O texto de cada resposta do `POST /auth/session` (spec `operator-login-ui`). */
function avisoDe(result: Exclude<SessionResult, { kind: 'ok' }>): Aviso {
  if (result.kind === 'network') return { tipo: 'erro', texto: 'Não foi possível falar com o servidor.' };
  switch (result.status) {
    case 400:
      return { tipo: 'erro', texto: result.title ?? INCOMPLETO };
    case 401:
      return { tipo: 'erro', texto: result.title ?? 'E-mail ou senha inválidos.' };
    case 403:
      // "Acesso desativado…" ou "Origem não permitida.": os dois são texto para a
      // pessoa, e o painel não precisa distinguir (D6).
      return { tipo: 'erro', texto: result.title ?? 'Acesso recusado.' };
    case 423:
      // O protótipo diz "5 minutos", mas o bloqueio dobra até 60 min: N sai do
      // Retry-After, arredondado para cima (convenção 17).
      return {
        tipo: 'bloqueado',
        minutos: result.retryAfter === undefined ? null : Math.max(1, Math.ceil(result.retryAfter / 60)),
      };
    case 429:
      return { tipo: 'erro', texto: 'Muitas tentativas ao mesmo tempo. Tente de novo em instantes.' };
    case 503:
      return { tipo: 'erro', texto: INICIANDO };
    default:
      return { tipo: 'erro', texto: 'Não foi possível entrar. Tente de novo.' };
  }
}

export function EntrarPage() {
  const [params] = useSearchParams();
  const [aviso, setAviso] = useState<Aviso>(null);
  const [enviando, setEnviando] = useState(false);
  const [bloqueadoAte, setBloqueadoAte] = useState<number | null>(null);
  const [seguro] = useState(hasSecureContextApis);
  const motivo = params.get('motivo');
  const sessaoExpirada = motivo === 'sessao-expirada';

  // Depois de 429 e 503, o botão volta quando passa o Retry-After; nada é reenviado
  // sozinho, porque é a senha da pessoa.
  useEffect(() => {
    if (bloqueadoAte === null) return;
    const timer = setTimeout(() => setBloqueadoAte(null), Math.max(0, bloqueadoAte - Date.now()));
    return () => clearTimeout(timer);
  }, [bloqueadoAte]);

  const handleSubmit = async (values: SessionInput) => {
    if (!seguro) return;
    setAviso(null);
    setEnviando(true);
    const result = await createSession(values);

    if (result.kind === 'ok') {
      // Com `retorno` (o authorize mandou para cá), segue exatamente a string
      // conferida. Fora da regra, o fluxo recomeça e termina na tela inicial: o
      // cookie recém-gravado devolve o código sem formulário (D6).
      const retorno = params.get('retorno');
      const seguir = followableAuthorizeReturn(retorno);
      const destino =
        seguir ??
        (await beginLogin(authRuntime, retorno === null ? safeReturnPath(params.get('voltarPara')) : '/'));
      authRuntime.assign(destino);
      return;
    }

    setEnviando(false);
    setAviso(avisoDe(result));
    if (result.kind === 'http' && (result.status === 429 || result.status === 503)) {
      setBloqueadoAte(Date.now() + (result.retryAfter ?? 1) * 1000);
    }
  };

  return (
    <Center mih="100vh" p="md">
      <Paper withBorder shadow="sm" p="xl" w="100%" maw={380}>
        <Stack gap="lg">
          {/* O lockup é a assinatura da marca, e o <Title> guarda só a semântica e o
              nome acessível, anunciado uma vez (design.md da change
              frontend-marca-visual, D11). */}
          <Stack align="center" gap={0}>
            <Title order={2}>
              <Logo variant="vertical" size={110} />
              <VisuallyHidden>Buteco Agentes</VisuallyHidden>
            </Title>
          </Stack>

          {!seguro && <Alert color="red">O painel precisa ser aberto por HTTPS.</Alert>}
          {aviso?.tipo === 'bloqueado' && (
            <Alert color="yellow" role="alert">
              Muitas tentativas sem sucesso. Por segurança, o acesso desta conta fica pausado por{' '}
              {aviso.minutos === null
                ? 'alguns minutos'
                : `${aviso.minutos} ${aviso.minutos === 1 ? 'minuto' : 'minutos'}`}
              .
            </Alert>
          )}
          {aviso?.tipo === 'erro' && (
            <Alert color="red" role="alert">
              {aviso.texto}
            </Alert>
          )}
          {motivo === 'falha-na-entrada' && aviso === null && (
            <Alert color="red" role="alert">
              {FALHA_NA_ENTRADA}
            </Alert>
          )}
          {sessaoExpirada && aviso === null && (
            <Alert color="blue" role="status">
              Sua sessão expirou. Entre novamente para continuar de onde parou.
            </Alert>
          )}

          <EntrarForm
            onSubmit={(values) => void handleSubmit(values)}
            onIncomplete={() => setAviso({ tipo: 'erro', texto: INCOMPLETO })}
            submitting={enviando}
            disabled={bloqueadoAte !== null}
          />
        </Stack>
      </Paper>
    </Center>
  );
}
