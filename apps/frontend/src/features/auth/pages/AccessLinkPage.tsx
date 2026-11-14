import { useEffect, useState, type ReactNode } from 'react';
import { Alert, Stack, Text, Title } from '@mantine/core';
import { useLocation, useNavigate } from 'react-router';
import { authRuntime } from '../../../auth/session';
import { classify404, startFederatedInvitation, type LoginProvider } from '../api/federatedApi';
import { FederatedAlert, ProviderButtons, RedirectingNotice, TextDivider } from '../components/FederatedParts';
import { useLoginProviders, useRefusalEmail } from '../federatedHooks';
import {
  A_CONTA_PRECISA_TER,
  ABRA_O_LINK_DE_NOVO,
  CONTINUAR_COM,
  ENTRE_COM_UMA_CONTA,
  invitationRefusalNotice,
  OU_CRIE_UMA_SENHA,
  startFailureNotice,
  type FederatedNotice,
} from '../federatedNotices';
import { Logo } from '../../../components/brand/Logo';
import { completeLink, lookupLink, type LinkDetails, type LinkPurpose } from '../api/accessLinkApi';
import { createSession } from '../api/sessionApi';
import { EntryCard } from '../components/EntryCard';
import { InvalidLinkCard, LoadingLinkCard, UnavailableLinkCard } from '../components/LinkStateCards';
import { NewPasswordForm } from '../components/NewPasswordForm';
import { formatLinkExpiry } from '../linkExpiry';
import type { EntrarRouteState } from '../entryNotices';
import { clearLinkToken, storeLinkToken, useLinkToken } from '../linkToken';
import { ENVIO_INDISPONIVEL, MUITAS_TENTATIVAS } from '../messages';

// /convite e /redefinir-senha (spec `panel-access-link-pages`; design.md da change
// entrada-publica-painel, D3 a D5). O aceite e a conclusão respondem `204` sem sessão
// (#181 D6, P5), e a página entra em seguida com o e-mail da consulta e a senha digitada.

interface PageTexts {
  heading: (link: LinkDetails) => string;
  intro: (link: LinkDetails) => ReactNode;
  /** O cabeçalho do convite com o provedor: sem o "Crie sua senha para entrar." (quadro `AceitarConvite`). */
  introComProvedor?: (link: LinkDetails) => ReactNode;
  passwordLabel: string;
  submitLabel: string;
  notice?: ReactNode;
  footer: (validade: string) => string;
  invalid: string;
}

const TEXTS: Record<LinkPurpose, PageTexts> = {
  convite: {
    heading: (link) => `Olá, ${link.nome}`,
    intro: (link) => (
      <>
        {link.convidadoPor} convidou você para o <b>Buteco Agentes</b>. Crie sua senha para entrar.
      </>
    ),
    introComProvedor: (link) => (
      <>
        {link.convidadoPor} convidou você para o <b>Buteco Agentes</b>.
      </>
    ),
    passwordLabel: 'Senha',
    submitLabel: 'Criar senha e entrar',
    footer: (validade) => `Este convite vale até ${validade} e só pode ser usado uma vez.`,
    invalid:
      'O convite pode ter vencido, já ter sido usado ou ter sido cancelado. Peça um novo link a quem convidou você.',
  },
  'redefinir-senha': {
    heading: () => 'Criar nova senha',
    intro: (link) => (
      <>
        Olá, {link.nome}. Crie uma nova senha para entrar no <b>Buteco Agentes</b>.
      </>
    ),
    passwordLabel: 'Nova senha',
    submitLabel: 'Salvar senha e entrar',
    // Aviso fixo, e não alerta: o Alert do Mantine nasce com role="alert".
    notice: (
      <Alert color="blue" p="xs" fz="xs" role="note">
        Ao salvar, você sai de todos os navegadores em que estava conectado. Se a sua conta usa verificação em 2
        etapas, o código continua sendo pedido.
      </Alert>
    ),
    footer: (validade) => `Este link vale até ${validade} e só pode ser usado uma vez.`,
    invalid:
      'O link pode ter vencido, já ter sido usado, ou outro link pode ter sido gerado depois dele. Peça um novo a um administrador do painel.',
  },
};

type Fase =
  | { tipo: 'carregando' }
  | { tipo: 'indisponivel' }
  | { tipo: 'invalido' }
  /** A volta de uma recusa do provedor sem o token na aba: o aviso e "abra o link de novo", sem pedido (D3). */
  | { tipo: 'sem-token'; recusa: string }
  | { tipo: 'formulario'; link: LinkDetails };

type AvisoDoEnvio = 'muitas-tentativas' | 'envio-indisponivel' | null;

export function AccessLinkPage({ purpose }: { purpose: LinkPurpose }) {
  const token = useLinkToken(purpose);
  const navigate = useNavigate();
  const location = useLocation();
  const texts = TEXTS[purpose];
  // A recusa que volta ao convite (`/convite?recusa=…`), lida uma vez, na montagem (D3).
  const [recusa] = useState(() => (purpose === 'convite' ? new URLSearchParams(location.search).get('recusa') : null));
  const [provedorDaRecusa] = useState(() =>
    recusa === null ? null : new URLSearchParams(location.search).get('provedor'),
  );
  const [fase, setFase] = useState<Fase>(
    token ? { tipo: 'carregando' } : recusa !== null ? { tipo: 'sem-token', recusa } : { tipo: 'invalido' },
  );
  const comProvedor = purpose === 'convite' && fase.tipo === 'formulario';
  const { providers, reload: reloadProviders } = useLoginProviders(comProvedor);
  const [redirecionando, setRedirecionando] = useState<string | null>(null);
  const [avisoDoInicio, setAvisoDoInicio] = useState<FederatedNotice | null>(null);
  // O e-mail que o provedor confirmou, no outro-email, lido uma vez depois da consulta (D3). Aqui o
  // hook só liga quando a fase vira `formulario`, depois da consulta, nunca na montagem; e não religa,
  // porque a fase continua `formulario` depois de uma resposta recusada do início. A guarda de uso
  // único do hook (o `ref`, que o /entrar precisa contra o efeito duplo do `StrictMode`) fica para
  // protegê-lo de uma mudança futura que o faça religar: a segunda chamada responderia `404` e
  // apagaria o e-mail do aviso.
  const refusal = useRefusalEmail(comProvedor && recusa === 'outro-email', {
    motivo: 'outro-email',
    provedor: provedorDaRecusa,
  });
  // O aviso da recusa some no novo clique do provedor; a resposta do início mostra só o seu (D3).
  const [recusaVisivel, setRecusaVisivel] = useState(recusa !== null);
  const [tentativa, setTentativa] = useState(0);
  const [aviso, setAviso] = useState<AvisoDoEnvio>(null);
  const [erroDaSenha, setErroDaSenha] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [bloqueadoAte, setBloqueadoAte] = useState<number | null>(null);

  useEffect(() => {
    if (!token) return;
    let ativo = true;
    void lookupLink(purpose, token).then((result) => {
      if (!ativo) return;
      if (result.kind === 'ok') setFase({ tipo: 'formulario', link: result.link });
      else if (result.kind === 'http' && result.status === 404) {
        clearLinkToken(purpose);
        setFase({ tipo: 'invalido' });
      } else setFase({ tipo: 'indisponivel' });
    });
    return () => {
      ativo = false;
    };
  }, [purpose, token, tentativa]);

  // Depois de ler a recusa, o endereço perde `recusa` e `provedor` (Q4).
  useEffect(() => {
    if (recusa !== null) void navigate(location.pathname, { replace: true });
    // Só na montagem: a recusa é lida uma vez.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // A recusa apaga a chave depois da consulta (e, no outro-email, da leitura do aviso); o token que o
  // formulário e uma nova tentativa precisam fica no estado (contrato da #274).
  useEffect(() => {
    if (recusa !== null && fase.tipo === 'formulario' && (recusa !== 'outro-email' || refusal.lido)) {
      clearLinkToken('convite');
    }
  }, [recusa, fase.tipo, refusal.lido]);

  const continuarComProvedor = async (provider: LoginProvider) => {
    if (!token) return;
    setAviso(null);
    setAvisoDoInicio(null);
    setRecusaVisivel(false);
    setRedirecionando(provider.nome);
    const result = await startFederatedInvitation(provider.chave, token);
    if (result.kind === 'ok') {
      // A chave regravada com o token do estado antes da navegação, sempre (D3): uma segunda tentativa
      // depois de uma recusa ainda volta com o token.
      storeLinkToken('convite', token);
      authRuntime.assign(result.url);
      return;
    }
    setRedirecionando(null);
    if (result.kind === 'http' && result.status === 404) {
      const motivo = classify404(result);
      if (motivo === 'invalid-link') {
        clearLinkToken(purpose);
        setFase({ tipo: 'invalido' });
        return;
      }
      if (motivo === 'provider-not-configured') reloadProviders();
    }
    setAvisoDoInicio(startFailureNotice(result));
  };

  useEffect(() => {
    if (bloqueadoAte === null) return;
    const timer = setTimeout(() => setBloqueadoAte(null), Math.max(0, bloqueadoAte - Date.now()));
    return () => clearTimeout(timer);
  }, [bloqueadoAte]);

  const entrar = async (email: string, senha: string) => {
    const login = await createSession({ email, senha });
    // A senha não vai no `state` do router: o /entrar abre na etapa do cadastro (todo convidado
    // novo, e quem teve a verificação redefinida), no passo do código, ou com o e-mail e a recusa
    // do login (spec `panel-access-link-pages`; change verificacao-2-etapas-obrigatoria, D7). O
    // link não traz `voltarPara`: depois do segundo passo, o destino é a tela inicial.
    const state: EntrarRouteState =
      login.kind === 'segundo-passo'
        ? { etapa: login.passo === 'cadastro' ? 'cadastro' : 'codigo' }
        : { email, falha: login };
    navigate('/entrar', { replace: true, state });
  };

  const handleSubmit = async (link: LinkDetails, senha: string) => {
    if (!token) return;
    setAviso(null);
    setErroDaSenha(null);
    setEnviando(true);
    const result = await completeLink(purpose, token, senha);

    if (result.kind === 'ok') {
      clearLinkToken(purpose);
      await entrar(link.email, senha);
      return;
    }

    setEnviando(false);
    if (result.kind === 'http' && result.status === 404) {
      clearLinkToken(purpose);
      setFase({ tipo: 'invalido' });
      return;
    }
    if (result.kind === 'http' && result.status === 400) {
      setErroDaSenha(result.title ?? null);
      return;
    }
    if (result.kind === 'http' && result.status === 429) {
      setAviso('muitas-tentativas');
      setBloqueadoAte(Date.now() + (result.retryAfter ?? 1) * 1000);
      return;
    }
    setAviso('envio-indisponivel');
    if (result.kind === 'http' && result.status === 503 && result.retryAfter !== undefined) {
      setBloqueadoAte(Date.now() + result.retryAfter * 1000);
    }
  };

  const footer = fase.tipo === 'formulario' ? texts.footer(formatLinkExpiry(fase.link.expiraEm)) : undefined;
  const temProvedores = comProvedor && providers.length > 0;
  const avisoDaRecusa =
    fase.tipo === 'formulario' && recusaVisivel && recusa !== null && recusa !== 'convite-invalido' && refusal.lido
      ? invitationRefusalNotice(
          recusa,
          recusa === 'outro-email' && refusal.email !== null ? { provedor: refusal.email, convite: fase.link.email } : null,
        )
      : null;

  return (
    <EntryCard width={400} footer={footer}>
      {fase.tipo === 'carregando' && <LoadingLinkCard />}
      {fase.tipo === 'indisponivel' && (
        <UnavailableLinkCard
          onRetry={() => {
            setFase({ tipo: 'carregando' });
            setTentativa((n) => n + 1);
          }}
        />
      )}
      {fase.tipo === 'invalido' && <InvalidLinkCard text={texts.invalid} />}
      {fase.tipo === 'sem-token' && (
        <Stack gap="md">
          <Stack align="center" gap="xs" ta="center">
            <Logo variant="symbol" size={54} />
            <Title order={1} fz={18} fw={600}>
              Convite
            </Title>
          </Stack>
          <FederatedAlert notice={invitationRefusalNotice(fase.recusa, null)} />
          <Text c="dimmed" fz="sm">
            {ABRA_O_LINK_DE_NOVO}
          </Text>
        </Stack>
      )}
      {fase.tipo === 'formulario' && (
        <Stack gap="lg">
          {aviso === 'envio-indisponivel' && (
            <Alert color="red" role="alert">
              {ENVIO_INDISPONIVEL}
            </Alert>
          )}
          {aviso === 'muitas-tentativas' && (
            <Alert color="yellow" role="alert">
              {MUITAS_TENTATIVAS}
            </Alert>
          )}
          <Stack align="center" gap="xs" ta="center">
            <Logo variant="symbol" size={54} />
            <Title order={1} fz={18} fw={600}>
              {texts.heading(fase.link)}
            </Title>
            <Text c="dimmed" fz="sm">
              {temProvedores && texts.introComProvedor ? texts.introComProvedor(fase.link) : texts.intro(fase.link)}
            </Text>
          </Stack>
          {redirecionando !== null ? (
            <RedirectingNotice provedor={redirecionando} />
          ) : (
            <>
              {avisoDaRecusa && <FederatedAlert notice={avisoDaRecusa} />}
              {avisoDoInicio && <FederatedAlert notice={avisoDoInicio} />}
              {temProvedores && (
                <Stack gap="xs">
                  <Text fw={600} fz="sm">
                    {ENTRE_COM_UMA_CONTA(providers[0].nome)}
                  </Text>
                  <ProviderButtons
                    providers={providers}
                    label={(provider) => CONTINUAR_COM(provider.nome)}
                    onPick={(provider) => void continuarComProvedor(provider)}
                    disabled={enviando}
                  />
                  <Text c="dimmed" fz="xs">
                    {A_CONTA_PRECISA_TER}{' '}
                    <Text component="span" ff="monospace" fz="xs">
                      {fase.link.email}
                    </Text>
                    .
                  </Text>
                  <TextDivider label={OU_CRIE_UMA_SENHA} />
                </Stack>
              )}
              <NewPasswordForm
                email={fase.link.email}
                passwordLabel={texts.passwordLabel}
                submitLabel={texts.submitLabel}
                notice={texts.notice}
                serverPasswordError={erroDaSenha}
                onSubmit={(senha) => void handleSubmit(fase.link, senha)}
                submitting={enviando}
                disabled={bloqueadoAte !== null}
              />
            </>
          )}
        </Stack>
      )}
    </EntryCard>
  );
}

export function ConvitePage() {
  return <AccessLinkPage purpose="convite" />;
}

export function RedefinirSenhaPage() {
  return <AccessLinkPage purpose="redefinir-senha" />;
}
