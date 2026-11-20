**Issue:** #302

## Why

A suíte do `apps/api` roda com o paralelismo padrão do xUnit (12 coleções ao mesmo tempo nesta máquina) e sai **inconclusiva** em boa parte das rodadas. Ela reprova um caso que mede prazo, sempre com a CPU estrangulada, e pela regra assimétrica da #181 a rodada inteira se repete, ao custo de 8–10 min cada vez. Medido em 19–20/11/2026 sobre `c9733ce`: com 12 threads, **4 inconclusivas em 10** rodadas de código limpo; com o paralelismo limitado a 6, **0 em 6**, com tempo parecido. Os números, o estado e o que é estimado estão no design.

É a primeira das changes de barateamento da suíte: a mais barata, sem mudar nenhum teste.

## What Changes

- **`apps/api` (testes):** um `xunit.runner.json` no `Buteco.Api.Tests`, copiado para a saída, com `maxParallelThreads: 6`. Também liga `diagnosticMessages`, para todo log de suíte registrar o número de threads com que rodou.
- **`docs/development.md`, "Rodar os testes":**
  - o valor, o motivo e onde foi calibrado: uma máquina de 12 núcleos, com a VM do Podman de 6 CPUs e 6 GiB;
  - que a decisão é provisória, e o gatilho de recalibração;
  - como cada baseline e cada fechamento do `apps/api` passam a registrar as threads e o veredito de cada rodada (válida ou inconclusiva).
- **Nenhum teste muda**, nenhum caso sai, nenhuma asserção muda. **Nenhum código de produção muda.**
- **Fora:** `apps/workers`, `tests/` e os outros apps. Lá o paralelismo não foi medido.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `api-scaffold`: um requisito novo, **sem o número**. O limite de paralelismo da suíte do `apps/api` é declarado num arquivo do projeto, e o valor efetivo é verificável no registro da execução (decisão do dono, 20/11/2026; design, D6).

O número de threads não entra no contrato. Ele é uma calibração provisória desta máquina, com gatilho de recalibração (design, D4), e muda sem mudar a spec.

## Impact

- `apps/api/tests/Buteco.Api.Tests/xunit.runner.json` (novo) e `Buteco.Api.Tests.csproj` (copiar o arquivo para a saída).
- `docs/development.md`: a seção "Rodar os testes".
- `01-ARQUITETURA_E_CONVENCOES.md`, convenção 19: um parágrafo com a regra do registro por rodada da suíte do `apps/api` (design, D4).
- `openspec/specs/api-scaffold/spec.md`: o requisito novo, pelo sync do archive.
- Os scripts locais de suíte (fora do repositório, `~/.cache/buteco-agents/bin/suite-NNN.sh`) passam a gravar a linha das threads no registro de estado, e o cabeçalho do `suite-302.sh` cita a regra. É prática, não código do repositório.
- Duração esperada da suíte do `apps/api`: parecida com a de hoje (D2). O ganho esperado é em rodadas válidas, não em tempo.
