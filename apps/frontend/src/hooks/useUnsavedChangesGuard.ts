import { useCallback, useEffect } from 'react';
import { useBeforeUnload, useBlocker } from 'react-router';

export interface UnsavedChangesGuard {
  isBlocked: boolean;
  confirmNavigation: () => void;
  cancelNavigation: () => void;
}

// Intercepta a navegação enquanto houver rascunho não salvo. Cobre os três
// jeitos de perder trabalho: trocar de aba (mudança só de query string,
// por isso a comparação inclui `search`), sair da rota e fechar ou
// recarregar a janela.
//
// Não renderiza nada: devolve só o estado do bloqueio, e a cópia do
// diálogo fica com quem consome (Decision 3 do design.md da change
// frontend-agente-detalhe-abas).
//
// Depende de a aplicação estar em data mode — `useBlocker` resolve o
// contexto de data router e estoura fora dele (ver change
// frontend-roteamento-data-router).
export function useUnsavedChangesGuard(isDirty: boolean): UnsavedChangesGuard {
  const blocker = useBlocker(
    useCallback(
      ({ currentLocation, nextLocation }) =>
        isDirty &&
        (currentLocation.pathname !== nextLocation.pathname ||
          currentLocation.search !== nextLocation.search),
      [isDirty],
    ),
  );

  // Um bloqueio já ativo não se desfaz sozinho quando o rascunho deixa de
  // existir (por exemplo, ao salvar com o diálogo aberto): sem isso o
  // roteador ficaria travado num bloqueio que não tem mais motivo.
  useEffect(() => {
    if (blocker.state === 'blocked' && !isDirty) {
      blocker.reset();
    }
  }, [blocker, isDirty]);

  useBeforeUnload(
    useCallback(
      (event: BeforeUnloadEvent) => {
        if (isDirty) {
          event.preventDefault();
        }
      },
      [isDirty],
    ),
  );

  return {
    isBlocked: blocker.state === 'blocked',
    confirmNavigation: () => blocker.proceed?.(),
    cancelNavigation: () => blocker.reset?.(),
  };
}
