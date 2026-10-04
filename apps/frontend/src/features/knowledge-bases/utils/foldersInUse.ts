import type { KnowledgeBase } from '../types/knowledgeBase';

// PASTA EM USO, pelo cruzamento com `GET /knowledge-bases` (design.md, D4). É
// ajuda visual: o 409 `folder-in-use` do apps/api continua sendo a garantia,
// porque a base pode ter sido criada depois da listagem.
//
// A chave é provedor + id, comparados de forma EXATA e sensível a caixa, como o
// índice único da pasta no apps/api (#104: `AbC` e `abc` não conflitam). Entram as
// bases sincronizadas ativas E inativas: a pasta continua ocupada com a base
// inativa.
export type FoldersInUse = ReadonlyMap<string, string>;

function key(providerKey: string, folderId: string): string {
  return `${providerKey}\u0000${folderId}`;
}

export function buildFoldersInUse(bases: KnowledgeBase[]): FoldersInUse {
  const map = new Map<string, string>();
  for (const base of bases) {
    if (base.syncSource) {
      map.set(key(base.syncSource.provider, base.syncSource.folderId), base.name);
    }
  }
  return map;
}

// Nome da base que usa a pasta, ou `undefined` se nenhuma base conhecida a usa.
export function folderInUseBy(
  foldersInUse: FoldersInUse,
  providerKey: string,
  folderId: string,
): string | undefined {
  return foldersInUse.get(key(providerKey, folderId));
}
