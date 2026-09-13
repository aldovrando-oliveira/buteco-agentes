## Why

`0d` mediu que **descrição de base genérica canibaliza as vizinhas**: a base cuja
descrição cobria seis assuntos tinha **5** intenções com alvo e foi chamada **24**
vezes, enquanto a maior base, com 28 intenções, foi chamada 11. Acerto de
roteamento de 61,0% contra um bar declarado em 75%, e recuperação de 0 em 31
quando o roteamento erra. O braço de controle com a ordem das tools invertida
descartou viés de posição.

O guarda que `KnowledgeBaseForm` já tem **mede a dimensão errada e passa verde com
o defeito presente**: ele avisa abaixo de `SHORT_DESCRIPTION_THRESHOLD = 80`
caracteres, e a descrição atratora de `0d` tinha **421** — as sete da rodada iam
de 250 a 427, e o aviso passaria verde em todas. É a convenção 15 aplicada a um
recurso de UI, e nessa forma é pior do que não haver guarda: campo sem aviso
convida a olhar, campo com aviso verde afirma que já foi olhado.

Esta é a **etapa 5a-4**, última da linha de bases de conhecimento, e existe
porque dois itens abertos apontam para ela — os dois pendurados até 13/09/2026
num gatilho (*"a próxima change que tocar `KnowledgeBaseForm`"*) que a 5a-3
converteu em posição de fila ao descobrir que o catálogo nunca abre o formulário.

## What Changes

Só `apps/frontend`. Nenhuma rota nova, nenhuma requisição nova, nenhuma validação
bloqueante nova.

- **A orientação da descrição ganha o critério que `0d` mediu** — delimitação
  ("do que esta base **não** trata") e a competição entre bases vizinhas — dentro
  do bloco de apoio permanente que já existe. **Não** vira aviso automático de
  generalidade: nenhuma regra distingue específica de genérica, e inventar uma
  seria a UI afirmando critério que o sistema não tem (convenção 13).
- **O aviso de comprimento fica, e para de afirmar qualidade.** A cópia atual
  diz *"curto demais para o modelo decidir com segurança"* — um veredito de
  qualidade que o comprimento não sustenta e que `0d` refutou. Passa a ser lido
  como o que é: piso de "escreveu alguma coisa".
- **O nome efetivo da tool continua NÃO exibido**, com o motivo **reescrito**, não
  apagado. Conferido: o formato `search_<slug>` é `private const` em
  `KnowledgeToolSetResolver.cs:20` e **não está em spec nenhuma** — nem na de
  `knowledge-tool-execution`, que a etapa 4 criou.
- **O preview "Como o agente vê esta base" deixa de afirmar mais do que sabe** —
  achado **novo**, que não está em nenhum dos dois itens abertos. Desde a etapa 4
  o modelo recebe `KnowledgeToolDescription.Build(...)`: prefixo fixo, a descrição
  cadastrada, e um bloco fixo de como ler o resultado. O preview mostra duas
  linhas cruas e se intitula como se fosse tudo.
- **`KnowledgeBaseDescriptionCard` carrega a mesma frase falsa** e entra junto:
  ele diz que a descrição *"é a descrição da ferramenta que o agente vê"*, e
  depois da etapa 4 ela é **parte** dessa descrição, não ela.
- **Os arquivos de `format:check` que esta change abre são formatados** — quatro
  dos sete (ver Impact).

**Não entra**: exibir o sintoma de base-atrator na tela. Conferido em
`apps/api/src/Buteco.Api/KnowledgeBases/` — não há contador de consultas por base,
e `knowledge-tool-execution` registra em log sem persistir contagem. A comparação
que diagnostica ("quantas vezes esta base foi chamada" contra "quantas perguntas
eram dela") não tem as duas metades no sistema. Fica no registro, como assinatura
a procurar em uso real.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `knowledge-base-catalog-ui`: dois requisitos mudam de conteúdo observável.
  - **Orientação e preview da descrição no formulário** — a orientação passa a
    exigir o critério de delimitação e a competição entre bases; o aviso de
    comprimento é declarado piso e não aferição; o `SHALL NOT` de nome de
    ferramenta **permanece** com justificativa reescrita; o preview passa a
    declarar que o texto do operador é envolvido por instruções fixas.
  - **A descrição da base é apresentada como texto lido pelo modelo** — a nota do
    detalhe para de afirmar que a descrição **é** a descrição da tool.

## Impact

| app | o que muda |
|---|---|
| `apps/frontend` | 4 arquivos modificados, 0 criados |
| `apps/api` | nada |
| `apps/workers` | nada |
| `apps/inbox` | nada |

**Arquivos, com o blast radius lido no código e não suposto** (convenção 18 —
criados e modificados contados separadamente, e modificados em pares com o teste):

| arquivo | por quê |
|---|---|
| `features/knowledge-bases/components/KnowledgeBaseForm.tsx` | cópia da orientação, cópia do contador, comentário do nome de tool, título e nota do preview |
| `features/knowledge-bases/components/KnowledgeBaseForm.test.tsx` | 1 asserção editada, 9 `it()` novos (6 deles negativos) |
| `features/knowledge-bases/components/KnowledgeBaseDescriptionCard.tsx` | a nota que afirma ser a descrição da tool |
| `features/knowledge-bases/components/KnowledgeBaseDescriptionCard.test.tsx` | 2 `it()` novos, um negativo e um positivo |

**Varrido para confirmar que não há quinto arquivo**: `grep -rn "curto
demais\|pergunta pertence a esta base\|não é mostrado ao cliente\|Como o agente
vê\|consultar_base"` sobre `apps/frontend/src` devolve **só** esses quatro. As
páginas que montam o formulário (`KnowledgeBaseCreatePage`,
`KnowledgeBaseEditPage`) e os testes delas não asseveram nenhuma dessas cadeias.

**`format:check`: quatro de sete.** A fila em `02-HISTORICO_E_STATUS.md` registra
**oito** arquivos reprovando, e que *"seis são `KnowledgeBaseForm`,
`KnowledgeBaseEditPage` e `KnowledgeBaseDescriptionCard` com os testes deles —
escopo exato da 5a-4"*. Conferido contra a árvore em `f925ebc`, com
`npx prettier --check` reproduzindo: são **sete**, não oito — `LoginPage.tsx` foi
corrigido pela change `frontend-marca-visual` ao tocá-lo. E **quatro**, não seis,
são desta change: `KnowledgeBaseEditPage.tsx` e o teste dele **não são abertos**
por ela — nada muda no que a página passa ao formulário. Restam **3** reprovando
depois desta change: `KnowledgeBaseEditPage` ×2 e `mcp-servers/utils/agentUsage.ts`.

**Baseline medida antes de escrever qualquer linha** (convenção 19), na branch
`feat/frontend-knowledge-base-form-orientacao` a partir de `main` em `f925ebc`,
com `npm ci` limpo: `apps/frontend` em **81 arquivos / 850 testes**. Saída completa
das três execuções em `~/.cache/buteco-agents/kb-5a4-baseline-f925ebc/`.

**A baseline exigiu três execuções, e isso é registro, não rodapé.** A primeira
reprovou **3 testes em 2 arquivos**; a segunda e a terceira deram **850/850**. A
primeira rodou logo após o `npm ci`, com 210s de duração contra 141s da terceira —
contenção de máquina, mesma assinatura da família de flake que
`agente-enderecos-a2a` fechou. **A identidade dos 3 se perdeu porque a saída foi
truncada com `tail -8`** — erro de instrumentação do mesmo tipo que a convenção 19
nomeia: instrumento que não falha visivelmente produz leitura inútil. As duas
execuções seguintes foram capturadas inteiras.

**Fecha dois itens abertos** — a orientação de generalidade da descrição e o nome
efetivo da tool —, e é a **quarta ocorrência** da família *"adiamento indefinido
com outro nome"*, a primeira em que a correção de gatilho para posição funcionou.
