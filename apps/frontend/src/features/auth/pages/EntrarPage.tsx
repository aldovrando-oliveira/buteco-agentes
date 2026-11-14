import { useCallback, useEffect, useState } from 'react';
import { Alert, Stack, Title, VisuallyHidden } from '@mantine/core';
import { useLocation, useNavigate, useSearchParams } from 'react-router';
import { Logo } from '../../../components/brand/Logo';
import { hasSecureContextApis } from '../../../auth/secureContext';
import { authRuntime } from '../../../auth/session';
import { createSession, submitSecondFactor } from '../api/sessionApi';
import type { SessionInput } from '../api/sessionApi';
import { classify404, startFederatedLogin, type LoginProvider } from '../api/federatedApi';
import { followableAuthorizeReturn } from '../../../auth/loginFlow';
import { FederatedAlert, ProviderButtons, RedirectingNotice, TextDivider } from '../components/FederatedParts';
import { useLoginProviders, useRefusalEmail } from '../federatedHooks';
import {
  ENTRAR_COM,
  ENTRAR_COM_OUTRA_CONTA,
  LOGIN_OTHER_ACCOUNT,
  loginRefusalNotice,
  OU_ENTRE_COM,
  startFailureNotice,
  type FederatedNotice,
} from '../federatedNotices';
import { readFederatedReturn, withoutReturnParams } from '../federatedReturn';
import { clearLinkToken } from '../linkToken';
import { EnrollmentForm } from '../components/EnrollmentForm';
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
import { CODIGO_INVALIDO, ENTRADA_EXPIROU, FALHA_NA_ENTRADA, INICIANDO, SEM_CONEXAO, SENHA_ALTERADA } from '../messages';
import { clearPasswordChanged, hasPasswordChanged } from '../passwordChangedNotice';

type Etapa = 'credenciais' | 'codigo' | 'cadastro';

/** O logout do emissor sem hint: o "Voltar" da etapa do cadastro encerra o cookie pendente no servidor (D3). */
const LOGOUT_SEM_HINT = '/connect/logout';

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
  const navigate = useNavigate();
  // O `state` com que as páginas do link abrem o /entrar: lido uma vez, na montagem.
  const [vindoDoLink] = useState(() => readEntrarState(location.state));
  // A volta do provedor (federado, segundoPasso, convite, recusa), lida uma vez, na montagem. Com o
  // convite aceito, a chave do token sai antes de o cadastro abrir (D1; contrato da #274).
  const [volta] = useState(() => {
    const lida = readFederatedReturn(params);
    if (lida.conviteAceito) clearLinkToken('convite');
    return lida;
  });
  const [etapa, setEtapa] = useState<Etapa>(
    vindoDoLink && 'etapa' in vindoDoLink ? vindoDoLink.etapa : (volta.etapa ?? 'credenciais'),
  );
  const [cadastroDoLink, setCadastroDoLink] = useState(vindoDoLink !== null && 'etapa' in vindoDoLink && vindoDoLink.etapa === 'cadastro');
  // O navegador guarda o `state` do router no histórico: o da etapa do cadastro é limpo ao ser
  // lido, para recarregar a página voltar a e-mail e senha (decisão do dono, 08/11/2026). Os
  // outros dois `state` ficam como estão (design.md da change verificacao-2-etapas-obrigatoria, D7).
  useEffect(() => {
    if (vindoDoLink && 'etapa' in vindoDoLink && vindoDoLink.etapa === 'cadastro') {
      void navigate(`${location.pathname}${location.search}`, { replace: true });
    }
    // Só na montagem: o `state` é lido uma vez.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  const [aviso, setAviso] = useState<EntryNotice>(
    vindoDoLink && 'falha' in vindoDoLink ? entryNotice(vindoDoLink.falha) : null,
  );
  const [expirou, setExpirou] = useState(false);
  const [avisoDoCodigo, setAvisoDoCodigo] = useState<SecondFactorNotice>(
    vindoDoLink && 'etapa' in vindoDoLink && vindoDoLink.etapa === 'codigo'
      ? { tipo: 'senha-salva' }
      : volta.etapa === 'codigo' && volta.aviso
        ? { tipo: 'confirmado', texto: volta.aviso }
        : null,
  );
  const [erroDoCodigo, setErroDoCodigo] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [bloqueadoAte, setBloqueadoAte] = useState<number | null>(() =>
    vindoDoLink && 'falha' in vindoDoLink && blocksUntilRetry(vindoDoLink.falha) && vindoDoLink.falha.kind === 'http'
      ? Date.now() + (vindoDoLink.falha.retryAfter ?? 1) * 1000
      : null,
  );
  const [seguro] = useState(hasSecureContextApis);
  // Os botões dos provedores, só no contexto seguro (como o formulário); a falha é a lista vazia (Q5).
  const { providers, reload: reloadProviders } = useLoginProviders(seguro);
  const [redirecionando, setRedirecionando] = useState<string | null>(null);
  const [avisoDoInicio, setAvisoDoInicio] = useState<FederatedNotice | null>(null);
  // A recusa da volta: o e-mail confirmado só no sem-acesso, lido uma vez (D2).
  const [recusaVisivel, setRecusaVisivel] = useState(volta.recusa !== null);
  const refusal = useRefusalEmail(volta.recusa === 'sem-acesso', { motivo: 'sem-acesso', provedor: volta.provedor });
  const avisoDaRecusa =
    recusaVisivel && volta.recusa !== null && refusal.lido ? loginRefusalNotice(volta.recusa, refusal.email) : null;

  // Depois de ler a volta, o endereço perde os parâmetros dela, e o `retorno` fica (Q4).
  useEffect(() => {
    if (volta.sujo) void navigate(`${location.pathname}${withoutReturnParams(params)}`, { replace: true });
    // Só na montagem: a volta é lida uma vez.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  // Depois da troca da senha em Minha conta: lido na montagem e apagado no efeito abaixo
  // (D3 da change minha-conta-painel). Vence o "sessão expirada" durante toda a página: uma
  // renovação em voo na troca pode chegar aqui com `motivo=sessao-expirada`.
  const [daTrocaDeSenha] = useState(hasPasswordChanged);
  const [senhaAlterada, setSenhaAlterada] = useState(daTrocaDeSenha);
  useEffect(() => {
    clearPasswordChanged();
  }, []);
  const motivo = params.get('motivo');
  const sessaoExpirada = motivo === 'sessao-expirada' && !daTrocaDeSenha;

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

  const entrarComProvedor = async (provider: LoginProvider) => {
    setAviso(null);
    setAvisoDoInicio(null);
    setRecusaVisivel(false);
    setRedirecionando(provider.nome);
    const result = await startFederatedLogin(provider.chave, followableAuthorizeReturn(params.get('retorno')));
    if (result.kind === 'ok') {
      authRuntime.assign(result.url);
      return;
    }
    setRedirecionando(null);
    setAvisoDoInicio(startFailureNotice(result));
    if (result.kind === 'http' && result.status === 404 && classify404(result) === 'provider-not-configured') {
      reloadProviders();
    }
  };

  const handleSubmit = async (values: SessionInput) => {
    if (!seguro) return;
    setAviso(null);
    setAvisoDoInicio(null);
    setRecusaVisivel(false);
    setSenhaAlterada(false);
    setExpirou(false);
    setEnviando(true);
    const result = await createSession(values);

    setEnviando(false);
    if (result.kind === 'segundo-passo') {
      setAvisoDoCodigo(null);
      setErroDoCodigo(null);
      setCadastroDoLink(false);
      setEtapa(result.passo === 'cadastro' ? 'cadastro' : 'codigo');
      return;
    }
    setAviso(entryNotice(result));
    esperarRetryAfter(result);
  };

  const voltarExpirado = useCallback(() => {
    setEtapa('credenciais');
    setExpirou(true);
  }, []);

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

        {etapa === 'cadastro' ? (
          <EnrollmentForm
            vindoDoLink={cadastroDoLink}
            avisoVerde={volta.etapa === 'cadastro' && !cadastroDoLink ? (volta.aviso ?? undefined) : undefined}
            onExpired={voltarExpirado}
            onDone={() => void seguir()}
            onBack={() => authRuntime.assign(LOGOUT_SEM_HINT)}
          />
        ) : etapa === 'codigo' ? (
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
        ) : redirecionando !== null ? (
          <RedirectingNotice provedor={redirecionando} />
        ) : (
          <>
            {avisoDoInicio && <FederatedAlert notice={avisoDoInicio} />}
            {avisoDaRecusa && !avisoDoInicio && <FederatedAlert notice={avisoDaRecusa} />}
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
            {senhaAlterada && aviso === null && !expirou && (
              <Alert color="green" role="status">
                {SENHA_ALTERADA}
              </Alert>
            )}
            {sessaoExpirada && aviso === null && !expirou && (
              <Alert color="blue" role="status">
                Sua sessão expirou. Entre novamente para continuar de onde parou.
              </Alert>
            )}

            <EntrarForm
              onSubmit={(values) => void handleSubmit(values)}
              onIncomplete={() => {
                setSenhaAlterada(false);
                setAviso({ tipo: 'erro', texto: INCOMPLETO });
              }}
              initialEmail={vindoDoLink && 'email' in vindoDoLink ? vindoDoLink.email : undefined}
              submitting={enviando}
              disabled={bloqueadoAte !== null}
            />

            {providers.length > 0 && (
              <Stack gap="sm">
                <TextDivider label={OU_ENTRE_COM} />
                <ProviderButtons
                  providers={providers}
                  label={(provider) =>
                    recusaVisivel && volta.recusa !== null && LOGIN_OTHER_ACCOUNT.has(volta.recusa)
                      ? ENTRAR_COM_OUTRA_CONTA(provider.nome)
                      : ENTRAR_COM(provider.nome)
                  }
                  onPick={(provider) => void entrarComProvedor(provider)}
                  disabled={enviando}
                />
              </Stack>
            )}
          </>
        )}
      </Stack>
    </EntryCard>
  );
}
