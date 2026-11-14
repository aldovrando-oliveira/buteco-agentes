import { useEffect, useState } from 'react';
import { useLocation, useNavigate } from 'react-router';
import { AVISO_CONECTADA, refusalNotice, type FormsNotice } from './accountFederatedTexts';

// A volta do conectar em `/account` (requisito "A volta do conectar em /account"; design.md da change
// telas-contas-federadas, D4). O backend responde `302 /account?conectado=<chave>` ou
// `/account?recusa=<motivo>[&provedor=<chave>]`.
// 1. A leitura é uma só, na montagem, e o aviso fica no estado da página até ela desmontar.
// 2. Os três parâmetros saem do endereço por `replace`, como no `/entrar` (Q4 da #190): recarregar não
//    repete o aviso, e um link copiado não o leva a outra pessoa. O `state` da navegação vai junto: é
//    ele que traz o aviso do rebaixamento de si mesmo.
// 3. Nenhum texto do conectar leva o e-mail, e o conectar nem grava o `buteco-recusa`: a rota de uso
//    único do aviso (`POST /auth/federated/refusal`) não é chamada aqui.

const PARAMETROS = ['conectado', 'recusa', 'provedor'] as const;

/** O aviso que a query traz, ou `null`. `recusa` vence `conectado`, que o backend nunca manda junto. */
export function returnNotice(search: string): FormsNotice | null {
  const query = new URLSearchParams(search);
  const recusa = query.get('recusa');
  if (recusa !== null) return refusalNotice(recusa);
  if (query.get('conectado') !== null) return AVISO_CONECTADA;
  return null;
}

export function useFederatedReturn(): FormsNotice | null {
  const location = useLocation();
  const navigate = useNavigate();
  const [aviso] = useState(() => returnNotice(location.search));

  useEffect(() => {
    const query = new URLSearchParams(location.search);
    if (!PARAMETROS.some((nome) => query.has(nome))) return;
    for (const nome of PARAMETROS) query.delete(nome);
    const resto = query.toString();
    navigate(
      { pathname: location.pathname, search: resto ? `?${resto}` : '', hash: location.hash },
      { replace: true, state: location.state },
    );
    // Só na montagem: a leitura é uma.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return aviso;
}
