## Casos que saem ou mudam de asserção

Nenhum. Os casos do `ApplicationsListPage.test.tsx` que já existiam ficam com as mesmas asserções; só o
comentário do topo do arquivo passa a citar a #272. Os casos novos, todos no mesmo arquivo:

- `a primeira carga não mostra a busca nem o filtro`;
- `o erro da primeira carga não mostra a busca nem o filtro, até a lista chegar`;
- `na busca seguinte, a busca e o filtro continuam na tela enquanto a nova lista carrega`;
- `o erro de uma busca seguinte mantém a busca e o filtro na tela` (design D2).

Um guarda marcado **(guarda)** só conta depois de ter reprovado contra o defeito que ele existe para pegar
(convenção 15). O `~/.cache/buteco-agents/bin/guarda-272.sh` reintroduz só o defeito nomeado, roda o arquivo de
teste e devolve o arquivo pela cópia, com o sha conferido. Saídas em `~/.cache/buteco-agents/272-guardas/`.

**A suíte** roda pelo `~/.cache/buteco-agents/bin/suite-272.sh` (cópia do `suite-270.sh`), com o lock e a regra
assimétrica, **depois do Prettier**. A comparação é por arquivo + nome (`frontend.json`) contra a baseline.

## 0. Preparação

- [x] 0.1 (repo) Issue #272 (`bug`, `tipo: bug`, `app: frontend`, milestone "Entrega M2 — Diferentes acessos") em `In progress` no board; worktrees `../buteco-agentes-272` (`fix/272-filtro-aplicacoes-carga`) e `../buteco-agentes-272-baseline` (detached), os dois em `9f8580f`.
- [x] 0.2 (scripts locais) `suite-272.sh` e `guarda-272.sh`, cópias do `suite-270.sh` e do `guarda-270.sh` (só o cabeçalho muda).
- [x] 0.3 (baseline) `suite-272.sh` sobre o worktree da baseline, `frontend`, em `~/.cache/buteco-agents/272-baseline/`.
  Feito em 11/11/2026, em `9f8580f`, árvore limpa: 2630 casos em 199 arquivos, 225 s, CPU mínima 53, `APROVADA`. Load de 2,31 no início. Uma rodada isolada do `ApplicationsListPage.test.tsx` na árvore da change (14 casos, ~6 s) se sobrepôs à baseline por volta das 9:57, fora do lock; a rodada foi verde e conta pela regra assimétrica, mas a duração e a CPU mínima carregam essa sobreposição.

## 1. A linha dos controles (apps/frontend, design D1, D2)

- [x] 1.1 `ApplicationsListPage`: o estado `jaCarregou`, ajustado na renderização antes do retorno do `403`, e `mostrarControles = (jaCarregou || query.isSuccess) && !vazia` no lugar do `!vazia`.
- [x] 1.2 Os quatro casos novos da lista do topo.

## 2. Os guardas

- [x] 2.1 **(guarda)** "A linha visível na primeira carga": `mostrarControles = !vazia` (o comportamento da #270) → reprova `a primeira carga não mostra a busca nem o filtro`.
  Visto: 2 reprovados de 14, `a primeira carga não mostra a busca nem o filtro` e `o erro da primeira carga não mostra a busca nem o filtro, até a lista chegar`, os dois no `expect(…queryByRole('searchbox', { name: 'Buscar aplicação' })).not.toBeInTheDocument()`; voltou: `ApplicationsListPage.tsx`, sha igual (`ab68dbb`).
- [x] 2.2 **(guarda)** "A linha escondida em toda carga": `&& !query.isFetching` na condição → reprova `na busca seguinte, a busca e o filtro continuam na tela enquanto a nova lista carrega`.
  Visto: 2 reprovados de 14, esse caso (`Unable to find an accessible element with the role "radiogroup" and name "Estado"`, depois que a busca sumiu) e `A busca sem resultado mantém a busca e o filtro, e Limpar busca volta a Todas` (da #270, `searchbox` ausente); voltou com o sha igual.
- [x] 2.3 **(guarda)** "Só os dados não bastam" (design D1): `mostrarControles = query.data !== undefined && !vazia` → reprova `o erro de uma busca seguinte mantém a busca e o filtro na tela`.
  Visto: 1 reprovado de 14, esse caso, com `Unable to find an accessible element with the role "searchbox" and name "Buscar aplicação"`; voltou com o sha igual.

## 3. Spec e documentação

- [x] 3.1 Delta da `panel-external-applications`: quando a linha aparece, as linhas `carregando` e `erro` da tabela, e os cenários "A primeira carga não mostra a busca nem o filtro" e "Na busca seguinte, a busca e o filtro continuam na tela".
- [x] 3.2 `CHANGELOG.md` e `02-HISTORICO_E_STATUS.md`, **antes do archive**.

## 4. Fechamento

- [x] 4.1 Prettier, `eslint` e `tsc -b` na árvore da change, antes da suíte.
- [x] 4.2 Rodada `frontend` pelo `suite-272.sh` na árvore da change, depois do Prettier. Comparação por arquivo + nome com a baseline: nenhum caso some nem muda de resultado; os novos são os quatro da lista.
  Feito: 2634 em 199 arquivos, 177 s, CPU mínima 58, `APROVADA` (`~/.cache/buteco-agents/272-fechamento/`), com load de 1,91 no início. Nenhum caso sumiu nem mudou de resultado; os novos são os quatro.
- [x] 4.3 Archive com o sync da delta, e `openspec validate --all --strict`.
- [x] 4.4 `git diff --stat` só com o `ApplicationsListPage`, o teste dele, `openspec/` e os documentos.
  Conferido: os dois arquivos da lista, `CHANGELOG.md`, `02-HISTORICO_E_STATUS.md` e `openspec/`.
