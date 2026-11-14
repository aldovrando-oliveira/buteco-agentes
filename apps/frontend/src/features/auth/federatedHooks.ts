import { useCallback, useEffect, useRef, useState } from 'react';
import {
  listLoginProviders,
  readRefusalEmail,
  type LoginProvider,
  type RefusalContent,
} from './api/federatedApi';

// Os hooks do login e do aceite de convite pelo provedor que o /entrar e o /convite dividem
// (design.md da change telas-login-google, D1 a D3).

/**
 * A lista dos provedores de tipo conhecido. Sem `enabled`, nada é pedido (o /entrar fora do contexto
 * seguro; o /convite antes da consulta válida). A falha é a lista vazia (Q5). `reload` relê, depois do
 * `404` "Provedor não configurado." do início.
 */
export function useLoginProviders(enabled: boolean) {
  const [providers, setProviders] = useState<LoginProvider[]>([]);
  const [rodada, setRodada] = useState(0);

  useEffect(() => {
    if (!enabled) return;
    let ativo = true;
    void listLoginProviders().then((lista) => {
      if (ativo) setProviders(lista);
    });
    return () => {
      ativo = false;
    };
  }, [enabled, rodada]);

  const reload = useCallback(() => setRodada((n) => n + 1), []);
  return { providers, reload };
}

/** A recusa que a página espera: o motivo e o provedor da query (`provedor` `null` sem ele). */
export interface ExpectedRefusal {
  motivo: string;
  provedor: string | null;
}

/**
 * O e-mail da recusa só vale se a recusa gravada é a da query: o mesmo motivo, e o mesmo provedor
 * quando a query o trouxe. Outra recusa (o cookie de um `nao-conectada` que ficou sem ser lido, por
 * exemplo) não empresta o e-mail: o aviso sai na variante sem ele (D2 e D3).
 */
function emailDaRecusa(content: RefusalContent | null, esperada: ExpectedRefusal): string | null {
  if (content === null || content.motivo !== esperada.motivo) return null;
  if (esperada.provedor !== null && content.provedor !== esperada.provedor) return null;
  return content.email;
}

/**
 * O e-mail confirmado da recusa, lido **uma vez** por montagem da rota de uso único
 * (`POST /auth/federated/refusal`). O pedido fica num `ref`: o efeito duplo do `StrictMode` não
 * dispara uma segunda chamada, que responderia `404` e apagaria o e-mail (D2). Cada execução do
 * efeito assina a mesma promessa, e só a viva grava o estado. A recusa esperada é a da query, lida
 * uma vez na montagem.
 */
export function useRefusalEmail(enabled: boolean, esperada: ExpectedRefusal) {
  const pedido = useRef<Promise<RefusalContent | null> | null>(null);
  const [estado, setEstado] = useState<{ lido: boolean; email: string | null }>({
    lido: false,
    email: null,
  });
  const { motivo, provedor } = esperada;

  useEffect(() => {
    if (!enabled) return;
    pedido.current ??= readRefusalEmail();
    let ativo = true;
    void pedido.current.then((content) => {
      if (ativo) setEstado({ lido: true, email: emailDaRecusa(content, { motivo, provedor }) });
    });
    return () => {
      ativo = false;
    };
  }, [enabled, motivo, provedor]);

  // Desligado, não há o que ler: o aviso sai sem e-mail, e nenhum pedido sai.
  return enabled ? estado : { lido: true, email: null };
}
