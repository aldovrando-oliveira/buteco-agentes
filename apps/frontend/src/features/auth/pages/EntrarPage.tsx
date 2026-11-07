import { useEffect, useState } from 'react';
import { Alert, Stack, Title, VisuallyHidden } from '@mantine/core';
import { useLocation, useSearchParams } from 'react-router';
import { Logo } from '../../../components/brand/Logo';
import { hasSecureContextApis } from '../../../auth/secureContext';
import { authRuntime } from '../../../auth/session';
import { createSession, submitSecondFactor } from '../api/sessionApi';
import type { SessionInput } from '../api/sessionApi';
import { EntrarForm } from '../components/EntrarForm';
import { EntryCard } from '../components/EntryCard';
import { SecondFactorForm, type SecondFactorNotice } from '../components/SecondFactorForm';
import { continueEntry } from '../continueEntry';
import {
  blocksUntilRetry,
  entryNotice,
  INCOMPLETO,
  lockMinutes,
  lockText,
  readEntrarState,
  type EntryFailure,
  type EntryNotice,
} from '../entryNotices';
import { CODIGO_INVALIDO, ENTRADA_EXPIROU, FALHA_NA_ENTRADA, INICIANDO, SEM_CONEXAO } from '../messages';

type Etapa = 'credenciais' | 'codigo';

/** O aviso do passo do código para cada recusa do `POST /auth/session/second-factor` (D1). */
function secondFactorNotice(result: EntryFailure): SecondFactorNotice {
  if (result.kind === 'network') return { tipo: 'erro', texto: SEM_CONEXAO };
  switch (result.status) {
    case 423:
      return { tipo: 'bloqueado', minutos: lockMinutes(result.retryAfter) };
    case 429:
      return { tipo: 'muitas-tentativas' };
    case 503:
      return { tipo: 'erro', texto: INICIANDO };
    default:
      // O 500 da chave trocada traz o texto para a pessoa no `title` (#218).
      return { tipo: 'erro', texto: result.title ?? 'Não foi possível conferir o código. Tente de novo.' };
  }
}

export function EntrarPage() {
  const [params] = useSearchParams();
  const location = useLocation();
  // O `state` com que as páginas do link abrem o /entrar: lido uma vez, na montagem.
  const [vindoDoLink] = useState(() => readEntrarState(location.state));
  const [etapa, setEtapa] = useState<Etapa>(vindoDoLink && 'etapa' in vindoDoLink ? 'codigo' : 'credenciais');
  const [aviso, setAviso] = useState<EntryNotice>(
    vindoDoLink && 'falha' in vindoDoLink ? entryNotice(vindoDoLink.falha) : null,
  );
  const [expirou, setExpirou] = useState(false);
  const [avisoDoCodigo, setAvisoDoCodigo] = useState<SecondFactorNotice>(
    vindoDoLink && 'etapa' in vindoDoLink ? { tipo: 'senha-salva' } : null,
  );
  const [erroDoCodigo, setErroDoCodigo] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [bloqueadoAte, setBloqueadoAte] = useState<number | null>(() =>
    vindoDoLink && 'falha' in vindoDoLink && blocksUntilRetry(vindoDoLink.falha) && vindoDoLink.falha.kind === 'http'
      ? Date.now() + (vindoDoLink.falha.retryAfter ?? 1) * 1000
      : null,
  );
  const [seguro] = useState(hasSecureContextApis);
  const motivo = params.get('motivo');
  const sessaoExpirada = motivo === 'sessao-expirada';

  // Depois de 429 e 503, o botão volta quando passa o Retry-After; nada é reenviado
  // sozinho, porque é a senha (ou o código) da pessoa.
  useEffect(() => {
    if (bloqueadoAte === null) return;
    const timer = setTimeout(() => setBloqueadoAte(null), Math.max(0, bloqueadoAte - Date.now()));
    return () => clearTimeout(timer);
  }, [bloqueadoAte]);

  const seguir = async () => {
    authRuntime.assign(await continueEntry(params));
  };

  const esperarRetryAfter = (result: EntryFailure) => {
    if (blocksUntilRetry(result) && result.kind === 'http') {
      setBloqueadoAte(Date.now() + (result.retryAfter ?? 1) * 1000);
    }
  };

  const handleSubmit = async (values: SessionInput) => {
    if (!seguro) return;
    setAviso(null);
    setExpirou(false);
    setEnviando(true);
    const result = await createSession(values);

    if (result.kind === 'ok') {
      await seguir();
      return;
    }

    setEnviando(false);
    if (result.kind === 'segundo-passo') {
      setAvisoDoCodigo(null);
      setErroDoCodigo(null);
      setEtapa('codigo');
      return;
    }
    setAviso(entryNotice(result));
    esperarRetryAfter(result);
  };

  const handleCode = async (codigo: string) => {
    setAvisoDoCodigo(null);
    setErroDoCodigo(null);
    setEnviando(true);
    const result = await submitSecondFactor(codigo);

    if (result.kind === 'ok') {
      await seguir();
      return;
    }

    setEnviando(false);
    if (result.kind === 'http' && result.status === 401) {
      // Só o `title` separa os dois 401, e o "expirou" é reconhecido por igualdade com o
      // texto do contrato (decisão do dono, Q1; a #247 troca por um campo de tipo).
      if (result.title === ENTRADA_EXPIROU) {
        setEtapa('credenciais');
        setExpirou(true);
        return;
      }
      setErroDoCodigo(CODIGO_INVALIDO);
      return;
    }
    setAvisoDoCodigo(secondFactorNotice(result));
    esperarRetryAfter(result);
  };

  return (
    <EntryCard footer="Ainda não tem acesso? Peça um convite ao administrador do painel.">
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

        {etapa === 'codigo' ? (
          <SecondFactorForm
            onSubmit={(codigo) => void handleCode(codigo)}
            onBack={() => {
              setAvisoDoCodigo(null);
              setErroDoCodigo(null);
              setEtapa('credenciais');
            }}
            notice={avisoDoCodigo}
            codeError={erroDoCodigo}
            submitting={enviando}
            disabled={bloqueadoAte !== null}
          />
        ) : (
          <>
            {aviso?.tipo === 'bloqueado' && (
              <Alert color="yellow" role="alert">
                {lockText(aviso.minutos)}
              </Alert>
            )}
            {aviso?.tipo === 'erro' && (
              <Alert color="red" role="alert">
                {aviso.texto}
              </Alert>
            )}
            {expirou && aviso === null && (
              <Alert color="blue" role="status">
                {ENTRADA_EXPIROU}
              </Alert>
            )}
            {motivo === 'falha-na-entrada' && aviso === null && !expirou && (
              <Alert color="red" role="alert">
                {FALHA_NA_ENTRADA}
              </Alert>
            )}
            {sessaoExpirada && aviso === null && !expirou && (
              <Alert color="blue" role="status">
                Sua sessão expirou. Entre novamente para continuar de onde parou.
              </Alert>
            )}

            <EntrarForm
              onSubmit={(values) => void handleSubmit(values)}
              onIncomplete={() => setAviso({ tipo: 'erro', texto: INCOMPLETO })}
              initialEmail={vindoDoLink && 'email' in vindoDoLink ? vindoDoLink.email : undefined}
              submitting={enviando}
              disabled={bloqueadoAte !== null}
            />
          </>
        )}
      </Stack>
    </EntryCard>
  );
}
