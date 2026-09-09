## Context

O repositório vai passar a ser tratado como open-source. Hoje a documentação
tem três problemas de naturezas diferentes, e cada um pede um remédio
diferente.

**Ausência legal.** Não há `LICENSE`. Sem ele, um repositório público não é
open-source: todo direito permanece reservado ao autor por padrão, e ninguém
pode legalmente usar, modificar ou redistribuir o código.

**Sobrecarga do `README.md`.** Com 548 linhas e 27 KB, ele acumula nove
papéis: pitch, tabela de stack, árvore de diretórios, pré-requisitos,
troubleshooting de Podman, subida de infraestrutura, subida de quatro apps,
receitas de `curl`, execução de testes, build de cinco imagens Docker e
convenções de código. Quem chega ao repositório não descobre o que o projeto
é nos primeiros trinta segundos — descobre a versão do .NET.

**Apodrecimento silencioso.** O conteúdo divergiu do código sem que ninguém
percebesse, e a divergência é verificável hoje:

| Afirmação no `README.md` | Realidade |
|---|---|
| "Monorepo com três apps independentes" | são quatro: `api`, `workers`, `frontend`, `inbox` |
| "`apps/frontend` — ainda não consome o backend" | consome tudo; 451 testes; oito etapas de redesenho |
| "Fora de escopo por enquanto: MCP, adapters de canal, autenticação, push notification, e qualquer UI" | todos entregues e arquivados |

Mais cinco de seis links para `openspec/` quebrados, todos pela mesma causa
mecânica: foram escritos durante a change, apontando para
`openspec/changes/<nome>/design.md`, e o arquivamento move a pasta para
`openspec/changes/archive/AAAA-MM-DD-<nome>/`. O mesmo defeito aparece no
`.env.example`. E o `openspec/config.yaml` — que alimenta agentes ao gerar
artefatos de toda change futura — ainda declara "três apps" e "ChatWoot e
Waha" como canais suportados, quando os adapters reais são WAHA e Telegram.

**Restrição inegociável.** `01-ARQUITETURA_E_CONVENCOES.md` (34 KB) e
`02-HISTORICO_E_STATUS.md` (92 KB) não podem ser alterados por esta change.
São simultaneamente a restrição mais dura e a melhor fonte de conteúdo
disponível: quase tudo que se quer publicar já está escrito ali.

## Goals / Non-Goals

**Goals:**

- Tornar o repositório legalmente open-source (Apache-2.0).
- Separar a documentação por audiência, de modo que cada leitor tenha um
  destino único e óbvio.
- Formalizar as premissas oficiais do projeto — estrutura, regras de
  negócio, frameworks e o "estilo da casa" — em documento público.
- Criar `CHANGELOG.md` sem fechar versão oficial.
- **Impedir a recorrência**: transformar a integridade da documentação de
  disciplina em invariante verificável.

**Non-Goals:**

- **Alterar `01-ARQUITETURA_E_CONVENCOES.md` ou `02-HISTORICO_E_STATUS.md`**,
  por qualquer meio — edição, movimentação, renomeação ou divisão.
- Configurar CI. Não existe `.github/workflows/` neste repositório, e os
  testes dependem de Docker/Podman via Testcontainers, o que torna o
  workflow não-trivial. É change separada.
- Fechar a versão `0.1.0` ou qualquer release.
- Traduzir specs, changes arquivadas ou histórico de commits.
- Produzir capturas de tela do painel. O repositório tem exatamente um
  asset (`favicon.svg`), e um README de plataforma com UI se beneficia
  muito de imagem — mas produzi-las exige subir o stack e é trabalho de
  outra natureza. Registrado em Open Questions.
- Adicionar headers de licença aos arquivos-fonte (ver Decisão 3).

## Decisions

### Decisão 1 — Apache-2.0, não MIT nem AGPL-3.0

Apache-2.0 é permissiva como a MIT, mas acrescenta concessão expressa de
patentes e exigência de registrar modificações. É a licença do próprio .NET,
o que a torna a escolha de menor atrito para uma base .NET, e é a preferida
por adoção corporativa.

*Alternativas consideradas:* **MIT** — mais curta e de maior adoção, mas sem
cláusula de patentes, o que numa plataforma de agentes de IA é uma lacuna
real. **AGPL-3.0** — protegeria contra um terceiro fechar um SaaS em cima do
projeto, mas afasta adoção corporativa e contribuição, custo alto para um
projeto que ainda não tem comunidade.

*Titular:* `Copyright 2026 Aldovrando Oliveira`. Confirmado explicitamente
pelo mantenedor — a grafia diverge do `git config user.name` do repositório
(`Aldo Oliveira`), e a divergência é intencional.

### Decisão 2 — A fronteira de idioma é por camada, não por arquivo

```
  INGLÊS   │ README.md · LICENSE · NOTICE · SECURITY.md · CODE_OF_CONDUCT.md
  vitrine  │ ── o que um estranho lê antes de decidir se entra
  + legal  │
───────────┼─────────────────────────────────────────────────────────────
  pt-BR    │ docs/** · CONTRIBUTING.md · CHANGELOG.md
  trabalho │ ── quem já entrou vai ler specs, commits e os 01/02 em
  real     │    português de qualquer forma
```

`CONTRIBUTING.md` e `CHANGELOG.md` ficam em pt-BR apesar de estarem na raiz
porque ambos são **derivados** de material em português: as 55 changes
arquivadas, o fluxo OpenSpec, os arquivos 01 e 02. Traduzi-los criaria uma
segunda fonte de verdade em outro idioma — exatamente o mecanismo de
divergência que esta change existe para eliminar.

O `README.md` declara a fronteira explicitamente
(*"Documentation and contribution guide are in Portuguese"*), de modo que
ninguém descubra o idioma clicando num link.

*Alternativa considerada:* tudo em inglês. Rejeitada porque criaria
dissonância permanente com os dois arquivos protegidos, as 55 changes
arquivadas e os 59 commits, todos em pt-BR — e a tradução teria de ser
mantida em sincronia com material que continua sendo escrito em português.

### Decisão 3 — Sem headers de copyright nos arquivos-fonte

A Apache-2.0 recomenda o header no apêndice do texto da licença, mas não o
exige. Num monorepo com centenas de arquivos `.cs`, `.ts` e `.tsx` isso é
invasivo, polui todo diff e, na prática, ninguém mantém. `LICENSE` +
`NOTICE` + seção no `README.md` são suficientes e é o que `dotnet/runtime` e
Mantine fazem.

### Decisão 4 — `docs/` é escrita para público externo; 01 e 02 permanecem internos

Três arranjos possíveis foram considerados:

```
(i) DERIVAR ..... docs/ é curadoria de 01, ambos vivos
                  ✅ docs/ completa    ❌ duas fontes de verdade divergindo
                                          — o defeito que já causou isso tudo

(ii) APONTAR .... docs/ fina, só link para 01
                  ✅ zero duplicação   ❌ expõe nomes numerados e 92 KB de
                                          histórico interno como doc oficial

(iii) SEPARAR ... docs/ escrita para fora, 01/02 como notas do mantenedor
      ▲ ESCOLHIDA
                  ✅ cada documento com seu público e seu tom
                  ❌ mais escrita nova; sobreposição de conteúdo permanece
```

O que torna (iii) sustentável apesar da sobreposição é o **fluxo em sentido
único**: 01 e 02 são fonte, `docs/` é destino, nunca o contrário. `docs/`
não cita histórico de changes nem decisões superadas; descreve o sistema
como ele é hoje, para quem nunca o viu. Isso é uma diferença de conteúdo, não
só de tom — e é o que impede as duas camadas de serem cópias uma da outra.

Os dois arquivos ficam **onde estão**, na raiz. O `README.md` os apresenta
em uma linha como *maintainer working notes*: nem escondidos, nem promovidos
a documentação oficial.

### Decisão 5 — O `README.md` é escrito por último

A ordem de implementação é deliberadamente invertida: conteúdo primeiro,
porta de entrada por último.

```
  1. docs/ técnica       development · configuration · deployment
            │
  2. docs/ premissas     architecture · conventions
            │
  3. CHANGELOG.md        [Unreleased] agrupado a partir do 02
            │
  4. Legal + contrib.    LICENSE · NOTICE · SECURITY · COC
                         CONTRIBUTING · .github/
            │
  5. README.md (EN)      + docs/README.md (índice)
                         ── todo alvo de link já existe neste ponto
```

O `README` é quem referencia todo o resto. Escrevê-lo primeiro significaria
criar links para arquivos inexistentes — que é, literalmente, o defeito
diagnosticado (cinco de seis links quebrados). A ordem espelha a convenção
de sequenciamento da casa (catálogo → vínculo → execução → UI): a camada de
apresentação vem depois do que ela apresenta.

Isto é ordenação **dentro de uma change**, não cinco changes. Não há
verificação intermediária que justifique fatiar, e fatiar deixaria o
repositório em estado incoerente entre etapas.

### Decisão 6 — Integridade de documentação é script, não prosa

Esta é a decisão central da change, e vem diretamente da convenção nº 8 do
`01-ARQUITETURA_E_CONVENCOES.md`:

> *"Checagem de integridade no startup é o padrão para todo registro que
> possa ficar incompleto em silêncio — não documentação em prosa."*

O que foi encontrado é precisamente um registro que ficou incompleto em
silêncio. Escrever "mantenha o README atualizado" no `CONTRIBUTING.md` é a
prosa que a convenção rejeita. O equivalente documental de
`ValidateRouteAuthenticationClassification` é um script que falha:

| Invariante | Detecta |
|---|---|
| Todo link relativo em `.md` resolve | o `README` atual, com 5 alvos inexistentes |
| Link para `openspec/changes/<nome>/` usa `archive/` quando arquivada | a causa mecânica dos 5 |
| Todo diretório em `apps/*` aparece em `docs/architecture.md` | "três apps" quando são quatro |
| `CHANGELOG.md` tem seção `[Unreleased]` | erosão do formato Keep a Changelog |

A convenção nº 8 também diz que a checagem vale nos **dois sentidos** — item
declarado sem contraparte real é tão problema quanto o inverso. Por isso a
terceira invariante compara `apps/*` contra o documento, não só o documento
contra si mesmo.

O script nasce rodável à mão, já que não há CI. O `CONTRIBUTING.md` deve
declarar isso honestamente, em vez de prometer verificação automática
inexistente.

**Correção durante a implementação — escopo da varredura.** A primeira
execução do script reprovou com 46 violações, das quais **30 dentro de
`openspec/changes/archive/`**. A causa é a mesma que este script existe para
pegar, em outra forma: um link escrito como `../../../apps/...` apontava para
a raiz enquanto a mudança vivia em `openspec/changes/<nome>/`, e o
arquivamento a moveu um nível mais fundo, fazendo todo caminho relativo cair
em `openspec/`. Os alvos existem; a profundidade é que mudou.

Essas 30 violações são **irreparáveis por construção**: mudanças arquivadas
são registro histórico congelado, e corrigir um link dentro de uma delas
falsificaria o que foi escrito naquele momento — a mesma objeção que protege
`01-ARQUITETURA_E_CONVENCOES.md` e `02-HISTORICO_E_STATUS.md`. Mantê-las na
saída treinaria qualquer pessoa a ignorar o relatório inteiro, que é o modo
como um guarda deixa de valer.

A varredura passou a cobrir apenas a **documentação viva** — raiz, `docs/`,
`.github/` e mudanças ativas. Depois da correção, as 13 violações restantes
são exatamente as fixáveis: as 11 do `README.md` (que o grupo 6 corrige) e
duas referências a `docs/README.md`, ainda não criado naquele momento.

Efeito colateral útil: a varredura encontrou **11** referências quebradas no
`README.md`, não as 6 do diagnóstico inicial. A diferença é que o diagnóstico
usou `grep` por links markdown terminados em `.md`, e o script também pega
referência a mudança arquivada em texto corrido — que quebra do mesmo jeito
para quem tenta seguir o caminho.

### Decisão 7 — `CHANGELOG.md` agrupa por linha de trabalho, não por change

Keep a Changelog pede entradas legíveis por humanos, agrupadas em
`Added`/`Changed`/`Fixed`. Listar as 55 changes arquivadas uma a uma
produziria um changelog de commits — ilegível e sem valor para quem chega.

O agrupamento usa as **linhas de trabalho** que o `02-HISTORICO_E_STATUS.md`
já emprega como unidade narrativa: catálogo de agentes, protocolo A2A, MCP,
delegação, inbox e adapters de canal, autenticação, contexto temporal e de
canal, containerização, redesenho do painel, base de conhecimento. A
estrutura já existe na fonte; o changelog a reaproveita.

Tudo em `[Unreleased]`, sem data e sem número de versão — nenhuma versão
oficial foi fechada, e esta change não fecha nenhuma.

### Decisão 8 — `deploy/runbook.md` move para `docs/deployment.md`

O runbook é documentação operacional e pertence ao índice de `docs/`, onde
quem vai operar o sistema procura. Verificado por busca: nenhuma spec viva em
`openspec/specs/` referencia `deploy/runbook.md`, e a spec `server-deployment`
especifica compose e imagens, não a localização da documentação. `deploy/migrate/`
permanece intacto.

### Decisão 9 — `apps/frontend/README.md` é removido, não reescrito

É o template cru gerado pelo Vite (`# React + TypeScript + Vite`, seções
sobre React Compiler e Oxlint), nunca editado. Num repositório público, um
README de template é ruído que sugere abandono. `docs/development.md` cobre o
assunto de forma correta, e um README por app não é padrão neste monorepo —
`apps/api`, `apps/workers` e `apps/inbox` não têm um.

Verificado: o arquivo não é referenciado por `package.json`, `Dockerfile` ou
configuração de lint. Nenhum build ou teste é afetado.

## Risks / Trade-offs

**[Duplicação entre `docs/` e os arquivos 01/02 volta a divergir]** →
Mitigação parcial pela Decisão 4 (fluxo em sentido único, conteúdo
deliberadamente diferente) e pela Decisão 6 (invariantes verificáveis). O
risco não é eliminado: um script consegue detectar link quebrado e app
ausente, mas não detecta afirmação semanticamente obsoleta como "ainda não
consome o backend". Risco aceito conscientemente — é a razão de `docs/`
descrever estado atual em vez de escopo e planos, que é a categoria de frase
que envelhece mal.

**[Fronteira de idioma vira caso a caso a cada arquivo novo]** → A Decisão 2
define o critério por camada (vitrine/legal vs. trabalho), não por arquivo,
e ele fica escrito em `docs/conventions.md` para decidir os casos futuros.

**[Sem CI, o script de integridade nunca roda]** → Risco real. O script é o
artefato durável desta change, mas depende de disciplina até existir CI.
Mitigação: `CONTRIBUTING.md` o cita como passo explícito de PR, e a change de
CI fica registrada como próximo passo natural.

**[Remoção de `apps/frontend/README.md` surpreende quem espera encontrá-lo]**
→ Baixo impacto; `docs/development.md` tem uma seção de `apps/frontend` e o
índice de `docs/` a torna localizável.

**[Reescrita do `README` perde conteúdo útil que só existia lá]** →
Mitigação: nada é apagado sem destino. Cada uma das nove seções atuais tem um
arquivo de `docs/` correspondente definido antes da reescrita; o
troubleshooting de Testcontainers com Podman, em particular, é conteúdo caro
e verificado empiricamente, e vai integral para `docs/development.md`.

## Migration Plan

Não há migração de dados, schema ou runtime — a change não toca código de
aplicação. A sequência de implementação é a da Decisão 5, e o critério de
conclusão é o script da Decisão 6 passando limpo.

Rollback é `git revert`: nenhum artefato desta change é consumido por
processo em execução. A única alteração com efeito fora da documentação é a
correção do bloco `context` em `openspec/config.yaml`, que passa a alimentar
agentes com informação correta — reverter apenas restaura a informação
errada, sem quebrar nada.

## Open Questions

- **Capturas de tela do painel.** O `README` ganharia muito com uma imagem,
  e o redesenho acaba de ser concluído em oito etapas. Fora de escopo aqui
  (exige subir o stack), mas vale como próximo passo — e define se será
  preciso um diretório de assets versionado.
- **`owner`/organização do GitHub.** O `NOTICE` e o `README` apontam para o
  repositório canônico, cuja URL ainda não existe. Fica como lacuna
  explícita a preencher na publicação.
- **Nome do projeto.** `README.md` diz "Buteco Agents",
  `01-ARQUITETURA_E_CONVENCOES.md` diz "Buteco Agentes", e
  `apps/frontend/package.json` diz `"frontend"` com versão `0.0.0`. Esta
  change padroniza a grafia nos documentos que cria (adotando
  **Buteco Agents**, do `README` atual), mas não altera o arquivo protegido
  nem o `package.json` — a inconsistência sobrevive em dois pontos.
- **CI.** Change seguinte natural, e pré-requisito para a Decisão 6 deixar
  de depender de disciplina.
