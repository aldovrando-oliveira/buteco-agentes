**Issue:** #307

## Why

Nas rodadas intermediárias de uma change, entre a baseline e o fechamento, o que se roda é decidido à mão a cada change, no cabeçalho do script de suíte local ("Dentro: …; Fora: …, porque …"). A escolha não é mecânica nem auditável. Ela depende de alguém lembrar, por exemplo, que o `apps/frontend/deploy/nginx.conf` é guardado por testes .NET de três apps. Foi o único defeito da amostra do estudo de testes rápidos que escapou de uma seleção por app (`b4fcf5d`, #285). No frontend, sem seleção, a alternativa é a suíte inteira do Vitest, quando `tsc -b` + lint + `vitest related` de uma folha custam uma fração dela (os números e o estado estão no design).

As duas changes de barateamento da suíte do `apps/api` já entraram (#302 e #303). A marcação por `Trait` e a "área tocada" ficaram adiadas por decisão do dono. Sobra a parte que não marca nenhum teste: decidir, a partir do diff, o que rodar.

**Não é para acelerar a suíte do `apps/api`.** Com o api tocado, ela continua rodando inteira. O ganho é a seleção auditável em todo app, e o tempo, no frontend.

## What Changes

- **Script novo, `scripts/selecionar-testes.py` (repo, fora dos apps):** lê o diff da branch contra o `origin/main`, com o que não foi commitado e os arquivos novos, e **imprime** o plano: cada passo, o comando exato e o motivo (os caminhos e a regra que o selecionaram). **Não executa nada.** A execução continua nos scripts de suíte locais, com o lock, a amostragem de CPU e a regra assimétrica (design, D1).
- **As regras** (design, D4):
  - **app .NET tocado** (`api`, `workers`, `inbox`, `connectors`): a suíte inteira do app, sem filtro;
  - **projetos de `tests/`** que referenciam um app tocado, pelo grafo de `ProjectReference` lido dos `.csproj` na hora;
  - **frontend:** `tsc -b`, lint e `vitest related` dos arquivos mudados, ou o Vitest inteiro quando a mudança toca `src/auth/`, `src/app/`, `src/theme.ts`, a configuração do projeto, ou apaga um arquivo;
  - **artefato lido por outra pilha:** `apps/frontend/deploy/**` seleciona os testes `Nginx*` do api, do inbox e do connectors e o `nginxHeaders.test.ts` do frontend. `apps/frontend/package.json` e qualquer `.csproj` acrescentam, além da regra que os classifica, o `ProductVersionCoherenceTests` do api;
  - **caminho não reconhecido** (`Directory.*.props`, `global.json`, `libs/`, `tests/Shared/`, um app novo, a raiz): tudo;
  - **só documentação** (inclusive `.claude/**`): nada além dos dois passos de todo plano, o `check-docs.py` e os testes do próprio script.
- **Testes do script, `scripts/tests/` (unittest da biblioteca padrão):** uma tabela de seleção escrita à parte, os 13 fixes da amostra do estudo como casos estáticos, e dois guardas de alcance (design, D7).
- **Documentação:**
  - `docs/development.md`, "Rodar os testes": como usar o script e a tabela das regras;
  - `CONTRIBUTING.md` §1: a seleção vale para as rodadas intermediárias, e a suíte de cada app afetado continua obrigatória antes do PR; os testes do script entram no checklist antes do PR;
  - `01-ARQUITETURA_E_CONVENCOES.md`, convenção 19: a baseline e o fechamento continuam completos, e as rodadas intermediárias citam o plano.
- **Nenhum teste existente muda. Nenhum código de produção muda.** Nenhum app, `libs/` nem `tests/` é tocado.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `open-source-governance`: um requisito novo, junto do "Guia de contribuição alinhado ao fluxo OpenSpec", que já diz "como executar os testes de cada app". A seleção das rodadas intermediárias sai do diff, é verificável antes de rodar, erra para o lado de rodar mais, e não substitui a suíte completa antes do PR. **Sem o mecanismo:** nem o nome do script, nem as regras, nem o formato de saída (design, D9).

## Impact

- **Novos:** `scripts/selecionar-testes.py`, `scripts/tests/test_selecionar_testes.py`, `scripts/tests/selecao-esperada.md` (a tabela à parte), `scripts/tests/fixes-da-amostra.md` (os 13 casos).
- **Mudam:** `docs/development.md`, `CONTRIBUTING.md`, `01-ARQUITETURA_E_CONVENCOES.md` (convenção 19), e, pelo sync do archive, `openspec/specs/open-source-governance/spec.md`.
- **Dependência nova:** nenhuma. Python 3 da biblioteca padrão, como o `check-docs.py`; o `git` e o `vitest` já usados.
- **Prática local, fora do repositório:** o `suite-307.sh` (`~/.cache/buteco-agents/bin`) ganha um modo que executa os passos do plano, um por vez, com o lock e o veredito. Ele continua sendo o único executor.
- **Apps:** nenhum. As tarefas que rodam suíte de `apps/api`, `apps/workers`, `apps/inbox` e `apps/frontend` são medições do script, sem mudança nesses apps.
