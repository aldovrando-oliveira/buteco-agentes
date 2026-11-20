## Context

A suíte do `apps/api` (`apps/api/tests/Buteco.Api.Tests`, 1861 casos em 225 classes) roda hoje sem nenhuma configuração de runner: não há `xunit.runner.json` nem `.runsettings`. O xUnit **2.9.3** (`Directory.Packages.props`) usa então o padrão:

- cada classe é uma coleção;
- coleções em paralelo até o número de threads de CPU (**12** nesta máquina);
- as classes de uma mesma coleção rodam em série.

Quase toda classe da suíte sobe uma fixture própria: Postgres do Testcontainers, `MigrateAsync` inteiro e boot do host por `WebApplicationFactory`. Os eventos do Podman contaram **~170 contêineres Postgres por execução**. Com 12 coleções ao mesmo tempo, a VM do Podman (6 CPUs) e a CPU do Mac saturam, e o `CPU_Speed_Limit` cai durante a própria suíte. Os casos que medem prazo reprovam, e pela regra assimétrica (#181) a rodada é **inconclusiva** e se repete inteira.

**Fontes:**

- as medições abaixo, de um estudo de suíte mais rápida feito em 19–20/11/2026 numa branch descartável, fora do `main`, sobre `c9733ce`;
- o padrão do xUnit lido na documentação oficial (`xunit.net/docs/config-xunit-runner-json`, página de 09/10/2026);
- os dois casos que reprovam: `Identity/Federated/FederatedProviderUnreachableTests.cs` l. 61–74 e `Identity/IdentityKeyRotationTests.cs` l. 51–94 e 126–135;
- `apps/api/src/Buteco.Api/Identity/Federated/FederatedLogin.cs` l. 29–33 e `FederatedChallenge.cs` l. 20–28.

### Medições que justificam a change

**Estado da máquina em todas as rodadas:**

- macOS, 12 núcleos, 16 GiB; VM do Podman (`applehv`) com 6 CPUs e 6 GiB;
- `buteco-agents_postgres_1` e `buteco-agents_rabbitmq_1` do compose de pé, WAHA parado;
- uma suíte por vez pelo `~/.cache/buteco-agents/suites.lock`, compartilhado com outra sessão;
- antes de cada rodada, espera do `CPU_Speed_Limit` = 100 e do load de 1 min < 8; durante a rodada, a CPU amostrada a cada 5 s;
- `DOCKER_HOST`, `TESTCONTAINERS_RYUK_DISABLED=true`, `TZ=America/Sao_Paulo` e `DOTNET_SYSTEM_NET_DISABLEIPV6=1` exportados.

O número de threads foi aplicado por runsettings (`dotnet test … -- xUnit.MaxParallelThreads=N xUnit.DiagnosticMessages=true`) e **conferido no log** pela linha `parallel test collections = on [N threads]`. Veredito pela regra assimétrica: verde é válida, mesmo com queda de CPU; reprovação com a CPU abaixo de 100 é inconclusiva.

**[M] Suíte do `apps/api`, código limpo de produção.** Em algumas rodadas havia provas de conceito de teste ligadas; elas estão na coluna "Variante".

| Threads | Variante | Duração | CPU mín. | Veredito |
|---|---|---|---|---|
| 12 (padrão) | nenhuma | 598,6 s | 30 | **inconclusiva** (`FederatedProviderHangingTests`) |
| 12 | nenhuma | 574,1 s | 39 | verde |
| 12 | nenhuma | 482,6 s | 41 | **inconclusiva** (`FederatedProviderHangingTests`) |
| 12 | nenhuma | 510,9 s | 37 | **inconclusiva** (`IdentityKeyRotationTests`) |
| 12 | Postgres compartilhado | 424,6 s / 362,4 s | 37 / 46 | verde / verde |
| 12 | host derivado por classe | 374,6 s / 408,4 s | 46 / 44 | verde / verde |
| 12 | as duas | 304,3 s | 46 | **inconclusiva** (`FederatedProviderHangingTests`) |
| 12 | as duas | 310,4 s | 44 | verde |
| **6** | nenhuma | **509,2 s / 484,8 s** | 48 / 46 | verde / verde |
| 6 | Postgres compartilhado | 415,2 s / 391,5 s | 46 / 51 | verde / verde |
| 6 | as duas | 335,4 s / 338,4 s | 48 / 39 | verde / verde |
| 4 | nenhuma | 586,3 s / 533,5 s | 53 / 53 | verde / verde |

As rodadas de 4, 6 e 12 threads "nenhuma" (quatro primeiras linhas e as de 6 e 4) foram **intercaladas** (4, 6, 12, 4, 6, 12), para as três sofrerem as mesmas condições da máquina.

**Totais [M]:**

- 12 threads: **4 inconclusivas em 10**;
- 6 threads: **0 em 6**;
- 4 threads: 0 em 2, e mais lento.
- Sem variante, a duração com 6 threads (485–509 s) fica dentro da faixa de 12 (483–599 s).

**O que essas medições não dizem:**

- **A diferença não é significativa sozinha.** Teste exato de Fisher sobre 0/6 contra 4/10: **p ≈ 0,12 unilateral e ≈ 0,23 bilateral**. O que sustenta a decisão é a amostra junto com o mecanismo (abaixo), não a estatística.
- **A CPU mínima não mudou de forma clara:** 39–51 com 6 threads, 30–46 com 12. O estrangulamento é térmico, e a CPU cai mesmo com carga menor. A hipótese é que 6 coleções deixam mais folga para os casos de prazo dentro da mesma CPU estrangulada, e não que a CPU caia menos.
- **Só o `apps/api` foi medido.** Nada aqui vale para o `apps/workers` (que já serializa as classes de host por coleção) nem para `tests/`.

### Os dois casos que reprovam

- **`FederatedProviderHangingTests.TheStart_WithTheProviderSilent_GivesUpAtTheExplicitTimeout`.** O provedor
  não responde, e o início do fluxo tem de desistir no prazo **explícito** (`FederatedLogin.ProviderTimeout`,
  10 s) e não no das tentativas do cliente OIDC, que "passava de 15 s" (comentário de `FederatedLogin.cs`).
  - A asserção é `Assert.InRange(watch.Elapsed, 10 s − 1 s, 10 s + 3 s)`. Nas reprovações, o caso levou 13–14 s.
- **`IdentityKeyRotationTests.Rotation_PublishesAhead_SwitchesWithoutRestart_KeepsTheOldKeyFor24Hours`.** Ele
  avança o relógio falso e espera, **em tempo real e com até 15 s**, o `BackgroundService` do anel produzir o
  efeito.

Os dois medem prazo de verdade, e por isso são os primeiros a reprovar quando a CPU encolhe.

## Goals / Non-Goals

**Goals:**

- Reduzir as rodadas inconclusivas da suíte do `apps/api` sem mudar nenhum teste.
- Deixar o número de threads **visível em todo log de suíte**, para cada número medido carregar o estado em que foi medido (convenção 22).
- Registrar, a partir desta change, o veredito de cada rodada do `apps/api` nas baselines e fechamentos. A decisão é provisória, e só esse registro permite confirmá-la ou revertê-la.
- Fixar o gatilho de recalibração.

**Non-Goals:**

- `apps/workers`, `tests/`, `apps/inbox`, `apps/connectors` e `apps/frontend`.
- Tornar a suíte mais rápida. O tempo esperado é o de hoje.
- Postgres compartilhado, host derivado por classe, camada rápida, `Trait` e script de seleção. São changes próprias; a do host derivado vem logo depois desta.
- Mexer nos dois casos de prazo (D5).

## Decisions

### D1. `maxParallelThreads: 6`, num `xunit.runner.json` do projeto de teste

O arquivo fica na raiz de `apps/api/tests/Buteco.Api.Tests` e é copiado para a saída, como manda a documentação:

```json
{
  "$schema": "https://xunit.net/schema/current/xunit.runner.schema.json",
  "maxParallelThreads": 6,
  "diagnosticMessages": true
}
```

```xml
<ItemGroup>
  <Content Include="xunit.runner.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

**Alternativas:**

- **Por runsettings, como no estudo:** descartada. Exige passar o argumento em todo `dotnet test`, ou um `.runsettings` que alguém precisa lembrar de citar. O arquivo do projeto vale para qualquer forma de rodar (`dotnet test` do projeto, da solução, IDE).
- **No `Directory.Build.props`:** descartada. Valeria para todos os projetos de teste, e só o `apps/api` foi medido.
- **`"0.5x"`, metade das threads de CPU:** descartada. Na máquina medida dá os mesmos 6, mas amarra o valor aos núcleos do Mac, quando o recurso que satura é a **VM do Podman, de 6 CPUs**, onde cada classe sobe o seu Postgres. Um Mac com mais núcleos e a mesma VM subiria o paralelismo sem a VM acompanhar. O número fixo fica amarrado a uma medição, e o gatilho da D4 o recalibra.
- **4 threads:** descartada. Mais lento (533–586 s), sem ganho de estabilidade sobre 6 na amostra.

**O `parallelAlgorithm` fica no padrão (`conservative`)**, o mesmo em que o estudo mediu. A documentação diz que ele só vale com o paralelismo de coleções ligado e um limite de threads, que é o caso daqui.

### D2. O efeito esperado é em validade, não em tempo

Com 6 threads, a duração medida ficou dentro da faixa de 12. **A change não promete suíte mais rápida.** Promete menos rodadas repetidas. Uma rodada inconclusiva custa uma suíte inteira (8–10 min), então, na média, a rodada válida sai mais barata mesmo com a mesma duração.

### D3. `diagnosticMessages: true`, para o número de threads ir junto do número medido

Com a opção ligada, o runner imprime na partida do assembly a configuração com que rodou:
`Starting: Buteco.Api.Tests (parallel test collections = on [6 threads], …)`.

- **O custo** são algumas linhas por execução.
- **O ganho:** é a conferência de que o arquivo foi lido (o estudo nunca usou o arquivo, só runsettings), e é o estado colado ao número que a convenção 22 pede, sem depender de o script de suíte lembrar.

**O que a documentação não respondeu, e as conferências 2.1–2.4 das tarefas checam na prática:**

1. **Em que verbosidade do `dotnet test` a linha aparece.** No estudo ela apareceu com `--logger "console;verbosity=detailed"`. Se não aparecer na saída padrão, o `docs/development.md` registra a forma que mostra, e os scripts de suíte passam a usá-la.
2. **Quem vence quando o arquivo e o runsettings definem o mesmo valor.** A página de runsettings da documentação não respondeu: 404 em 20/11/2026. Isso importa para quem quiser recalibrar por linha de comando. A tarefa roda com o arquivo em 6 e `-- xUnit.MaxParallelThreads=4`, e registra qual dos dois aparece na linha.

### D4. A decisão é provisória: registro por rodada, e gatilho de recalibração

**O registro, a partir desta change:**

- toda baseline e todo fechamento que rodar a suíte do `apps/api` registra, **por rodada**: as threads (da linha da D3), o veredito (`APROVADA`, `REPROVADA (válida)` ou `INCONCLUSIVA`), a duração e a CPU mínima;
- no `02`, a entrada de cada change traz a linha "`apps/api`: N rodadas, M inconclusivas, 6 threads";
- a regra fica escrita no `docs/development.md`, na seção "Rodar os testes", porque é ela que diz como rodar a suíte. O `02` acumula a série.

**Onde a regra fica para a próxima change encontrá-la.** Proponho dois lugares, além do `docs/development.md` e do `02`:

1. **No `01`, na convenção 19**, num parágrafo novo junto do que já manda a baseline e o fechamento guardarem a saída completa. O motivo: é o documento que toda proposta lê antes de escrever o `tasks.md` (CONTRIBUTING, "Antes de escrever código"). E a própria convenção 19 já diz que esse tipo de regra **"pertence ao `tasks.md` da change, na tarefa de fechamento, e não à disciplina de quem roda"**: escrita no 01, ela vira tarefa da próxima change pelo caminho que as outras regras de baseline já fazem. O `docs/development.md` diz **como** rodar; quem escreve `tasks.md` não tem motivo para abri-lo.
2. **No cabeçalho do script de suíte** (`suite-302.sh`, e por cópia nos seguintes): o script grava a linha das threads e o veredito no `-estado.txt`. É o carregador prático, porque cada change copia o script da anterior. **Não basta sozinho:** o script vive em `~/.cache/buteco-agents/bin`, fora do repositório e fora da revisão. Outra máquina ou outra pessoa não o tem.

**Não proponho** um modelo de `tasks.md` versionado (não existe hoje na base). Criá-lo é decisão maior que esta change.

**O gatilho, com condição observável (convenção 22)**, qualquer um dos três:

1. **2 inconclusivas nas últimas 10 rodadas da suíte do `apps/api`** com 6 threads, contadas na série do `02`, com o nome do caso que reprovou em cada uma. Uma inconclusiva isolada é registrada e não dispara (decisão do dono, 20/11/2026): a taxa medida com 12 threads foi 4/10, e com 6 foi 0/6. Duas em dez já fica na metade do caminho entre as duas;
2. **uma rodada válida acima de ~10 min (600 s)**, com o estado da máquina ao lado. As válidas medidas ficaram em 485–509 s;
3. **mudança na máquina de referência:** número de núcleos do Mac, CPUs ou memória da VM do Podman, ou versão do xUnit.

**O que o gatilho dispara:** uma medição nova intercalada, como a da tabela acima (4, 6 e 12, duas rodadas cada), e a decisão de manter, mudar ou voltar ao padrão. Ela não dispara uma mudança automática do valor.

**O registro do gatilho:** no fechamento, entra nos itens em aberto do `02`, com a referência às medições acima. É o lugar onde esta base mantém as réguas medidas e os seus gatilhos (`WorkerHostCollection`, régua de contenção do `apps/api`).

### D5. Alternativa registrada: tratar os dois casos de prazo em vez do paralelismo (descartada agora)

- **`FederatedProviderHangingTests`.** Alargar o teto da faixa (hoje prazo + 3 s) até caber a contenção medida
  (13–14 s) aproxima o teto do tempo das tentativas do cliente ("passava de 15 s"). E o que o caso guarda é
  justamente a diferença entre os dois.
  - Com um teto de 15 s, o caso não distinguiria mais "desistiu no prazo explícito" de "desistiu pelas
    tentativas". Seria afrouxar o guarda.
  - **Descartada.**
- **`IdentityKeyRotationTests`.** Alargar os 15 s de espera esconde a contenção sem tratá-la. O caso reprovou
  uma vez em 10.
  - **Descartada agora.** Se reprovar com 6 threads, é o primeiro gatilho da D4, e a alternativa volta a ser
    avaliada com os dados novos.
- **O motivo geral:** os dois são só os casos de prazo **que já reprovaram**. Tratar a causa comum (contenção
  que a própria suíte gera) protege também os que ainda não reprovaram. Tratar caso a caso seria repetir o
  ajuste a cada novo caso de prazo.

### D6. Um requisito no `api-scaffold`, sem o número (decisão do dono, 20/11/2026)

A change não muda comportamento do sistema; muda a calibração do runner de teste de um app. O `openspec validate` exige delta (ver "Validação"). O dono escolheu o padrão da `fixtures-token-emissor` (#236): um requisito da própria suíte no `api-scaffold`, a spec que já guarda os "testes automatizados" do `apps/api`.

**O requisito não carrega o número nem o mecanismo:**

- **o limite de paralelismo é declarado num arquivo do projeto**, e vale para qualquer forma de rodar a suíte;
- **o valor efetivo de uma execução é verificável no registro dela**, pela forma descrita em `docs/development.md`.

**Ele continua verdadeiro se a conferência 2.4 desligar o `diagnosticMessages`:** aí a forma descrita passa a ser o argumento de runsettings que liga as mensagens só na execução, e o requisito não muda. O valor 6 pode ser recalibrado (D4) sem tocar a spec.

**O cenário "sem o arquivo, o registro mostra o padrão"** é o que a conferência 2.2 prova contra o defeito (convenção 15).

### Validação [M, 20/11/2026, nesta proposta]

- **Sem delta** (primeira versão da proposta): `openspec validate testes-api-paralelismo` reprovou com e sem `--strict` (`Change must have at least one delta. No deltas found.`). O `validate` desta versão do OpenSpec exige delta mesmo no modo não estrito, e não só no `--strict` como a #236 registrou.
- **Com o delta da D6:** `Change 'testes-api-paralelismo' is valid`, com e sem `--strict`. A tarefa 4.3 refaz o `validate` no fechamento.

## Árvore de arquivos

```
apps/api/tests/Buteco.Api.Tests/
├── Buteco.Api.Tests.csproj      (muda: o Content do xunit.runner.json)
└── xunit.runner.json            (novo)
docs/
└── development.md               (muda: "Rodar os testes")
01-ARQUITETURA_E_CONVENCOES.md   (muda: convenção 19, o registro por rodada)
openspec/changes/testes-api-paralelismo/
├── proposal.md
├── design.md
├── tasks.md
└── specs/api-scaffold/spec.md   (requisito novo, sem o número)
```

Nenhum arquivo em `apps/api/src`, `libs/`, `tests/` nem nos outros apps.

## Risks / Trade-offs

- **[A amostra não é significativa sozinha (p ≈ 0,12–0,23)]** → a baseline e o fechamento desta change rodam a suíte do `apps/api` **duas vezes com 12 threads e duas com 6**, intercaladas, e somam às medições acima. O registro da D4 continua a série nas próximas changes. A decisão é revista pelo gatilho, não por impressão.
- **[O valor é calibrado nesta máquina]** → o `docs/development.md` diz em que máquina e VM. O gatilho 3 da D4 cobre a troca de máquina.
- **[O arquivo não ser lido (nome, cópia para a saída) e o runner seguir com 12 em silêncio]** → as conferências 2.1 e 2.2 checam a linha `[6 threads]` no log da change e a linha `[12 threads]` com o arquivo fora; a baseline (0.3) mostra `[12 threads]`. Com o `diagnosticMessages` ligado, toda execução seguinte mostra a linha.
- **[Um runsettings passado na linha de comando sobrepor o arquivo sem ninguém notar]** → a linha da D3 mostra o valor efetivo, e a conferência 2.3 registra a precedência.
- **[Menos paralelismo esconder um defeito de concorrência entre classes]** → as classes não compartilham estado entre si (cada uma tem a sua fixture). A comparação por nome do fechamento confere que os mesmos 1861 casos rodam e passam.
- **[Ruído do `diagnosticMessages` no log]** → algumas linhas por execução. Se a conferência 2.4 mostrar mais que isso, o valor volta a `false`, e a linha passa a ser pedida pelos scripts por runsettings.

## Migration Plan

Não há migração nem deploy: é configuração de teste. Para voltar atrás, basta apagar o `xunit.runner.json` e a linha do `.csproj`.

## Open Questions

Nenhuma de produto. As duas dúvidas técnicas (a verbosidade da linha e a precedência entre arquivo e runsettings) são as conferências 2.1 e 2.3, não decisões.

## O que o apply mudou (20/11/2026)

- **A forma descrita do requisito é o `.trx`, e não o console** (D3, conferência 2.1). Na saída padrão do `dotnet test`, a linha das threads não aparece. Com `--logger "console;verbosity=normal"`, aparece. Mas o `.trx` grava a linha **em qualquer verbosidade**, no bloco de saída da execução. O `docs/development.md` e os scripts leem as threads de lá.
- **A linha de comando vence o arquivo** (D3, conferência 2.3): com o arquivo em 6 e `-- xUnit.MaxParallelThreads=4`, a execução roda com 4. Ficou escrito no `docs/development.md` como forma de medir.
- **O `diagnosticMessages` fica `true`** (conferência 2.4): ele acrescenta 6 linhas por execução.
- **A série desta change somou uma inconclusiva com 12 threads** (fechamento 2, `FederatedProviderHangingTests`, CPU mín. 48), e nenhuma com 6. Somada à do design, 12 threads ficou com 5 inconclusivas em 12 e 6 threads com 0 em 8. O Fisher exato sobre a série somada dá p ≈ 0,05, unilateral e bilateral; com a série do design, era 0,12 e 0,23. A D2 se confirmou: com 6, 456–458 s; com 12, 446–502 s.
- **O `suite-302.sh` espera o lock em vez de recusá-lo**, porque outra sessão usava o mesmo `suites.lock` durante a change. É diferença de script local, não do repositório.

