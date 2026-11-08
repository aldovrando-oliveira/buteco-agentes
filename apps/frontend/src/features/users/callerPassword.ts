import { useState } from 'react';
import { blockedUntil, failureNotice, isApiFailure, type FailureNotice } from '../../utils/failureNotice';
import { useBlockedUntil } from '../../hooks/useBlockedUntil';
import { INFORME_A_SENHA } from './usersTexts';

// O envio das ações com a senha de quem chama (requisito "Senha de quem chama"; design.md da
// change usuarios-convites-identidade-painel, D4). Vazia não é enviada: o servidor contaria a
// tentativa no bloqueio do e-mail de quem chama (decisão do dono Q6). O `401` "Senha inválida."
// volta ao campo pelo `sessionFetch`, sem renovar nem deslogar (#246, Q0). A senha só existe
// neste estado, e sai no sucesso, depois do `401` e quando o formulário desmonta.

export function useCallerPassword() {
  const [senha, setSenha] = useState('');
  const [erro, setErro] = useState<string | null>(null);
  const [aviso, setAviso] = useState<FailureNotice | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [bloqueado, setBloqueadoAte] = useBlockedUntil();

  /**
   * Chama a ação com a senha. `outro` trata os erros próprios do formulário (o `400`, o
   * `409`) e devolve `true` quando tratou; o resto vira o `401` no campo ou o aviso.
   * Devolve o resultado, ou `undefined` quando não houve sucesso.
   */
  async function enviar<T>(call: (senha: string) => Promise<T>, outro?: (error: unknown) => boolean) {
    setAviso(null);
    if (senha === '') {
      setErro(INFORME_A_SENHA);
      return undefined;
    }
    setErro(null);
    setEnviando(true);
    try {
      const result = await call(senha);
      setSenha('');
      return result;
    } catch (error) {
      if (isApiFailure(error) && error.status === 401) {
        setSenha('');
        setErro(error.title ?? 'Senha inválida.');
        } else if (!outro?.(error)) {
        setAviso(failureNotice(error));
        setBloqueadoAte(blockedUntil(error));
      }
      return undefined;
    } finally {
      setEnviando(false);
    }
  }

  return {
    campo: { value: senha, onChange: setSenha, error: erro },
    aviso,
    enviando,
    bloqueado,
    enviar,
  };
}
