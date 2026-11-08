import { useEffect, useState, type ReactNode } from 'react';
import { Alert, Stack, Text, Title } from '@mantine/core';
import { useNavigate } from 'react-router';
import { Logo } from '../../../components/brand/Logo';
import { completeLink, lookupLink, type LinkDetails, type LinkPurpose } from '../api/accessLinkApi';
import { createSession } from '../api/sessionApi';
import { EntryCard } from '../components/EntryCard';
import { InvalidLinkCard, LoadingLinkCard, UnavailableLinkCard } from '../components/LinkStateCards';
import { NewPasswordForm } from '../components/NewPasswordForm';
import { formatLinkExpiry } from '../linkExpiry';
import type { EntrarRouteState } from '../entryNotices';
import { clearLinkToken, useLinkToken } from '../linkToken';
import { ENVIO_INDISPONIVEL, MUITAS_TENTATIVAS } from '../messages';

// /convite e /redefinir-senha (spec `panel-access-link-pages`; design.md da change
// entrada-publica-painel, D3 a D5). O aceite e a conclusão respondem `204` sem sessão
// (#181 D6, P5), e a página entra em seguida com o e-mail da consulta e a senha digitada.

interface PageTexts {
  heading: (link: LinkDetails) => string;
  intro: (link: LinkDetails) => ReactNode;
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
  | { tipo: 'formulario'; link: LinkDetails };

type AvisoDoEnvio = 'muitas-tentativas' | 'envio-indisponivel' | null;

export function AccessLinkPage({ purpose }: { purpose: LinkPurpose }) {
  const token = useLinkToken(purpose);
  const navigate = useNavigate();
  const texts = TEXTS[purpose];
  const [fase, setFase] = useState<Fase>(token ? { tipo: 'carregando' } : { tipo: 'invalido' });
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
              {texts.intro(fase.link)}
            </Text>
          </Stack>
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
