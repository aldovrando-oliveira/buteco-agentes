import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { blockedUntil, failureNotice, isApiFailure, type FailureNotice } from '../../utils/failureNotice';
import { useBlockedUntil } from '../../hooks/useBlockedUntil';
import { getAccount } from '../account/api/accountApi';
import { ACCOUNT_QUERY_KEY } from '../account/queryKeys';
import type { CallerConfirmation } from './api/usersApi';
import { INFORME_A_SENHA, INFORME_O_CODIGO } from './usersTexts';

// O envio das ações com a confirmação de quem chama (requisito "Senha de quem chama"; design.md da
// change usuarios-convites-identidade-painel, D4). Vazia não é enviada: o servidor contaria a
// tentativa no bloqueio do e-mail de quem chama (decisão do dono Q6). O `401` volta ao campo pelo
// `sessionFetch`, sem renovar nem deslogar (#246, Q0). O valor só existe neste estado, e sai no
// sucesso, depois do `401` e quando o formulário desmonta.
//
// O MODO (change telas-contas-federadas, D2, Q2 e Q3 do design da #190): quem não tem senha (entrou
// só pelo Google) confirma com o código do app autenticador, e o backend, para essa pessoa, só confere
// o `codigo` (`CallerPassword.RejectAsync`). O painel sabe disso por `formasDeEntrada` de
// `GET /account`, o único sinal servido. Razões do desenho:
// 1. `'codigo'` só com a resposta em mãos: pendente ou em erro, o modo é `'senha'`, o de hoje, e
//    nenhum dos seis formulários espera um pedido a mais para abrir (Q2).
// 2. A falha da consulta não deixa ninguém preso: no modo `'senha'`, o `401` "Código inválido." só
//    sai para quem não tem senha, então relê a conta, e o campo passa ao código.
// 3. O corpo leva UM campo, o do modo: mandar os dois deixaria o contrato depender de o backend
//    ignorar o outro.
// 4. A troca de modo chega limpa (decisão do dono no fechamento): depois da releitura, o
//    "Código inválido." era da senha que a pessoa digitou, e não descreve o campo do código.

/** O `title` com que o backend recusa o código (`CallerPassword.cs`, `InvalidCodeMessage`). */
export const CODIGO_INVALIDO = 'Código inválido.';

export type ModoDeConfirmacao = 'senha' | 'codigo';

export function useCallerPassword() {
  const conta = useQuery({ queryKey: ACCOUNT_QUERY_KEY, queryFn: getAccount });
  const modo: ModoDeConfirmacao =
    conta.data && !conta.data.formasDeEntrada.includes('senha') ? 'codigo' : 'senha';
  const [valor, setValor] = useState('');
  const [erro, setErro] = useState<string | null>(null);
  const [aviso, setAviso] = useState<FailureNotice | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [bloqueado, setBloqueadoAte] = useBlockedUntil();
  // A troca de modo (o 4 acima): o ajuste durante a renderização, sem efeito.
  const [modoVisto, setModoVisto] = useState(modo);
  if (modoVisto !== modo) {
    setModoVisto(modo);
    setErro(null);
    setValor('');
  }

  /**
   * Chama a ação com a confirmação. `outro` trata os erros próprios do formulário (o `400`, o
   * `409`) e devolve `true` quando tratou; o resto vira o `401` no campo ou o aviso.
   * Devolve o resultado, ou `undefined` quando não houve sucesso.
   */
  async function enviar<T>(
    call: (confirmacao: CallerConfirmation) => Promise<T>,
    outro?: (error: unknown) => boolean,
  ) {
    setAviso(null);
    if (valor === '') {
      setErro(modo === 'codigo' ? INFORME_O_CODIGO : INFORME_A_SENHA);
      return undefined;
    }
    setErro(null);
    setEnviando(true);
    try {
      const result = await call(modo === 'codigo' ? { codigo: valor } : { senha: valor });
      setValor('');
      return result;
    } catch (error) {
      if (isApiFailure(error) && error.status === 401) {
        setValor('');
        setErro(error.title ?? (modo === 'codigo' ? CODIGO_INVALIDO : 'Senha inválida.'));
        if (modo === 'senha' && error.title === CODIGO_INVALIDO) void conta.refetch();
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
    modo,
    campo: { value: valor, onChange: setValor, error: erro, modo },
    aviso,
    enviando,
    bloqueado,
    enviar,
  };
}
