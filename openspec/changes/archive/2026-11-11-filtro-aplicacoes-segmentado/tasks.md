## Casos que saem ou mudam de asserção

Todos em `apps/frontend/src/features/applications/pages/ApplicationsListPage.test.tsx`:

- **`A busca vai ao servidor`**: o filtro passa de `user.selectOptions(getByLabelText('Estado'), 'ativas')` a
  `user.click` na opção "Ativas" do `radiogroup` "Estado". As asserções (`?busca=erp&estado=ativas`, nenhuma
  chamada por tecla) não mudam.
- **`Vazia e sem resultado são estados diferentes`**: fica só com a `vazia` (os dois textos e os dois "Nova
  aplicação", como antes) e ganha a ausência da busca e do `radiogroup` "Estado". A parte da busca sem resultado
  sai dele, porque a `vazia` não tem mais a busca para digitar, e vai para o caso novo abaixo.
- **`A busca sem resultado mantém a busca e o filtro, e Limpar busca volta a Todas`** (novo, com a parte que
  saiu do caso anterior): parte de uma lista com aplicações. Ficam as asserções do texto "…entre as ativas.", da
  ausência da `vazia`, da busca esvaziada pelo "Limpar busca" e de mais de um pedido. A busca e o filtro na
  tela durante a busca sem resultado são asserções novas. Muda uma: o filtro de volta a Todas passa de
  `toHaveValue('todas')` a "Todas" marcada, e o fim deixa de ser a `vazia` e passa a ser a lista de volta.
- **`O filtro de estado é o controle segmentado`** (novo): as três opções, na ordem, com "Todas" marcada, e
  nenhum `combobox` na página.

Nenhum caso fora desta lista sai ou afrouxa.

Um guarda marcado **(guarda)** só conta depois de ter reprovado contra o defeito que ele existe para pegar
(convenção 15). O `~/.cache/buteco-agents/bin/guarda-270.sh` reintroduz só o defeito nomeado no arquivo, roda o
arquivo de teste e devolve o arquivo pela cópia, com o sha conferido. Saídas em `~/.cache/buteco-agents/270-guardas/`.

**A suíte** roda pelo `~/.cache/buteco-agents/bin/suite-270.sh` (cópia do `suite-188.sh`), com o lock e a regra
assimétrica: uma rodada verde conta mesmo com a CPU caindo; uma reprovação só conta com a CPU em 100 do início ao
fim, senão é inconclusiva e se repete. Reprovação por caso que esta change mudou registra-se "reprovada pelo X
(a → b), corrigida". A comparação é por arquivo + nome (`frontend.json`) contra a baseline.

## 0. Preparação

- [x] 0.1 (repo) Issue #270 (`bug`, `tipo: bug`, `app: frontend`, milestone "Entrega M2 — Diferentes acessos") em `In progress` no board; worktrees `../buteco-agentes-270` (`fix/270-filtro-aplicacoes`) e `../buteco-agentes-270-baseline` (detached), os dois em `15dec2e`.
- [x] 0.2 (scripts locais) `suite-270.sh`, cópia do `suite-188.sh`, e `guarda-270.sh`, cópia do `guarda-188.sh` (só o cabeçalho muda nos dois).
- [x] 0.3 (baseline) `suite-270.sh` sobre o worktree da baseline, `frontend`, com a saída completa em `~/.cache/buteco-agents/270-baseline/`.
  Feito em 11/11/2026, em `15dec2e`, árvore limpa: 2628 casos em 199 arquivos, 154 s, CPU mínima 62, `APROVADA`. Load de 2,49 no início (o Spotlight indexando os worktrees novos segurou a rodada até ali); VM do Podman de pé, sem contêiner do compose.

## 1. A lista (apps/frontend, design D1, D2)

- [x] 1.1 `ApplicationsListPage`: o `SegmentedControl` no lugar do `NativeSelect` e do `Text` "Estado", com as props do `AgentListPage` e `aria-label="Estado"`; a busca com `maw={340}` e `flex: 1`; a linha `Group gap="sm" wrap="nowrap"`, escondida na `vazia`.
- [x] 1.2 Os casos da lista no topo, no `ApplicationsListPage.test.tsx`, achando o controle pelo `radiogroup` "Estado" e pelas opções dentro dele.

## 2. Os guardas

- [x] 2.1 **(guarda)** "Voltar ao `NativeSelect`": o `SegmentedControl` trocado pelo `NativeSelect` com o rótulo "Estado" de antes → reprova `O filtro de estado é o controle segmentado` (e os outros casos que procuram o controle).
  Visto: 3 reprovados de 10 (`O filtro de estado é o controle segmentado`, `A busca vai ao servidor`, `A busca sem resultado mantém…`), os três com `Unable to find an accessible element with the role "radiogroup" and name "Estado"`; voltou: `ApplicationsListPage.tsx`, sha igual (`e8e7b00`).
- [x] 2.2 **(guarda)** "A linha dos controles na `vazia`": o `{!vazia && (` trocado por `{(` → reprova `Vazia e sem resultado são estados diferentes`, nomeando a busca.
  Visto: 1 reprovado de 10, `Vazia e sem resultado são estados diferentes`, no `expect(…queryByRole('searchbox', { name: 'Buscar aplicação' })).not.toBeInTheDocument()` (l. 139); voltou com o sha igual.
- [x] 2.3 **(guarda)** "A linha some também na busca sem resultado": a condição trocada por `apps.length > 0` → reprova `A busca sem resultado mantém a busca e o filtro…`.
  Visto: 1 reprovado de 10, esse caso, com `Unable to find an accessible element with the role "searchbox" and name "Buscar aplicação"`; voltou com o sha igual. Saídas das três em `~/.cache/buteco-agents/270-guardas/`.

## 3. Spec e documentação

- [x] 3.1 Delta da `panel-external-applications`: o filtro como controle segmentado, a `vazia` sem a linha dos controles, o cenário novo e o cenário da `vazia` partido em dois pontos.
- [x] 3.2 `CHANGELOG.md` e `02-HISTORICO_E_STATUS.md`, **antes do archive**, com a baseline, a rodada final e os guardas.

## 4. Fechamento

- [x] 4.1 Rodada `frontend` pelo `suite-270.sh` na árvore da change. Comparação por arquivo + nome com a baseline: nenhum caso some nem muda de resultado fora da lista do topo; os novos são os dois da lista.
  Feito: 2630 em 199 arquivos, 161 s, CPU mínima 60, `APROVADA` (`~/.cache/buteco-agents/270-fechamento/`). Nenhum caso sumiu nem mudou de resultado; os novos são os dois da lista.
- [x] 4.2 Archive com o sync da delta, e `openspec validate --all --strict`.
- [x] 4.3 `git diff --stat` só com o `ApplicationsListPage`, o teste dele, `openspec/` e os documentos.
  Conferido: os dois arquivos da lista, `CHANGELOG.md`, `02-HISTORICO_E_STATUS.md` e `openspec/`.
- [x] 4.4 Checklist visual, no PR, para a homologação: a lista com o filtro em "Todas", "Ativas" e "Desativadas", a busca sem resultado e a vazia, nos dois esquemas de cor.
