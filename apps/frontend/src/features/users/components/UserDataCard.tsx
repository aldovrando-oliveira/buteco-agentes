import { useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Group,
  NativeSelect,
  SimpleGrid,
  Stack,
  Text,
  TextInput,
} from '@mantine/core';
import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { ActionDenied } from '../../../components/feedback/ActionDenied';
import {
  PAPEIS,
  papelDaResposta,
  papelParaCorpo,
  rotuloDoPapel,
  type Papel,
} from '../../../auth/roles';
import { renewal, sessionStore } from '../../../auth/session';
import {
  failureNotice,
  failureText,
  isApiFailure,
  isRoleRefusal,
  type FailureNotice,
} from '../../../utils/failureNotice';
import { ACCOUNT_QUERY_KEY } from '../../account/queryKeys';
import { ApiError, changeUserRole, updateUser, type UserDetail } from '../api/usersApi';
import { useCallerPassword } from '../callerPassword';
import { USERS_QUERY_KEY, userQueryKey } from '../queryKeys';
import { DEIXAR_DE_SER_ADMIN, DESCRICAO_DO_PAPEL } from '../usersTexts';
import { CallerPasswordField } from './CallerPasswordField';

// O cartão "Dados e papel" de Editar usuário (requisitos "Cartão Dados de Editar usuário" e
// "Rebaixar a si mesmo" de `panel-user-detail-page`; design.md das changes editar-usuario-painel,
// D5, e papeis-no-painel, D4). O rascunho só existe enquanto difere do lido: voltar ao original
// desabilita "Salvar", e sem rascunho o formulário mostra a leitura mais nova.
//
// Um "Salvar alterações" só, como no quadro, e duas rotas: `PUT /users/{id}` (nome e e-mail) e
// `PUT /users/{id}/role` (papel). A ordem é DADOS PRIMEIRO, e o papel só sai depois do `200` dos
// dados (decisão do dono Q5): a falha dos dados não muda o papel, e a falha do papel depois dos
// dados salvos vira o aviso de gravação parcial, com o motivo do servidor. A chamada é direta,
// sem `useMutation`: a promoção leva a senha de quem chama, e o `MutationCache` a guardaria.
//
// Rebaixar a si mesmo (Q6): "Salvar" abre a confirmação do quadro; confirmada, o `200` renova a
// sessão (para o id token trazer o papel novo, e o menu perder a seção Acesso) e vai a Minha
// conta, SEM reler nem invalidar Usuários, que responderiam `403`.

interface UserDataCardProps {
  user: UserDetail;
  proprio: boolean;
  /** O usuário sumiu entre a leitura e o `PUT` (`404`). */
  onGone: () => void;
}

interface Erros {
  nome?: string;
  email?: string;
  papel?: string;
}

type Resultado =
  { tipo: 'dados' } | { tipo: 'papel'; nome: string } | { tipo: 'parcial'; motivo: string };

/**
 * Na promoção, o que fica no bloco da senha, pelo `useCallerPassword`, como no convite: o `401`
 * no campo, e o bloqueio, o excesso, a indisponibilidade e a rede no aviso do bloco, com o botão
 * desabilitado até o `Retry-After`. O resto volta para o cartão.
 */
function ficaNoBlocoDaSenha(error: unknown): boolean {
  return !isApiFailure(error) || [401, 423, 429, 503].includes(error.status);
}

/** O motivo da gravação parcial: o `title` do servidor, ou o texto dos avisos dos cartões. */
function motivo(error: unknown): string {
  if (isApiFailure(error) && error.status === 401) return error.title ?? 'Senha inválida.';
  return failureText(error);
}

export function UserDataCard({ user, proprio, onGone }: UserDataCardProps) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const senha = useCallerPassword();
  const [rascunho, setRascunho] = useState<{ nome: string; email: string } | null>(null);
  const [escolhido, setEscolhido] = useState<Papel | null>(null);
  const [erros, setErros] = useState<Erros>({});
  const [aviso, setAviso] = useState<FailureNotice | null>(null);
  const [recusado, setRecusado] = useState(false);
  const [resultado, setResultado] = useState<Resultado | null>(null);
  const [confirmando, setConfirmando] = useState(false);
  const [salvando, setSalvando] = useState(false);

  const nome = rascunho?.nome ?? user.nome;
  const email = rascunho?.email ?? user.email;
  const papelLido = papelDaResposta(user.papel);
  const papel = escolhido ?? papelLido;
  const dadosSujos = nome !== user.nome || email !== user.email;
  const papelSujo = papel !== null && papel !== papelLido;
  const sujo = dadosSujos || papelSujo;
  const desativado = user.estado === 'desativado';
  const promocao = papelSujo && papel === 'admin' && !proprio;
  const rebaixandoSi = proprio && papelLido === 'admin' && papelSujo && papel !== 'admin';

  const limparAvisos = () => {
    setResultado(null);
    setErros({});
    setAviso(null);
    setRecusado(false);
  };

  const editar = (campo: 'nome' | 'email', valor: string) => {
    limparAvisos();
    setRascunho({ nome, email, [campo]: valor });
  };

  const escolher = (valor: string) => {
    limparAvisos();
    setConfirmando(false);
    setEscolhido(papelDaResposta(valor));
  };

  const descartar = () => {
    limparAvisos();
    setConfirmando(false);
    setRascunho(null);
    setEscolhido(null);
  };

  /** Os erros que não vão a campo: o `403` de papel no aviso próprio, o resto no dos cartões. */
  const avisar = (error: unknown) => {
    if (isRoleRefusal(error)) setRecusado(true);
    else setAviso(failureNotice(error));
  };

  /** O passo 1: nome e e-mail. Devolve se pode seguir para o papel. */
  async function salvarDados(): Promise<boolean> {
    try {
      const detalhe = await updateUser(user.id, { nome, email });
      queryClient.setQueryData(userQueryKey(user.id), detalhe);
      if (!rebaixandoSi)
        void queryClient.invalidateQueries({ queryKey: USERS_QUERY_KEY, exact: true });
      if (proprio) void queryClient.invalidateQueries({ queryKey: ACCOUNT_QUERY_KEY });
      setRascunho(null);
      return true;
    } catch (error) {
      if (!isApiFailure(error)) {
        avisar(error);
      } else if (error.status === 409) {
        setErros({ email: error.title ?? 'Já existe um usuário com este e-mail.' });
      } else if (error.status === 400 && error instanceof ApiError) {
        const campos = error.problem?.errors ?? {};
        setErros({ nome: campos.nome?.[0], email: campos.email?.[0] });
      } else if (error.status === 404) {
        onGone();
      } else {
        avisar(error);
      }
      return false;
    }
  }

  /** O passo 2: o papel. `depoisDosDados` diz se a falha vira o aviso de gravação parcial. */
  async function salvarPapel(alvo: Papel, depoisDosDados: boolean) {
    let falha: unknown;
    const chamar = (s?: string) =>
      changeUserRole(
        user.id,
        s === undefined
          ? { papel: papelParaCorpo(alvo) }
          : { papel: papelParaCorpo(alvo), senha: s },
      ).catch((error: unknown) => {
        falha = error;
        throw error;
      });

    let detalhe: UserDetail | undefined;
    if (promocao) {
      // O `useCallerPassword` cuida do que fica no bloco da senha; o resto volta para cá.
      detalhe = await senha.enviar(chamar, (error) => !ficaNoBlocoDaSenha(error));
    } else {
      detalhe = await chamar().catch(() => undefined);
    }

    if (detalhe) {
      setEscolhido(null);
      if (rebaixandoSi) {
        // Renovar antes de sair: o id token novo tira a seção Acesso do menu, e Minha conta abre
        // com o aviso. Nada de Usuários é relido (`403`).
        // Com o token de agora como "o que falhou": sem ele, a renovação reaproveitaria o
        // registro ainda válido e não buscaria o id token novo.
        await renewal.renew(sessionStore.read()?.accessToken);
        navigate('/account', { state: { papelNovo: detalhe.papel } });
        return;
      }
      queryClient.setQueryData(userQueryKey(user.id), detalhe);
      void queryClient.invalidateQueries({ queryKey: USERS_QUERY_KEY, exact: true });
      setResultado({ tipo: 'papel', nome: detalhe.nome });
      return;
    }

    if (depoisDosDados) {
      setResultado({ tipo: 'parcial', motivo: motivo(falha) });
      return;
    }
    if (promocao && ficaNoBlocoDaSenha(falha)) return;
    if (!isApiFailure(falha)) avisar(falha);
    else if (falha.status === 409) setErros({ papel: falha.title });
    else if (falha.status === 400 && falha instanceof ApiError)
      setErros({ papel: falha.problem?.errors?.papel?.[0] });
    else if (falha.status === 404) onGone();
    else avisar(falha);
  }

  async function salvar() {
    limparAvisos();
    // A senha vazia na promoção não sai, e nada é gravado antes de pedi-la.
    if (promocao && senha.campo.value === '') {
      await senha.enviar(async () => undefined);
      return;
    }
    setSalvando(true);
    try {
      const alvo = papel;
      if (dadosSujos && !(await salvarDados())) return;
      if (papelSujo && alvo) await salvarPapel(alvo, dadosSujos);
      else if (dadosSujos) setResultado({ tipo: 'dados' });
    } finally {
      setSalvando(false);
      setConfirmando(false);
    }
  }

  const pedirSalvar = () => {
    if (!sujo) return;
    if (rebaixandoSi && !confirmando) {
      limparAvisos();
      setConfirmando(true);
      return;
    }
    void salvar();
  };

  return (
    <SectionedCard title="Dados e papel">
      <Box
        component="form"
        noValidate
        px="md"
        py="md"
        onSubmit={(event) => {
          event.preventDefault();
          pedirSalvar();
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
            <Stack gap={4}>
              <NativeSelect
                label="Papel"
                value={papel ?? ''}
                disabled={desativado}
                onChange={(e) => escolher(e.currentTarget.value)}
                data={[
                  ...(papel === null ? [{ value: '', label: '—' }] : []),
                  ...PAPEIS.map((p) => ({ value: p, label: rotuloDoPapel(p) })),
                ]}
                error={erros.papel}
                description={papel ? DESCRICAO_DO_PAPEL[papel] : undefined}
                inputWrapperOrder={['label', 'input', 'error', 'description']}
              />
              {desativado && (
                <Text size="xs" c="dimmed">
                  Reative o usuário antes de mudar o papel.
                </Text>
              )}
            </Stack>
          </SimpleGrid>
          {promocao && (
            <Stack gap="xs" maw={560}>
              <Text size="sm">
                Como Admin, {user.nome} passa a gerir usuários e a ver Identidade. Para promover,
                confirme com a sua senha.
              </Text>
              {senha.aviso && (
                <Alert color={senha.aviso.cor} role="alert">
                  {senha.aviso.texto}
                </Alert>
              )}
              <CallerPasswordField
                {...senha.campo}
                description="Para promover, confirme que é você."
              />
            </Stack>
          )}
          {confirmando && (
            <Alert color="red" role="alert" maw={720}>
              <Stack gap="xs">
                <Text size="sm">{DEIXAR_DE_SER_ADMIN}</Text>
                <Group gap="sm">
                  <Button size="xs" color="red" loading={salvando} onClick={() => void salvar()}>
                    Deixar de ser Admin
                  </Button>
                  <Button
                    size="xs"
                    variant="subtle"
                    color="gray"
                    disabled={salvando}
                    onClick={() => setConfirmando(false)}
                  >
                    Cancelar
                  </Button>
                </Group>
              </Stack>
            </Alert>
          )}
          {resultado?.tipo === 'parcial' && (
            <Alert color="red" role="alert" maw={720}>
              Nome e e-mail salvos. O papel não mudou: {resultado.motivo}
            </Alert>
          )}
          {recusado && <ActionDenied maw={560} />}
          {aviso && (
            <Alert color={aviso.cor} maw={560}>
              {aviso.texto}
            </Alert>
          )}
          <Group gap="sm">
            <Button
              type="submit"
              size="sm"
              disabled={!sujo || confirmando || (promocao && senha.bloqueado)}
              loading={salvando && !confirmando}
            >
              Salvar alterações
            </Button>
            {sujo && (
              <Button
                variant="subtle"
                color="gray"
                size="sm"
                disabled={salvando}
                onClick={descartar}
              >
                Descartar
              </Button>
            )}
            {resultado?.tipo === 'dados' && (
              <Text size="xs" c="green" role="status">
                Alterações salvas.
              </Text>
            )}
            {resultado?.tipo === 'papel' && (
              <Text size="xs" c="green" role="status">
                Alterações salvas. O novo papel chega ao painel de {resultado.nome} em até 15
                minutos, quando a sessão renovar; um rebaixamento já vale na hora em Usuários e
                Identidade.
              </Text>
            )}
          </Group>
        </Stack>
      </Box>
    </SectionedCard>
  );
}
