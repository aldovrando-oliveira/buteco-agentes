## Context

Etapa **5a-4** da linha de bases de conhecimento, e a última dela: o formulário
de base (`KnowledgeBaseForm`), em `apps/frontend`. Backend não é tocado, e não há
rota nem requisição nova.

É a change que dois itens abertos apontam. Até 13/09/2026 os dois diziam
*"gatilho: a próxima change que tocar `KnowledgeBaseForm`"*, pendurados na 5a-3 —
que é o catálogo e nunca abre o formulário. A 5a-3 corrigiu isso criando a 5a-4
com linha própria na fila, e esta change é o resultado.

**Base: `main` em `f925ebc`**, que já inclui `frontend-marca-visual` (PR #17).
Aquela change não toca nenhum dos quatro arquivos desta, e o que ela mudou em
`theme.ts` foi **acréscimo** (`--buteco-brand-ink`) — conferido, e importa porque
o resultado negativo da convenção 16 abaixo depende da tupla `yellow`, que ela
não tocou.

### Protótipos

`design/` traz os três arquivos que valem, com a conferência contra a conexão do
Claude Design (projeto `Sistema Gestão de Agentes`,
`e4f9bbd6-dd31-4ff1-954b-0e686d2eaa54`) feita **antes** de copiar:

| arquivo | o que é |
|---|---|
| `design/Buteco Agentes.dc.html` | Protótipo navegável, 163.947 bytes. |
| `design/support.js` | Runtime do protótipo. Sem ele o `.dc.html` não renderiza. |
| `design/CONHECIMENTO.md` | Especificação de design da linha, revisão 2 de 09/09/2026. |

**O que foi conferido, e o que NÃO foi — porque a diferença importa.** Os três
arquivos do projeto têm **exatamente** os tamanhos dos arquivados pela 5c
(163.947 / 69.150 / 17.656), e a região `isKbForm` do protótipo — linhas 878-912,
que é a tela inteira desta change — foi lida **pela conexão** e conferida
caractere a caractere contra a cópia arquivada: idêntica. O `etag` do
`.dc.html` no projeto (`1789243552588322`) é o **mesmo** da leitura que fez essa
conferência, o que prova que o arquivo não mudou entre uma coisa e outra. A cópia
local veio do arquivo da 5c. **Não** foi feita comparação byte a byte do arquivo
inteiro, que é o que a 5a-3 fez com `md5`; a afirmação aqui é mais fraca de
propósito, e está escrita para que ninguém a leia como a daquela change.

O projeto ganhou um arquivo novo desde então — `Marca Buteco Agentes.dc.html`,
286.825 bytes —, que é o protótipo da `frontend-marca-visual` e não toca esta tela.

### Conferência protótipo × código (convenções 6 e 17), feita ANTES das decisões

**Quantas das treze correções de protótipo tocam esta tela: zero.** Conferidas
uma a uma na lista viva de `02-HISTORICO_E_STATUS.md` — as três da 5a-3 e a de
tipo novo são do catálogo e da aba de diagnóstico; as duas da 5c são da aba de
diagnóstico; as da 5a-2 são da tabela de documentos e do modal de atualizar; as
cinco anteriores são da tabela de documentos e do modal de vincular. **Nenhuma é
do formulário**, e a lista já esteve dessincronizada uma vez, então isso foi lido
e não assumido.

Isso não significa que o protótipo esteja certo nesta tela — significa que os
achados dela ainda não tinham sido procurados. Esta change acha **dois**, e os
dois são de tipo diferente dos treze anteriores: eles não vêm do protótipo estar
errado sobre o sistema, vêm de o **sistema ter mudado depois** que a tela foi
implementada fielmente.

O que o protótipo faz na tela, lido em `design/Buteco Agentes.dc.html:877-911` e
no `kbFormVals` do mesmo arquivo (`:1818-1831`):

| protótipo | implementado hoje | situação |
|---|---|---|
| `kfChars: d.length + ' caracteres' + (… < 80 ? ' · curto demais para o modelo decidir com segurança' : '')` (`:1827`) | idêntico, com plural corrigido para 1 caractere | **muda aqui** (D3) |
| `kfCharsFg: … 'var(--wa)' : 'var(--mut2)'` (`:1828`) | `c={isShort ? 'yellow' : 'dimmed'}` | **fica** — ver o resultado negativo abaixo |
| `consultar_base — {{ kfPreviewName }}` (`:897`) | não implementado, com comentário declarando a recusa | **fica recusado**, motivo reescrito (D4) |
| bloco "Como o agente vê esta base" com nome e descrição | idêntico | **muda aqui** (D5) |
| `kfPreviewDesc: … 'Sem descrição: o modelo recebe só o nome…'` (`:1830`) | idêntico | **muda aqui**, terceiro membro da família do D5 |
| `display:flex;justify-content:space-between` com **um** filho (`:891`) | não replicado | sem consequência |

**Resultado negativo, registrado porque custou o mesmo que supor errado
(convenção 16):** `c="yellow"` no contador **não** é tom fixo da escala neutra.
`yellow` é cor declarada em `theme.ts:53` com tupla própria, tem guarda de
contraste em `theme.test.ts` (valor **3,72**, hoje na linha 193 — a linha se move,
o valor não, e é o valor que a citação carrega) e é o idioma da casa para este
papel em sete outros pontos do painel (`AgentTable.tsx:46`,
`AgentDelegationsTab.tsx:124`, `AgentMcpServerRow.tsx:85`, entre outros). O gatilho
da convenção 16 é papel que troca de **ponta** da escala entre esquemas; não é o
caso. Nenhuma variável nova nasce aqui.

### O que a etapa 4 definiu, lido no código

`KnowledgeToolSetResolver.cs:81`:

```csharp
var toolName = ToolNameSanitizer.Sanitize($"{ToolNamePrefix}{ToolNameSlugifier.Slugify(binding.Name)}");
tools.Add(BuildSearchTool(toolName, agentId, binding.Id, binding.Name, binding.Description));
```

com `ToolNamePrefix = "search_"` (`:20`, `private const`), `ToolNameSlugifier`
removendo diacríticos via `NormalizationForm.FormD` e produzindo kebab-case, e
`ToolNameSanitizer` trocando o que sobra fora de `[a-zA-Z0-9_-]` por `_`,
prefixando `_` se o primeiro caractere não servir, e truncando em 64.

E `KnowledgeToolDescription.Build` (`KnowledgeToolDescription.cs:52`):

```csharp
$"Busca trechos na base de conhecimento '{knowledgeBaseName}'. "
+ $"Conteúdo da base: {knowledgeBaseDescription} "
+ ComoLerOResultado;
```

onde `ComoLerOResultado` é um bloco fixo de 441 caracteres, igual para toda base,
com guarda de asserção negativa próprio em `KnowledgeToolSetResolverTests`.

## Goals / Non-Goals

**Goals:**

- Pôr na tela o critério que `0d` mediu — delimitação e competição entre bases —
  dentro do andaime que a 5a-1 já construiu.
- Fazer o aviso de comprimento parar de afirmar qualidade, sem removê-lo.
- Fechar o item do nome efetivo da tool com decisão e motivo verificados, mesmo
  que a decisão seja não exibir.
- Corrigir as afirmações que a etapa 4 tornou falsas, no formulário e no detalhe.

**Non-Goals:**

- **Validação bloqueante de generalidade.** Não existe regra automática que
  distinga descrição específica de genérica.
- **Aviso automático de generalidade**, mesmo não-bloqueante — mesma razão, e é a
  frase que o item aberto existe para impedir.
- **Exibir o sintoma de base-atrator na tela.** Ver D2.
- **Mexer em `apps/workers` ou `apps/api`.** Nada do que esta change descobriu
  pede mudança de backend.
- **Exibir o nome da tool**, pretendido ou efetivo. Ver D4.
- **Corrigir `format:check` fora do que esta change abre.** Ver D6.

## Decisions

### D1 — A orientação vive como texto de apoio permanente, no bloco que já existe

**Decisão:** a cópia nova entra no parágrafo de apoio do bloco da descrição — o
`<Text size="xs" c="dimmed">` que já está lá —, acrescentando duas coisas que ele
não diz: **dizer do que a base não trata**, e que **descrição genérica atrai
perguntas de outras bases**. Nada condicional, nada calculado.

**Por quê, e as três alternativas descartadas:**

- **Aviso condicional** (só aparece em certas descrições) exige um predicado. O
  único predicado disponível é comprimento, e `0d` mediu que ele não é o sinal.
  Qualquer outro seria inventado. Convenção 13, na forma exata que o item aberto
  nomeia.
- **Bloco explicativo próprio** poria um terceiro bloco de prosa numa tela que já
  tem dois (a orientação da descrição e a nota de documentos). O texto novo é
  **refinamento do critério** que o parágrafo existente já tenta dar — ele já diz
  "diga que assunto está aqui e em que situação consultar" e já manda evitar
  "documentos diversos". Separá-lo criaria dois lugares dizendo como escrever o
  mesmo campo, que é a duplicação que a 5c recusou em D10.
- **Deixar como está**, apostando que "evite 'documentos diversos'" já cobre. Não
  cobre, e isso é medido: as sete descrições de `0d` iam de 250 a 427 caracteres
  e nenhuma era do tipo "documentos diversos" — a atratora cobria seis assuntos
  nomeados. O que falta ao parágrafo não é "escreva mais", é **escreva
  delimitado**, e a cópia atual orienta na direção contrária.

**O que a cópia nova afirma, e é tudo verificável:** que o texto é lido pelo
modelo (já afirmado, e é verdade — `KnowledgeToolDescription.Build`), que dizer o
que a base não cobre ajuda o modelo a não escolher errado, e que bases irmãs
competem entre si. Nenhuma delas é um veredito sobre o texto que o operador
escreveu.

### D2 — O sintoma de base-atrator fica no registro, não na tela

**Decisão:** a assinatura *"uma base chamada muito acima da sua fatia de
intenções"* não ganha lugar na tela. Fica em `02-HISTORICO_E_STATUS.md` como o
que procurar ao diagnosticar em uso real.

**Por quê, conferido e não suposto:** a comparação precisa de duas metades, e o
sistema não tem nenhuma das duas persistidas.

- **"Quantas vezes esta base foi chamada"** — não existe. `apps/api/.../KnowledgeBases/`
  tem `Commands`, `Endpoints`, `Entities`, `Queries`, `Requests`, `Responses`, e
  nenhum contador de consulta. A spec de `knowledge-tool-execution` prevê registro
  em **log** (*"um registro de log nomeia o…"*), não contagem persistida.
- **"Quantas perguntas eram dela"** — é rótulo humano sobre um corpus de
  avaliação. `0d` o produziu à mão para medir; não é dado de produção.

O próprio protótipo já escreve a recusa na tela vizinha, e ela vale igual aqui:
*"o sistema … não coleta consultas por base — por isso não há barra de progresso
nem número de acessos nesta tela"*. Exibir o sintoma exigiria backend novo, e
backend feito de improviso dentro de change de tela é o que a convenção 1 proíbe
por corolário.

**O que isso deixa em aberto, com gatilho:** se algum dia houver contagem de
invocação por tool — que é vizinha do item já aberto de *"a renomeação por colisão
é invisível na UI"* —, as duas se resolvem juntas, e aí a tela tem o que exibir.

### D3 — O aviso de comprimento fica, e a cópia dele para de afirmar qualidade

**Decisão:** `SHORT_DESCRIPTION_THRESHOLD = 80` continua, o contador continua, a
cor de aviso continua. Muda **o que o sufixo diz**. Hoje:

> `{n} caracteres · curto demais para o modelo decidir com segurança`

"para o modelo decidir com segurança" é um veredito sobre a **decisão do modelo**,
e o comprimento não o sustenta — `0d` mediu 61,0% de acerto de roteamento com
descrições de 250 a 427 caracteres, todas muito acima do limiar. A frase promete
que passar dos 80 compra segurança, e não compra.

O sufixo novo diz o que o limiar de fato mede: que abaixo disso dificilmente cabe
o assunto **e** a delimitação. É piso de "escreveu alguma coisa", explicitamente.

**Alternativa descartada — remover o aviso.** Foi considerada, porque um guarda
que mede a dimensão errada é pior que nenhum. Recusada: o aviso não é falso sobre
**o que mede**, é falso sobre **o que promete**, e descrição de 19 caracteres
continua sendo um problema real (curta faz o modelo não chamar). Corrigir a
promessa custa uma linha; remover jogaria fora um piso útil e deixaria o campo sem
nenhum retorno.

**Alternativa descartada — mover o limiar para cima.** Não há número a mover para:
`0d` não mediu um comprimento que separe boas de ruins, e mediu o contrário —
a pior tinha 421. Subir o limiar seria inventar um corte, que é a mesma forma da
proibição escrita em `KnowledgeToolDescription.ComoLerOResultado` (*"não
acrescentar aqui nenhum valor numérico de corte"*).

### D4 — O nome da tool continua não exibido, com o motivo reescrito

**Decisão:** nem o pretendido, nem o pretendido com ressalva. Nada. E o comentário
de `KnowledgeBaseForm.tsx` que recusa `consultar_base` é **atualizado, não
apagado**, porque a recusa nova tem motivo diferente da antiga.

**A premissa antiga era metade certa, e a metade que sobrou é a que decide.** O
comentário diz: *"nome de tool de base de conhecimento é decisão da etapa 4
(resolvedor de tool) e passa pelo `ToolNameDeduplicator` — não existe em spec
nenhuma hoje"*. O item aberto afirma que *"a premissa do comentário deixou de
valer"*. Conferido contra a árvore, e **só a primeira cláusula deixou de valer**:

- *"é decisão da etapa 4"* — resolvida. A etapa 4 decidiu, em código.
- *"não existe em spec nenhuma hoje"* — **continua verdadeira**. `grep` por
  `search_` em `openspec/specs/knowledge-tool-execution/spec.md` não devolve nada:
  os seis requisitos daquela spec exigem tools **distintas** e ordem determinística
  *"independente do nome da base"*, e deliberadamente não fixam o formato. O
  formato é um `private const` de implementação.

Isso é a convenção 6 na forma que ela mesma nomeia — a frase em prosa de um item
aberto que descreve o que o código faz, e que ninguém abre o arquivo para checar.
O item está certo no que importa (a 5a-4 é o lugar) e errado no detalhe que
decidiria a questão sozinho.

**Três razões independentes, e cada uma basta:**

1. **Nenhuma spec fixa o formato.** Exibi-lo faria a UI virar a spec de fato de um
   formato que nenhuma spec fixa — e a próxima change de `apps/workers` que mudasse
   o prefixo quebraria a tela sem nenhum teste reprovando nos dois lados.
2. **Não há fonte reusável no frontend.** `ToolNameSlugifier` e
   `ToolNameSanitizer` são C# em `apps/workers`, e não há rota de `apps/api` que
   devolva nome de tool (`grep -rni "toolname"` em `apps/api/src` devolve só
   `AvailableToolNames` da descoberta MCP). A tela teria de reimplementar em
   TypeScript a remoção de diacríticos por `FormD`, a substituição e o truncamento
   em 64 — segunda fonte de verdade para uma regra que já mordeu esta base uma vez
   (o padrão `Informa__es_Gerais` do censo de nomes nasceu exatamente de pular um
   dos dois passos).
3. **Mesmo calculado certo, é o nome PRETENDIDO.** `ToolNameDeduplicator` renomeia
   por colisão, e a precedência declarada é MCP → delegação → conhecimento:
   conhecimento é **sempre** o renomeado. A colisão só se resolve na execução, com
   o conjunto inteiro do agente na mão — que o formulário de uma base não tem, nem
   em princípio, porque a base ainda nem foi vinculada a agente nenhum.

**Alternativa descartada — exibir com a ressalva "pode ser renomeado".** A
ressalva teria de aparecer **sempre**, porque a tela nunca sabe se houve colisão.
Um identificador exibido com "isto talvez não seja o identificador" não ensina
nada e gasta a atenção do operador num dado que ele não pode usar. É a convenção
13 na direção forte, com um verniz de honestidade.

**Alternativa descartada — pedir uma rota que devolva o nome efetivo.** Seria
backend sequenciado, não improviso, então é legítimo propor — mas o nome efetivo
depende do **agente**, não da base, então a rota não teria onde encostar nesta
tela. Isso pertence ao item aberto *"a renomeação por colisão é invisível na UI"*,
cujo lugar natural é a aba de tools do agente, e o registro dele passa a dizer
isso com esta razão.

**O que muda de fato:** o comentário passa a citar as três razões acima com os
arquivos e linhas, e o teste `'não afirma um nome de tool que o sistema ainda não
definiu'` é renomeado — o nome dele afirma o que deixou de ser verdade. A asserção
é a mesma, estendida ao segundo prefixo (`search_`); o que estava errado era a
justificativa.

### D5 — O preview para de se intitular como tudo o que o agente vê

**Achado novo desta change**, não está em nenhum dos dois itens abertos, e é
convenção 13 sobre uma frase que era verdadeira quando foi escrita.

O bloco se chama **"Como o agente vê esta base"** e mostra duas linhas: o nome, e
a descrição crua. Desde a etapa 4 o modelo recebe, como descrição da tool:

> `Busca trechos na base de conhecimento '<nome>'. Conteúdo da base: <descrição> ` + bloco fixo de como ler o resultado

**Decisão:** o preview continua mostrando o nome e o texto do operador — é o que
ele serve para mostrar —, e o título e a nota passam a dizer a verdade: que este é
**o trecho que o operador escreve** dentro do texto que o agente recebe, e que o
sistema o envolve com instruções fixas de como ler o resultado.

**Alternativa descartada — reproduzir a cadeia inteira.** Traria os 441
caracteres de `ComoLerOResultado` para o frontend, que é a mesma segunda fonte de
verdade do D4, num texto com guarda negativa próprio em `apps/workers` (o que
proíbe valor numérico de corte) que ninguém replicaria do lado da tela. E o texto
fixo não é acionável pelo operador: ele não pode mudá-lo.

**Alternativa descartada — deixar como está.** Era o que a 5a-1 podia fazer, e não
é mais: a etapa 4 tornou a afirmação conferível, e ela não confere.

**O gatilho que fica escrito junto:** se `KnowledgeToolDescription.Build` deixar de
envolver a descrição em prefixo e bloco fixo, esta cópia vira falsa. O gatilho vai
no comentário do componente, ao lado da citação do arquivo — que é a régua que a
convenção 6 pede para afirmação sobre código em outro app.

### D6 — `KnowledgeBaseDescriptionCard` entra, e `KnowledgeBaseEditPage` não

**Decisão:** a change abre quatro arquivos — o formulário e o card de descrição do
detalhe, cada um com o teste. `KnowledgeBaseEditPage` fica fora.

**Por que o card entra:** ele carrega a **mesma** frase que o D5 corrige, na forma
mais forte. Textualmente: *"Ele é a descrição da ferramenta que o agente vê: é por
ele que o modelo decide se a pergunta pertence a esta base."* A segunda metade é
verdadeira; a primeira afirma identidade onde há **parte**. Deixar uma das duas
telas dizendo isso é a duplicação que a 5c nomeou em D10 — *"dois lugares onde a
cópia pode divergir na próxima change"* —, e aqui as duas já divergiriam na mesma
frase.

**O custo, dito com o número:** convenção 14 cobra uma rodada de conferência
manual por tela, e isto acrescenta uma segunda tela. É **um card**, não uma tela
nova, e a alternativa é uma change futura de um arquivo e meio para uma frase.

**Por que a página de edição não entra:** ela só monta o formulário e passa
`initialValues`, `errors`, `submitting` e `submitLabel`. Nada nesta change muda o
que ela passa. Ela aparece na fila de `format:check` e é por isso que o registro a
listou como escopo — o que é o inverso do critério: **o que decide formatar é a
change abrir o arquivo, não o arquivo estar na mesma lista.**

**Consequência para o item aberto de `format:check`:** o registro diz "8 arquivos,
seis são escopo exato da 5a-4". São **7** hoje (`LoginPage.tsx` foi corrigido pela
`frontend-marca-visual` ao tocá-lo) e **quatro** são desta change. O item é
corrigido com os dois números e com a causa — a atribuição contou por vizinhança de
nome de arquivo, não por leitura do que a change abre.

## Risks / Trade-offs

Cada risco com contraparte verificável (convenção 10).

| risco | contraparte verificável |
|---|---|
| **R1 — A cópia nova vira parede de texto e ninguém lê.** O bloco da descrição passaria a ter orientação, campo, contador e preview, com o parágrafo crescendo. | Conferência manual da convenção 14 **medindo**, não comparando aparência: `getBoundingClientRect` da altura do bloco da descrição antes e depois, e contagem de linhas renderizadas do parágrafo nos dois esquemas, na largura do protótipo (`max-width:680px`) e em janela estreita. Régua declarada antes: o parágrafo não passa de **6 linhas** renderizadas a 620px. Se passar, o texto encurta — não o `PROSE_MAX_WIDTH`. |
| **R2 — O guarda novo passa verde com o defeito presente**, que é o defeito que esta change existe para corrigir, reencenado. | Convenção 15, com as duas metades registradas: cada `it()` novo é escrito, o defeito reintroduzido de propósito (a cópia velha restaurada), a reprovação observada e anotada, e só então a correção mantida. E **o que continuou verde** também vai para o registro — em especial os `it()` de contador, que não devem reprovar. |
| **R3 — A asserção negativa vira cláusula dentro de um `it()` positivo** e some numa refatoração. Foi a quarta causa que a 5c nomeou: numa tela cujo valor está no que ela se recusa a afirmar, cada negativa é um `it()`. | Contadas **antes**: a tela carrega **6** negativas depois desta change — nenhum identificador de ferramenta; nenhum veredito de qualidade no contador; nenhum veredito de generalidade (badge, alerta ou cor derivada do texto); nenhum número de uso ou de chamadas por base; o preview não se declara o texto completo; o card não afirma que a descrição **é** a descrição da tool. Seis `it()`, uma por negativa, com o motivo no comentário. |
| **R4 — O `it()` renomeado do D4 perde o rastro** e alguém reintroduz `consultar_base` achando que a recusa caducou com a etapa 4 (que é exatamente o raciocínio que quase venceu aqui). | O comentário do teste passa a citar as **três** razões com arquivo e linha, e a razão 3 (renomeação em runtime) é a que não caduca. O requisito da spec viva carrega a mesma justificativa reescrita — comportamento observável que fica só no `design.md` some no archive, e a próxima change o reintroduz. |
| **R5 — A cópia do D5 vira falsa em silêncio** se `apps/workers` mudar a montagem da descrição da tool. | Gatilho escrito no comentário do componente com o arquivo e a linha (`KnowledgeToolDescription.cs:52`), e a afirmação na tela é **estrutural** ("o sistema acrescenta instruções fixas"), nunca a reprodução do texto — o que quebraria a cada edição de prosa lá. |
| **R6 — A conferência manual não acha nada** porque a semente do protótipo não produz o estado que interessa. Foi o par que a 5c mediu: percorrer não alcança estado que a semente não produz. | Os dois modos, declarados antes: **percorrer** o formulário nos dois esquemas em criação e edição, com descrição vazia / curta / longa / de 421 caracteres (o valor medido por `0d`); e **ler** o `kbFormVals` do protótipo contra a spec viva, que é o modo que acharia uma terceira afirmação falsa como as do D5. |
| **R7 — Formatar os 4 arquivos mistura ruído de prettier com a mudança de verdade** no diff, e a revisão perde o que importa. | Formatação em **commit próprio**, antes das mudanças de conteúdo, com `npx prettier --write` só nos 4 caminhos. A suíte roda entre os dois, e o número tem de ser o mesmo da baseline — 850/850 — porque reflow não muda comportamento. |

## Open Questions

Nenhuma bloqueante. Duas registradas com gatilho, para não virarem adiamento com
outro nome:

- **Contagem de invocação por tool** — se nascer, D2 e o item aberto *"a
  renomeação por colisão é invisível na UI"* passam a ter dado, e a aba de tools
  do agente é onde eles encostam. **Gatilho:** o primeiro caso real de
  base-atrator diagnosticado em uso, que é quando alguém vai querer o número.
- **O bar de roteamento de `0d` continua reprovado** (61,0% contra 75%), e esta
  change não o move — ela é cópia, não mecanismo. **Gatilho:** a busca unificada
  entre bases (linha 2 da fila), que muda o mecanismo de roteamento e é onde o bar
  volta a ser medível.
