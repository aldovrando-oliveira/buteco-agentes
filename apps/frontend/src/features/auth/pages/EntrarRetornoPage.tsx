import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Center, Loader, Paper, Stack, Text } from '@mantine/core';
import { useLocation, useNavigate } from 'react-router';
import { beginLogin, exchangeCode, takePendingLogin } from '../../../auth/loginFlow';
import { authRuntime, renewal } from '../../../auth/session';
import { FALHA_NA_ENTRADA, INICIANDO } from '../messages';

interface Retorno {
  code: string | null;
  state: string | null;
  error: string | null;
  errorDescription: string | null;
}

type Estado =
  | { tipo: 'concluindo' }
  | { tipo: 'sem-login' }
  | { tipo: 'erro'; voltarPara: string }
  | { tipo: 'iniciando'; voltarPara: string };

function lerRetorno(search: string): Retorno {
  const params = new URLSearchParams(search);
  return {
    code: params.get('code'),
    state: params.get('state'),
    error: params.get('error'),
    errorDescription: params.get('error_description'),
  };
}

// O `redirect_uri` do cliente `buteco-painel` (design.md da change
// painel-entra-pelo-emissor, D6). A URL chega com `code` e `state`: eles são lidos uma
// vez, e a URL é trocada por `/entrar/retorno` sem query ANTES de qualquer pedido, em
// toda saída, para o código não ficar na entrada do histórico da aba nem no `Referer`
// de um pedido seguinte (revisão do dono de 01/11/2026). O processamento usa os valores
// capturados e roda uma vez por montagem: no StrictMode o efeito roda duas vezes, e o
// login em andamento é de uso único.
//
// O `iss` da resposta não é conferido enquanto o painel conhecer um emissor só (#189).
export function EntrarRetornoPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const [retorno] = useState(() => lerRetorno(location.search));
  const processado = useRef(false);
  const [estado, setEstado] = useState<Estado>({ tipo: 'concluindo' });

  useEffect(() => {
    if (processado.current) return;
    processado.current = true;
    navigate('/entrar/retorno', { replace: true });

    void (async () => {
      const pendente = takePendingLogin(authRuntime);
      if (pendente === null || retorno.state === null || retorno.state !== pendente.state) {
        setEstado({ tipo: 'sem-login' });
        return;
      }

      if (retorno.error !== null) {
        // `login_required` só vem com `prompt=none`, que o painel nunca manda.
        if (retorno.error === 'login_required') {
          navigate('/entrar', { replace: true });
          return;
        }
        // Inglês e técnico: vai ao console, e não à tela.
        console.warn(`Authorize respondeu ${retorno.error}: ${retorno.errorDescription ?? ''}`);
        setEstado({ tipo: 'erro', voltarPara: pendente.voltarPara });
        return;
      }

      if (retorno.code === null) {
        setEstado({ tipo: 'sem-login' });
        return;
      }

      const resultado = await exchangeCode(authRuntime, renewal, {
        code: retorno.code,
        verifier: pendente.verifier,
      });
      if (resultado === 'ok') {
        navigate(pendente.voltarPara, { replace: true });
      } else if (resultado === 'rejected') {
        // Código vencido ou já usado, ou usuário desativado entre o authorize e a troca.
        navigate(
          `/entrar?motivo=falha-na-entrada&voltarPara=${encodeURIComponent(pendente.voltarPara)}`,
          { replace: true },
        );
      } else {
        setEstado({ tipo: 'iniciando', voltarPara: pendente.voltarPara });
      }
    })();
  }, [navigate, retorno]);

  const recomecar = async (voltarPara: string) => {
    authRuntime.assign(await beginLogin(authRuntime, voltarPara));
  };

  return (
    <Center mih="100vh" p="md">
      <Paper withBorder shadow="sm" p="xl" w="100%" maw={380}>
        {estado.tipo === 'concluindo' && (
          <Stack align="center" gap="sm">
            <Loader size="sm" />
            <Text>Concluindo a entrada…</Text>
          </Stack>
        )}
        {estado.tipo === 'sem-login' && (
          <Stack>
            <Alert color="red" role="alert">
              {FALHA_NA_ENTRADA}
            </Alert>
            <Button fullWidth onClick={() => navigate('/entrar', { replace: true })}>
              Entrar
            </Button>
          </Stack>
        )}
        {estado.tipo === 'erro' && (
          <Stack>
            <Alert color="red" role="alert">
              Não foi possível concluir a entrada.
            </Alert>
            <Button fullWidth onClick={() => void recomecar(estado.voltarPara)}>
              Tentar de novo
            </Button>
          </Stack>
        )}
        {estado.tipo === 'iniciando' && (
          <Stack>
            <Alert color="yellow" role="alert">
              {INICIANDO}
            </Alert>
            <Button fullWidth onClick={() => void recomecar(estado.voltarPara)}>
              Tentar de novo
            </Button>
          </Stack>
        )}
      </Paper>
    </Center>
  );
}
