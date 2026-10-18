## Context

A #160 (change arquivada
`openspec/changes/archive/2026-10-18-mcp-autenticacao-header-customizado/`)
entregou o tipo `CustomHeader` em `apps/api` e `apps/workers`: um token fixo
enviado cru num header de nome configurável. O frontend ficou para esta issue
(D7 daquela change), e a janela conhecida é esta: um servidor `CustomHeader`
criado pela API sai com rótulo vazio na tabela e no card, e o formulário de
edição não o salva.

A referência de tela é o protótipo aprovado pelo dono em 18/10/2026, copiado
em `design/` com os requisitos e as decisões fechadas da #168
(`design/PROTOTIPO.md`). As oito regras de uso do protótipo, do comentário da
#168, valem integralmente.

Escopo: só `apps/frontend`. Nenhum contrato novo; a change consome o que a #160
já serve.

### Verificações (convenção 6)

Feitas no worktree, em `f505114`, antes de qualquer decisão.

**V1. O contrato entregue pela #160.** Lido no código de `apps/api` e no
`design.md` arquivado.

- `McpServerResponse` (`apps/api/src/Buteco.Api/McpServers/Responses/McpServerResponse.cs:9-21`):
  `id, name, description, url, authType, authHeaderName, isActive, createdAt,
  updatedAt`. `authHeaderName` vem **depois** de `authType`, é `string?` e é
  nulo em todo tipo que não seja `CustomHeader` (`McpServer.cs:42` e `:71`
  gravam nulo nos outros tipos). A credencial continua fora.
- Os três requests ganharam `AuthHeaderName` como **último** parâmetro,
  opcional, `string? = null`: `CreateMcpServerRequest.cs:3`,
  `UpdateMcpServerRequest.cs:3`, `TestMcpServerConfigRequest.cs:3`.
- `authType` vai para o fio como nome do membro (`McpServerAuthType.cs:5`,
  `JsonStringEnumConverter`), e a API aceita só nome de membro, sem diferenciar
  caixa (`McpAuthHeaderRules.TryParseAuthType`, `McpAuthHeaderRules.cs:50-67`).
- **Chaves e mensagens dos 400** (`McpAuthHeaderRules.cs` e
  `McpServerEndpoints.cs`):

  | chave | quando | mensagem |
  |---|---|---|
  | `authHeaderName` | nome nulo ou só espaço, com `CustomHeader` | "O nome do header é obrigatório para o tipo de autenticação CustomHeader." (`McpAuthHeaderRules.cs:84`) — cita o **nome técnico**, não o rótulo da tela |
  | `authHeaderName` | caractere fora de `tchar` | "O nome do header só pode ter letras, números e os caracteres ! # $ % & ' * + - . ^ _ \` \| ~ (sem espaço nem dois-pontos)." (`:90`) |
  | `authHeaderName` | o .NET recusa como header de requisição | "O header {nome} não pode ser usado em uma requisição (é um header de conteúdo ou de controle do .NET)." (`:96`) |
  | `authHeaderName` | prefixo `Mcp-` | "Headers com prefixo Mcp- são do protocolo MCP e não podem levar a credencial." (`:102`) |
  | `authHeaderName` | reservado (`Accept`, `Host`, de conexão…) | "O header {nome} é controlado pelo protocolo e não pode levar a credencial." (`:107`) |
  | `credential` | token com caractere fora de 0x20–0x7E, com `CustomHeader` | "A credencial só pode ter caracteres ASCII visíveis ou espaço, sem quebra de linha nem caractere de controle." (`:149`) |
  | `credential` | token com espaço nas pontas, com `CustomHeader` | "A credencial não pode começar nem terminar com espaço." (`:154`) |
  | `credential` | tipo com credencial e nenhuma persistida (cadastro, ou update conforme D4) | "A credencial é obrigatória para o tipo de autenticação informado." (`McpServerEndpoints.cs:250`) |
  | `credential` | **teste não salvo** com `CustomHeader` e credencial em branco | "A credencial é obrigatória para testar o tipo de autenticação CustomHeader." (`McpServerEndpoints.cs:193-196`) — também cita o nome técnico |
  | `authType` | tipo inválido | mensagem gerada dos membros (`McpAuthHeaderRules.cs:71-72`) |

  O formato do token só é validado com `CustomHeader`
  (`McpServerEndpoints.cs:243`); `authHeaderName` com outro tipo é ignorado,
  não recusado (`:220-224`).
- **Update (D4 da #160).** `McpServer.UpdateDetails`
  (`apps/api/.../Entities/McpServer.cs:63-78`) limpa a credencial quando o tipo
  **muda** ou vai para `None` (`:74-76`); o handler responde 400 em `credential`
  se o tipo novo exige credencial e não há nenhuma
  (`UpdateMcpServerCommandHandler.cs:33-36`), antes do `SaveChanges`. Logo: troca
  entre tipos com credencial exige credencial nova; saída de `None` também
  exige, sem credencial anterior; `CustomHeader` → `CustomHeader` com nome novo
  **mantém** o token (tipo não mudou).
- **Teste não salvo com `CustomHeader` sem credencial:** 400 em `credential`
  antes de qualquer conexão (`McpServerEndpoints.cs:151-155` e `:193-196`).
- **Teste salvo com nome recusado:** `McpConnectionTester.TestAsync` devolve
  `failureReason: Unknown` com "O nome de header salvo para este servidor
  ('{nome}') não pode ser usado em uma requisição. Edite o servidor e informe
  outro nome de header. Nenhuma requisição foi feita."
  (`McpConnectionTester.cs:23-26` e `:95-98`). O frontend já exibe `Unknown`
  pelo `Record` exaustivo de `ConnectionTestResultAlert.tsx`, sem mudança.

Nenhum ponto contraria a #168 nem o protótipo.

**V2. Componente de cartão com radio.** Mantine instalado: `9.4.2`
(`apps/frontend/node_modules/@mantine/core/package.json`). `Radio.Card` existe
(`esm/components/Radio/RadioCard/RadioCard.mjs`): é um `UnstyledButton` com
`role="radio"`, `aria-checked` e o modificador `data-checked`, e navega por
setas entre os irmãos do mesmo `name`. Ele **não** estiliza o estado marcado.

O precedente é `OriginOption`
(`apps/frontend/src/features/knowledge-bases/components/KnowledgeBaseOriginCard.tsx:47-94`):
`Radio.Card` com `Radio.Indicator`, título `fw={600} size="sm"`, descrição
`fz={13}` `dimmed`, nota opcional, `p={14}`, `radius="md"`, e
`display: flex; align-items: flex-start` (o `Radio.Card` é botão e centralizava
o conteúdo quando o vizinho era mais alto: achado R1-1 da conferência). Fica
dentro de `Radio.Group` com `SimpleGrid cols={{ base: 1, sm: 2 }} spacing={12}`
(`:291-316`). A classe `buteco-origin-option` (`apps/frontend/src/index.css:55-62`)
dá fundo `--mantine-color-body` e, marcado, borda de 2px
`--mantine-primary-color-filled` e fundo `--mantine-primary-color-light`, os dois
declarados pelo Mantine nos dois esquemas. A change que o criou
(`openspec/changes/archive/2026-10-04-frontend-cadastro-base-sincronizada/design.md`,
D6/D7) registra o raio do tema contra o da prancha e a correção R1-1.

Varredura de outros cartões com radio no painel
(`grep -rn "Radio\.\(Card\|Group\|Indicator\)\|<Radio\b" apps/frontend/src`,
`exit=0`): `OriginOption` é o **único**; `FolderPickerModal.tsx:88` usa `Radio`
simples numa lista, não cartão. O formulário MCP seria o **segundo** consumidor.

**V3. Todos os pontos do frontend que conhecem o tipo de autenticação.**
Varredura `grep -rln "authType\|AuthType\|BearerToken" apps/frontend/src`
(`exit=0`; a primeira tentativa com `--include=*.ts` sem aspas falhou com
*no matches found* do `zsh` e foi refeita, convenção 21), conferida item a item:

| arquivo | o que conhece |
|---|---|
| `features/mcp-servers/types/mcpServer.ts:1` | a união `'None' \| 'BearerToken'` e os quatro tipos que a usam |
| `features/mcp-servers/components/McpServerForm.tsx:50-53` | as opções do `Select`; estado, validação, payload e teste (`:22-28`, `:44-48`, `:84-87`, `:101-114`, `:195-204`) |
| `features/mcp-servers/components/McpServerTable.tsx:19-22`, `:87` | `Record<McpServer['authType'], string>` de rótulos |
| `features/mcp-servers/components/McpServerConfigCard.tsx:9-12`, `:37`, `:45` | o mesmo `Record`, e a linha de credencial para tipo ≠ `None` |
| `features/mcp-servers/pages/McpServerEditPage.tsx:85-90` | `initialValues` sem `authHeaderName` |
| `features/mcp-servers/pages/McpServerCreatePage.tsx` | nada além de repassar o `CreateMcpServerInput` |

Os dois `Record` são exaustivos: com `CustomHeader` na união, o `tsc` reprova os
dois até ganharem o rótulo, que é a garantia da convenção 25 de graça.

Consumidores de `McpServer` fora da feature (`agents`, `inventory`):
`AgentMcpServerRow.tsx`, `AgentToolsTab.tsx`, `AgentDetailPage.tsx` e
`InventoryPage.tsx` importam o tipo ou os hooks, e **nenhum** lê `authType` em
produção; só os fixtures dos testes o declaram. Fixtures com `authType: '…'`,
por arquivo: `AgentMcpServerRow.test.tsx` 1, `AgentToolsTab.test.tsx` 1,
`AgentDetailPage.test.tsx` 1, `InventoryPage.test.tsx` 1,
`useMcpServers.test.ts` 4, `McpServerConfigCard.test.tsx` 2,
`McpServerForm.test.tsx` 8, `McpServerTable.test.tsx` 2,
`McpServerCreatePage.test.tsx` 1, `McpServerDetailPage.test.tsx` 2,
`McpServerEditPage.test.tsx` 4, `McpServerListPage.test.tsx` 1. Nem todos são
`McpServer` (alguns são inputs); o `tsc` enumera os que precisam de
`authHeaderName` quando o campo ficar obrigatório (convenção 18, tabela de
blast radius).

Módulos de API mockados por outros arquivos (`grep -rl "vi.mock(.*mcpServersApi\|vi.mock(.*useMcpServers"`):
11 arquivos, inclusive `app/router.test.tsx`. Esta change **não** acrescenta
função exportada a `mcpServersApi.ts` nem a `useMcpServers.ts`, então nenhum
deles é arrastado por mock.

**V4. Comparação do resultado do teste.** `McpServerForm.tsx:44-48` define
`TestedConnectionConfig` com `url`, `authType` e `credential`, e o resultado só
é exibido se os três baterem (`:103-114`). Lido: um campo novo fora dessa
comparação não descarta o resultado, então, sem `authHeaderName` nela, um
sucesso continuaria visível depois de editar o nome do header — o operador
veria "Conexão bem-sucedida" para um header que nunca foi testado. Confirma a
premissa da #168. A demonstração por teste é o guarda G1 (tarefas 4.8 e 4.10).

**V5. Versão do protótipo.** A leitura do canvas devolveu a versão
`1792295069-a164`, a aprovada. Nenhuma mais nova.

**V6. O tratamento atual do 400 do teste.** `McpServerForm.tsx:170-180` junta
as mensagens de `problem.errors` num `ConnectionTestResultAlert` de falha, e o
teste `McpServerForm.test.tsx:213` ("erro de validação (400) no teste de conexão
é normalizado para o mesmo alerta de falha") prende isso. A origem é a Decision
1 de `openspec/changes/archive/2026-08-03-frontend-mcp-servidores-catalogo/design.md:107-117`,
cuja premissa é: *"o `McpServerForm` já impede isso via validação client-side
dos mesmos campos"*. Com `CustomHeader` a premissa deixa de valer — a validação
de formato do header e do token é da API (regra 4 da #168) e o 400 vira o
caminho principal desses erros. Decide D5.

**V7. Empilhamento por largura do formulário.** `SimpleGrid` do 9.4.2 aceita
`type="container"` (`esm/components/SimpleGrid/SimpleGrid.mjs:33-43`): envolve a
grade num elemento com `container-type: inline-size`
(`@mantine/core/styles.css:5705`) e resolve os `cols` por container query, com
chaves em px. A navbar do painel tem 224px e colapsa em `sm`
(`components/layout/AppShell.tsx:57`), então a largura do formulário não é
função simples da viewport. Escala do tema: `size="sm"` é **12px**
(`theme.ts:129-135`), não os 14px do Mantine padrão. Decide D3.

**V8. Precedentes de tela.** Nenhum formulário do painel tem alerta de resumo
de erros no topo (`grep -rn "Corrija" apps/frontend/src` só acha textos de
outros contextos). Aviso amarelo é `Alert color="yellow"` em sete lugares
(ex.: `AgentEditPage.tsx:81`, `AgentA2ACard.tsx:74`). Superfície sutil
declarada nos dois esquemas: `--buteco-surface-subtle` (`theme.ts:228`, `:270`).
Rótulo de seção em caixa alta: `components/data/SectionLabel.tsx`.

**V9. CORS e portas para a conferência.** `apps/api` em desenvolvimento aceita
`http://localhost:5173`, `:4173` e `:3000`
(`apps/api/src/Buteco.Api/appsettings.Development.json:18`). Em 18/10/2026: a
5173 está ocupada pelo Vite do checkout principal; a 4173 estava livre
(`lsof -iTCP:4173 -sTCP:LISTEN`, `exit=1`); há um `apps/api` do checkout
principal na 5017, com `Buteco.Api.dll` de 18/10 01:30, e **não** se sabe se
esse binário já tem a #160 — a tarefa de conferência confere antes.

**Versões:** nenhuma dependência nova nem troca de versão. Mantine 9.4.2 é o
instalado (V2); `Radio.Card`, `SimpleGrid type="container"` e `Alert` já estão
nele.

## Goals / Non-Goals

**Goals:**

- Cadastrar, editar e exibir servidores `CustomHeader` pelo painel, fechando a
  janela da D7 da #160.
- Seletor em cartões com radio, nos dois fluxos, com o padrão já escrito no
  painel.
- Avisos e regras de credencial na troca de tipo coerentes com a D4 da #160.
- Corrigir o "Testar conexão" que testava o servidor salvo com o tipo antigo.

**Non-Goals:**

- Validação local de formato do nome do header ou do token (regra 4 da #168).
- Qualquer mudança em `apps/api` ou `apps/workers`.
- OAuth client credentials (#161).
- Mudar campos que o protótipo desenha diferente mas que esta change não toca
  (descrição, fonte da URL, placeholder da URL): divergências registradas abaixo.

## Decisions

### D1. Componente compartilhado `RadioCardOption`, extraído do `OriginOption`

O formulário MCP é o segundo consumidor de exatamente o mesmo padrão (V2).
Extrair agora, em `apps/frontend/src/components/forms/RadioCardOption.tsx`:

```tsx
interface RadioCardOptionProps {
  value: string;
  title: string;
  description: string;
  disabled?: boolean;
  note?: string;
}
```

A API é a do `OriginOption`, com `value: string` no lugar de
`KnowledgeBaseContentMode`. O corpo é o atual (o `Radio.Card`, o alinhamento no
topo da R1-1, o indicador, título, descrição e nota), e a classe CSS muda de
`buteco-origin-option` para `buteco-radio-card-option`, que deixa de nomear um
consumidor. O `KnowledgeBaseOriginCard` passa a importar o componente e apaga o
`OriginOption` local. Os testes dele (`KnowledgeBaseOriginCard.test.tsx`)
continuam verdes **sem editar asserção**: afirmam papel `radio`, nome acessível e
`aria-checked`, que o componente extraído preserva.

Pasta nova, `components/forms/`: as quatro existentes são `brand`, `data`,
`feedback` e `layout`, e um controle de formulário não é nenhuma delas.

Motivo, pela convenção 2: o gatilho é repetição **já observada**, e o que se
repetiria não é markup, é regra aprendida — a borda e o fundo por esquema
(convenção 16) e a correção R1-1, que custou uma rodada de conferência para
achar. Copiar o componente é copiar essas duas decisões, e a próxima correção
num dos dois não chegaria no outro. O precedente de dois consumidores existe
(`libs/ProviderCatalog`).

- *Recusada, replicar o padrão no formulário MCP:* duas cópias do alinhamento e
  da regra do estado marcado, e uma classe `buteco-origin-option` usada num
  formulário que não tem origem nenhuma, ou uma segunda classe idêntica.
- *Recusada, extrair para dentro de `features/mcp-servers` e importar de
  `knowledge-bases`:* referência entre features por um componente que não é de
  domínio nenhum (convenção 7, feature por conceito).

### D2. Aparência dos cartões: a regra do painel, não a do protótipo

Marcado: borda de 2px em `--mantine-primary-color-filled` e fundo
`--mantine-primary-color-light`; desmarcado: borda padrão e fundo
`--mantine-color-body`. É o que `index.css:55-62` já escreveu.

O protótipo desenha borda de **1px** e fundo `#eef3fb`. Pela convenção 17, a
regra que o sistema já escreveu vence: os dois tokens são declarados nos dois
esquemas (convenção 16), e o hex do protótipo só existe no claro; trocar a
espessura no componente extraído mudaria também o card de origem das bases,
aprovado em conferência. A regra 2 da #168 já diz que os hex e px do protótipo
são aproximação.

- *Recusada, 1px como no protótipo:* ou muda o card de origem sem motivo, ou
  obriga uma variante no componente compartilhado só por espessura de borda.

### D3. Empilhamento: uma coluna ou três, por largura do formulário

`SimpleGrid type="container"` com `cols={{ base: 1, '<N>px': 3 }}` e
`spacing={12}`. Nunca duas colunas: com três opções, duas colunas deixam uma
órfã numa linha própria, e o requisito é "lado a lado" ou "empilhados".

Container query, não media query: o que decide se "Token em header
customizado" cabe é a largura do **formulário**, e ela depende da navbar de
224px que colapsa em `sm` (V7). Um breakpoint de viewport erraria nas duas
pontas do colapso.

**`N` = `760px`, medido.** A estimativa de partida era título de 27
caracteres em 12px, peso 600 (~175px), mais `p={14}` dos dois lados, indicador
e vão de 10px, mais a borda de 2px marcada: cartão mínimo de ~240px, ~745px de
container; o ponto de partida foi 760px.

**Medido em 18/10/2026 (tarefa 7.4), e confirma o 760.** O título mede
**169,02px** e fica em uma linha em todas as larguras. O corte acontece
exatamente no container de 760px: **três colunas em 760, uma em 759**. No
último ponto com três colunas, a largura útil do cartão (conteúdo menos
indicador e vão) é **185px** com o cartão desmarcado e **183px** com ele
marcado (borda de 2px), o pior caso: **13,98px de folga**. A estimativa errou
pouco no título (175 contra 169) e acertou o corte. Com o cartão marcado, o
formulário cresce, a barra de rolagem vertical (15px) aparece e o corte por
**viewport** passa de 1058 para 1073px, enquanto o corte por **container** fica
em 760px: é a medida que justifica a container query.

Método: no Chrome da automação o redimensionamento da janela não teve efeito,
então a viewport foi controlada por um `iframe` da mesma origem, com largura
fixa, em que media queries e container queries reagem de verdade; leitura por
`getBoundingClientRect`. O tema não muda nenhuma das larguras (mesma fonte,
mesma borda); a conferência do dono cobriu os dois esquemas.

**Navbar colapsada: não existe no painel.** Abaixo de `sm` (viewport ≤ 768px)
a navbar ocupa a largura inteira e cobre o formulário, sem `Burger`
(`components/layout/AppShell.tsx:56`). Nessa faixa o container é sempre menor
que 760px, os cartões ficam em uma coluna e o título cabe com folga (693px de
container em 767px de viewport). É comportamento anterior a esta
change: **#171**.

- *Recusada, `repeat(auto-fit, minmax(230px, 1fr))` como no protótipo
  (`minColWidth` do `SimpleGrid`):* produz a linha de duas mais uma órfã nas
  larguras intermediárias, e o 230 foi medido na fonte do protótipo, não na do
  tema.
- *Recusada, `cols={{ base: 1, lg: 3 }}` por viewport:* ver acima.

### D4. Estado do formulário e regra do teste

**Estado.** `McpServerFormValues` ganha `authHeaderName: string`. Trocar de
tipo não apaga o valor digitado: ele continua no estado e só não é enviado,
para quem testa os cartões e volta não perder o que digitou.

**Payload.** `authHeaderName` vai em `CreateMcpServerInput`,
`UpdateMcpServerInput` e `TestMcpServerConfigInput` **só** com `CustomHeader`,
como foi digitado (sem `trim`: a API recusa espaço com mensagem própria, e
aparar em silêncio seria corrigir no cliente o que a regra 4 deixa com a API).
Com outro tipo, o campo não é enviado — a API o ignoraria, mas o payload não
deve carregar configuração de um tipo que não está em uso.

**Tipo salvo.** Na edição, o tipo salvo é o `initialValues.authType` que a
`McpServerEditPage` já passa a partir de `GET /mcp-servers/{id}`. A página
passa também `authHeaderName: data.authHeaderName ?? ''`.

- *Recusada, uma prop `savedAuthType` separada:* seria a segunda via do mesmo
  valor, vindo da mesma resposta, e as duas podem divergir num teste ou numa
  refatoração sem nada reprovar.

**Avisos (D4 da #160).** Com `typeChanged = isEditing && authType !== savedAuthType`:

| de → para | aviso | credencial |
|---|---|---|
| com credencial → outro com credencial | "O tipo mudou de {salvo} para {atual}. Informe a credencial nova: a credencial salva era do tipo anterior e não é reaproveitada." | obrigatória |
| com credencial → `None` | "Ao salvar, a credencial atual ({salvo}) é apagada. Ela não fica guardada sem uso." | campo oculto |
| `None` → com credencial | nenhum | obrigatória |
| mantido | nenhum | em branco mantém |

**Comparação do resultado do teste.** `TestedConnectionConfig` ganha
`authHeaderName` (V4).

**Regra do teste em função pura.** A escolha entre testar o salvo, testar o
digitado ou não testar mora numa função exportada,
`connectionTestMode({ isEditing, hasServerId, savedAuthType, authType,
credential })` → `'saved' | 'unsaved' | 'blocked'`, em
`features/mcp-servers/utils/connectionTestMode.ts`. Testável sem montar o
formulário, e é a mesma pergunta que decide o `disabled` do botão e a nota
(precedente: convenção 20, condição em função pura).

| situação (edição, com id) | modo | nota |
|---|---|---|
| tipo mantido, exige credencial, credencial em branco | `saved` | "Sem digitar a credencial, o teste usa a configuração salva deste servidor." e, com `CustomHeader`, ", inclusive o nome do header: uma alteração no nome só é testada depois de salvar." |
| tipo mudou para um tipo com credencial, credencial em branco | `blocked` | "Para testar o tipo novo, informe a credencial nova." |
| credencial digitada | `unsaved` | nenhuma |
| tipo `None`, mantido ou não | `unsaved` | nenhuma |
| cadastro | `unsaved` | nenhuma |

Duas mudanças de comportamento, as duas registradas:

1. **`blocked`** corrige o defeito da #168: hoje (`McpServerForm.tsx:101`)
   credencial em branco na edição testa o salvo mesmo com o tipo trocado.
2. **`None` em edição passa a testar o digitado.** Hoje, com credencial em
   branco, testa o salvo — e com `None` o campo de credencial nem aparece, então
   o teste sempre usava a URL salva, sem nota nenhuma. O protótipo não mostra
   nota para `None`; sem a nota, testar o salvo afirmaria em silêncio um
   resultado sobre uma URL que não é a da tela (convenção 13). Com `None` não há
   credencial a preservar, então o teste do digitado é completo — que era o
   único motivo da Decision 6 de `frontend-mcp-servidor-uso-e-diagnostico`.
   **Aprovada pelo dono em 18/10/2026**, na revisão da proposta.

A nota do modo `saved` troca "credencial salva" por "configuração salva": o
teste salvo usa também a URL e o nome do header salvos
(`TestSavedMcpServerConnectionCommandHandler.cs:44`), e "credencial" dizia menos
do que acontece.

- *Recusada, testar o salvo na troca de tipo, com uma nota:* é exatamente o
  defeito, só que avisado; o resultado continuaria sendo do tipo antigo.
- *Recusada, deixar o botão habilitado e barrar no clique:* o protótipo e a
  #168 pedem desabilitado com nota, e a nota diz o que falta antes do clique.

### D5. Erros: obrigatoriedade no cliente, o resto da API, nos campos

**Validação local, só de obrigatoriedade:**

| campo | quando | mensagem |
|---|---|---|
| `authHeaderName` | `CustomHeader` e `!value.trim()` | "Informe o nome do header em que o serviço espera o token." |
| `credential` | cadastro, tipo ≠ `None`, em branco | "A credencial é obrigatória para o tipo de autenticação informado." (a de hoje) |
| `credential` | edição, `typeChanged`, tipo ≠ `None`, em branco | "Informe a credencial nova para o tipo {rótulo}." |

**No teste não salvo**, o mesmo critério roda antes da requisição, como já roda
o da URL (`McpServerForm.tsx:119-122`): URL vazia, e, com `CustomHeader`, nome
do header e token em branco. Token em branco com `BearerToken` continua
podendo ser testado no cadastro (comportamento de hoje, teste
`McpServerForm.test.tsx:336`): a API aceita, e o resultado
(`CredentialRejected`) é informação útil.

**Os 400 da API vão para os campos, no submit e no teste.** No submit, como
hoje (`errors` da página). No teste não salvo, o formulário aplica cada chave de
`problem.errors` ao campo correspondente (`url`, `authType`, `authHeaderName`,
`credential`) e mostra, no lugar do resultado, um aviso inline: "Corrija os
campos destacados antes de testar. Nenhuma conexão foi tentada." Chave que não
corresponda a nenhum campo vai no próprio aviso, para não sumir.

Isso **substitui** a Decision 1 de `frontend-mcp-servidores-catalogo` (V6), que
normalizava o 400 para o alerta "Falha na conexão". A premissa dela era que o
formulário já impedia o 400; com o formato do header validado só na API, não
impede, e o alerta de falha afirmaria uma tentativa de conexão que não
aconteceu (convenção 13): a API responde 400 antes de montar o transporte
(`McpServerEndpoints.cs:151-155`). O teste `McpServerForm.test.tsx:213` muda de
asserção, por decisão. **Aprovada pelo dono em 18/10/2026**, na revisão da
proposta.

**Precedência entre o erro da página e o do formulário.** Lido no código: o
formulário exibe `errors?.<campo> ?? form.errors.<campo>`
(`McpServerForm.tsx:215`, `:223`, `:230`, `:240`, `:257`). `errors` é o 400 do
submit, guardado em estado da página e limpo só no início do próximo submit
(`McpServerEditPage.tsx:28`, `McpServerCreatePage.tsx:26`). `form.errors` é do
`useForm`, que limpa o erro de um campo quando ele muda
(`clearInputErrorOnChange = true` por padrão,
`@mantine/form/esm/use-form.mjs:18`). Consequência: depois de um submit com 400
em `authHeaderName`, a mensagem da página fica no campo mesmo depois de ele ser
editado, e esconde o 400 do teste que o formulário aplica com `setFieldError`
no mesmo campo. Com esta change, o 400 do teste passa a ir para os campos, e
isso deixa de ser só uma mensagem velha: passa a esconder a mensagem nova.

**Decisão: (a) o erro da página de um campo deixa de ser exibido quando o
campo é editado.** O formulário guarda o conjunto de campos editados desde o
último `errors` recebido, e esse conjunto é zerado quando a página entrega um
`errors` novo (novo submit). Um campo nesse conjunto mostra só `form.errors`.
O 400 do teste aplicado a um campo também o põe no conjunto: é um erro mais
novo, sobre o valor atual. A página não muda; a regra fica no formulário, que é
quem sabe o que foi editado. É o mesmo comportamento que o `useForm` já tem para
os próprios erros, estendido ao erro que vem de fora.

- *Recusada, (b) exibir o mais recente entre os dois:* exige ordenar duas
  fontes (marcar quando cada uma chegou), e enquanto o operador edita, sem
  testar nem submeter de novo, o erro mais recente continua sendo o do submit,
  que fala de um valor que já não está no campo (convenção 13). A (a) resolve o
  caso da revisão e esse junto.
- *Recusada, a página limpar `fieldErrors` a cada mudança:* a página não vê as
  mudanças de campo (o formulário é dono do estado), e expor um `onChange` por
  campo só para isso inverteria quem decide.

**Corrigido na implementação (convenção 9): o 400 de credencial ausente na
edição deixa de chegar pelo formulário.** A delta herdou da spec viva
(`openspec/specs/mcp-server-catalog-ui/spec.md:215`) a cláusula "incluindo o
caso de `AuthType` diferente de `None` sem nenhuma credencial persistida" no
cenário "Edição rejeitada por validação do servidor", e o teste
`McpServerEditPage.test.tsx:140` a prendia (servidor `None` trocado para Bearer
Token, credencial em branco, 400 da API no campo). Com a obrigatoriedade local
da tabela acima, essa troca é barrada no cliente com "Informe a credencial nova
para o tipo Bearer Token." e o `PUT` não sai, então o 400 nunca chega. Achado ao
rodar a suíte antiga contra o formulário novo, antes de editar teste. O caso
passou a ser coberto pelo cenário "Saída de None exige credencial"; a cláusula
saiu da delta e o teste foi reescrito com o comportamento novo (decisão do dono
em 18/10/2026). O cenário passou a citar, como exemplos de 400 que ainda chegam,
os dois que o formulário deixa passar por não validar formato: token de
`CustomHeader` com espaço no início (`credential`) e troca para `CustomHeader`
com `Content-Type` no nome (`authHeaderName`). Os dois ganharam teste pelo
caminho real da página (`PUT` rejeitado → `fieldErrorsFrom` → campo), porque o
teste de 400 que já existia (`:166`) só simula a chave `name`.

**A mensagem da API que cita `CustomHeader`.** São duas (V1):
"O nome do header é obrigatório para o tipo de autenticação CustomHeader." e
"A credencial é obrigatória para testar o tipo de autenticação CustomHeader."
As duas são de obrigatoriedade, e a validação local acima as torna
inalcançáveis pelo formulário: o cliente barra antes de enviar, no submit e no
teste. O único resto é entrada que o `trim` do JavaScript considera não vazia e o
`IsNullOrWhiteSpace` do .NET considera vazia (um nome feito só de `U+0085`), que
não é caso de uso. **Não é achado de backend, e não há tradução no frontend.**

- *Recusada, traduzir no cliente a mensagem que cita `CustomHeader`:* seria
  segunda fonte de verdade da mensagem, e inútil, porque ela não chega.
- *Recusada, validar o formato do header no cliente para dar a mensagem antes
  do teste, como o protótipo:* terceira cópia da lista de reservados, que
  envelhece (regra 4 da #168; D3 da #160 já escolheu prefixo em vez de lista
  porque o SDK acrescenta headers).

### D6. A caixa de prévia, e as afirmações sobre outro app

A caixa só existe com `CustomHeader` (decisão 2 do dono). Linha:
`{authHeaderName.trim() || '<nome do header>'}: ••••••••`, em monoespaçado; o
valor do token nunca entra no componente da caixa. Nota: "O valor vai como foi
digitado, sem prefixo. Authorization não é enviado." Superfície:
`--buteco-surface-subtle` com borda `--mantine-color-default-border`, os dois
declarados nos dois esquemas (V8).

O formulário passa a afirmar **quatro** coisas sobre `apps/api`/`apps/workers`.
Cada uma leva, no código, um comentário de "afirmação estrutural sobre outro
app" com arquivo, linha e o **GATILHO** que a torna falsa, no molde do
`FolderField` (`KnowledgeBaseOriginCard.tsx:201-207`, convenção 13):

| afirmação na tela | fonte | gatilho |
|---|---|---|
| caixa de prévia e descrições dos cartões Bearer/header: "em cada requisição", "sem prefixo", "Authorization não é enviado", "Authorization: Bearer" | `apps/api/.../Connectivity/McpConnectionTester.cs:126-140` e `apps/workers/.../Mcp/McpTransportFactory.cs:68-82` (`AdditionalHeaders`, que o SDK manda em toda requisição — V1 da #160) | a montagem do header mudar em qualquer dos dois, ou a #161 trocar a forma (token obtido e renovado) |
| aviso de troca de tipo: "não é reaproveitada" | `apps/api/.../Entities/McpServer.cs:63-78` (`authTypeChanged`) e `UpdateMcpServerCommandHandler.cs:33-36` | o update voltar a manter a credencial na troca de tipo |
| aviso de troca para `None`: "é apagada" | `McpServer.cs:74-76` | o update deixar de limpar a credencial ao ir para `None` |
| aviso do 400 do teste: "Nenhuma conexão foi tentada" | `McpServerEndpoints.cs:151-155` (o 400 sai antes do `mediator.Send`) | a validação de forma do teste passar a rodar depois de conectar |

As afirmações são estruturais e curtas; nenhuma reproduz texto de `apps/api`.

- *Recusada, um comentário só no topo do arquivo:* a convenção 13 pede a fonte
  junto da frase; um bloco distante não é lido por quem edita a frase.

### D7. Tabela, detalhe e tipos

- `McpServerAuthType` ganha `'CustomHeader'`. `McpServer` ganha
  `authHeaderName: string | null`, **obrigatório**, porque a API sempre o
  devolve (V1); o `tsc` enumera os fixtures (V3). Os três inputs ganham
  `authHeaderName?: string`.
- Os dois `Record` de rótulos ganham `CustomHeader: 'Token em header
  customizado'` (decisão 1 do dono). Ficam duplicados em `McpServerTable.tsx` e
  `McpServerConfigCard.tsx`, como hoje, e o formulário passa a ter o terceiro
  (rótulos dos cartões e dos avisos). **Três cópias do mesmo mapa** é o gatilho
  da convenção 2: o mapa sai para `features/mcp-servers/utils/authTypeLabels.ts`
  como `Record<McpServerAuthType, string>` exaustivo, e os três importam.
- `McpServerConfigCard` ganha a linha "Header", entre "Autenticação" e
  "Credencial", com o nome em `ff="monospace"`, só com `CustomHeader`. Um
  servidor `CustomHeader` sem `authHeaderName` não existe pela API
  (`McpServerEndpoints.cs:238-241` recusa); se chegar nulo, a linha mostra `—`
  (registro que não veio, convenção 13), nunca vazio.

- *Recusada, manter os rótulos copiados em três arquivos:* é a forma que já
  deixou o tipo novo com rótulo vazio em dois lugares (D7 da #160); o `Record`
  exaustivo salvou a compilação, mas três cópias são três textos para manter
  iguais.

### D8. Composição do formulário

Ordem, do protótipo: Nome, Descrição (mantida, ver divergência P3), Url;
divisor; rótulo de seção "Autenticação" (`SectionLabel`); cartões; aviso de
troca para `None`; "Nome do header"; credencial/Token (com o aviso de troca de
tipo dentro do campo, acima da descrição); caixa de prévia; nota de teste;
resultado do teste; botões.

O grupo de cartões usa `Radio.Group` com `label="Tipo de autenticação"`,
`withAsterisk` e `error`, para o 400 em `authType` aparecer no grupo.

**Corrigido na implementação (convenção 9):** o aviso de troca de tipo fica
dentro do campo de credencial, mas **entre a descrição e a caixa de texto**, e
não entre o rótulo e a descrição como no protótipo. Causa: a descrição do
Mantine é um `<p>` (`@mantine/core/esm/components/Input/InputDescription/InputDescription.mjs:34`,
`component: "p"`), que não pode conter o `Alert`; o ponto de inserção que o
`Input.Wrapper` oferece é o `inputContainer`, em volta da caixa. A decisão (o
aviso pertence ao campo) não muda; a conferência visual confere a posição.

**Corrigido na implementação (convenção 9): `aria-invalid` explícito no campo
de credencial/token.** O `PasswordInput` do Mantine 9.4.2 chama o `Input` com
`withAria: false` (`@mantine/core/esm/components/PasswordInput/PasswordInput.mjs:159`)
e põe só `data-invalid` no `<input>` (`:165`); o `TextInput` põe `aria-invalid`
sozinho (`Input.mjs:75`). Resultado: com erro, o "Nome do header" se declarava
inválido para leitor de tela e o "Token"/"Credencial" não. Achado pelo teste do
400 de `credential` da página, que reprovou numa asserção de `aria-invalid`. As
props que sobram chegam ao `<input>` pelo `...rest` (`:171`), então o formulário
passa `aria-invalid` quando o campo tem erro, com o comentário da fonte. Os
testes dos dois 400 da página afirmam `aria-describedby` e `aria-invalid`, e a
asserção reprova sem a correção (verificado: 1 de 12 no caso do token). Os
outros três `PasswordInput` do painel com validação (`LoginForm`, token do WAHA
e do Telegram em `ChannelForm`) têm o mesmo defeito e ficam para issue própria,
fora desta change.

### D9. Conferência visual

Feita **pelo dono** (convenção 14), com o painel servido do worktree. A tarefa
prepara:

- **Porta:** Vite do worktree na **4173** com `--strictPort` (aceita pelo CORS de
  desenvolvimento, V9), conferida livre com `lsof` na hora. Nunca a 5173, que é
  do checkout principal.
- **API:** a da 5017, se `GET /mcp-servers` devolver a chave `authHeaderName`
  (prova de que o binário tem a #160). Se não devolver, o dono decide subir o
  `apps/api` do `main` atual; esta change não sobe nem derruba processo de
  outro checkout.
- **Roteiro** por quadro e por esquema (grupo 7 do `tasks.md`), com os servidores de exemplo
  a cadastrar.
- **Sem suíte rodando** em nenhum worktree durante a conferência
  (convenção 19, ocorrência da conferência de protótipo).

### Divergências do protótipo (convenção 17)

| # | quadro | o protótipo | o que prevalece | por quê |
|---|---|---|---|---|
| P1 | Main, Erros | cartão marcado com borda de 1px e `#eef3fb` | 2px e `--mantine-primary-color-light` | D2: regra escrita do painel, válida nos dois esquemas |
| P2 | Main, Erros | `repeat(auto-fit, minmax(230px, 1fr))` | uma ou três colunas por container query | D3: sem linha órfã; 230 é medida da fonte do protótipo |
| P3 | Main | sem campo Descrição | Descrição mantida, entre Nome e Url | a API serve e o painel já edita; a issue não a remove |
| P4 | Main, Erros | alerta de resumo "Corrija os campos destacados antes de salvar." no topo | erros só nos campos; sucesso por notificação e navegação | o mesmo espaço mostra o sucesso simulado ("nada é gravado", regra 5); nenhum formulário do painel tem resumo (V8) |
| P5 | Main, Erros | validação local do nome do header e do token, com textos próprios (ex.: "Content-Type é um header de conteúdo…") | obrigatoriedade local; formato e texto vêm do 400 da API | regra 4 da #168; D5 |
| P6 | Main | teste com header inválido: "Corrija o nome do header antes de testar. Nenhuma requisição foi feita." | obrigatoriedade barrada no campo; 400 do teste nos campos com "Corrija os campos destacados antes de testar. Nenhuma conexão foi tentada." | D5; um texto para os quatro campos, porque o 400 pode vir de qualquer um |
| P7 | Main | edição com `None` mantido: teste com a configuração salva, sem nota | teste com a configuração digitada | D4, mudança 2 |
| P8 | Main, Erros | URL em monoespaçado; placeholder `https://mcp.exemplo.com/mcp` | campo de URL como está | fora do escopo da #168; placeholder é conteúdo de exemplo (regra 5) |
| P9 | Main, Erros | indicador de radio desenhado à mão | `Radio.Indicator` do Mantine | regra 3 |
| P10 | Main, Erros | avisos em `#fbf1de`/`#8a5a00` | `Alert color="yellow"` | precedente do painel (V8); tokens por esquema |
| P11 | Main | caixa de prévia em `#faf9f8`/`#f1f1f1` | `--buteco-surface-subtle` e `--mantine-color-default-border` | convenção 16 |
| P12 | Main | "Conexão bem-sucedida (simulada no protótipo)" | `ConnectionTestResultAlert` real | regra 5 |
| P13 | Detalhe | datas fixas, nome e URL de exemplo | dados do servidor | regra 5 |

## Árvore de pastas proposta

```
apps/frontend/src/
├── index.css                                   (modificado: classe renomeada)
├── components/
│   └── forms/                                  (novo)
│       ├── RadioCardOption.tsx                 (novo, D1)
│       └── RadioCardOption.test.tsx            (novo)
└── features/
    ├── knowledge-bases/components/
    │   └── KnowledgeBaseOriginCard.tsx         (modificado: usa RadioCardOption)
    ├── mcp-servers/
    │   ├── types/mcpServer.ts                  (modificado, D7)
    │   ├── utils/
    │   │   ├── authTypeLabels.ts               (novo, D7)
    │   │   ├── connectionTestMode.ts           (novo, D4)
    │   │   └── connectionTestMode.test.ts      (novo)
    │   ├── components/
    │   │   ├── McpServerForm.tsx               (modificado, D3–D6, D8)
    │   │   ├── McpServerForm.test.tsx          (modificado)
    │   │   ├── McpServerTable.tsx              (modificado, D7)
    │   │   ├── McpServerTable.test.tsx         (modificado)
    │   │   ├── McpServerConfigCard.tsx         (modificado, D7)
    │   │   └── McpServerConfigCard.test.tsx    (modificado)
    │   └── pages/
    │       ├── McpServerEditPage.tsx           (modificado, D4)
    │       ├── McpServerEditPage.test.tsx      (modificado)
    │       └── McpServerCreatePage.test.tsx    (modificado se o fixture pedir)
    ├── agents/…/*.test.tsx                     (fixtures, se o tsc pedir)
    └── inventory/pages/InventoryPage.test.tsx  (fixture, se o tsc pedir)
```

Nada em `libs/`.

**Projeção (rascunho até o `tsc` enumerar os fixtures, convenção 18):** criados
5 (componente e teste, `authTypeLabels.ts`, função do teste e teste);
modificados de produção 7; modificados de teste ~6 por comportamento, mais os
fixtures que o `tsc` apontar ao tornar `authHeaderName` obrigatório (até 12
arquivos, uma linha cada). Comentário é parte do produto aqui: quatro registros
de afirmação estrutural (D6) e três decisões no código (D1, D4, D5).

**Entregue (18/10/2026, `git diff --numstat` e `wc -l` sobre a árvore, sem
`openspec/`):**

| | projetado | entregue | linhas (+/−) |
|---|---|---|---|
| criados de produção | 3 | **3** (`RadioCardOption.tsx`, `authTypeLabels.ts`, `connectionTestMode.ts`) | 124 |
| criados de teste | 2 | **2** (`RadioCardOption.test.tsx`, `connectionTestMode.test.ts`) | 145 |
| modificados de produção | 7 | **7** (`index.css`, `KnowledgeBaseOriginCard.tsx`, `mcpServer.ts`, `McpServerForm.tsx`, `McpServerTable.tsx`, `McpServerConfigCard.tsx`, `McpServerEditPage.tsx`) | +386 / −155 |
| modificados de teste | ~6 por comportamento + até 12 de fixture | **12** arquivos: 4 por comportamento (`McpServerForm`, `McpServerEditPage`, `McpServerConfigCard`, `McpServerTable`), dos quais 3 também levaram fixture, e 8 só de fixture | +765 / −29 |

A contagem de arquivos bateu nas três linhas projetadas. A de fixture saiu do
`tsc` (2.2): 12 objetos em 11 arquivos, uma linha cada. **Comentário contra
lógica** nas linhas adicionadas (arquivos novos inteiros mais as linhas `+` dos
modificados; linha de comentário é a que começa por `//`, `/*`, `{/*` ou `*`, ou
está dentro de um bloco): produção **338 de lógica : 142 de comentário (1 :
0,42)**; teste **720 : 35 (1 : 0,05)**. A produção é dominada pelo
`McpServerForm`, onde os quatro registros de afirmação estrutural e as três
decisões ficam junto do código; o teste é quase todo arranjo, que é a forma de
guarda com arranjo da convenção 18.

## Testes e guardas

Todos em `apps/frontend`, unitários (Vitest + Testing Library), no worktree.

- **G1 [guarda] resultado descartado ao editar o nome do header.** Teste salvo
  ou não salvo com sucesso, editar o nome do header, o resultado some. Defeito
  para reprovar: tirar `authHeaderName` de `TestedConnectionConfig` e da
  comparação.
- **G2 [guarda] "Testar conexão" desabilitado na troca de tipo com credencial
  em branco.** Edição de um `BearerToken`, marcar "Token em header customizado",
  credencial em branco: botão desabilitado, nota presente, nenhuma chamada a
  `testSavedMcpServerConnection`. Defeito para reprovar: `connectionTestMode`
  voltar a devolver `saved` quando a credencial está em branco,
  independentemente do tipo (a regra de `McpServerForm.tsx:101` hoje). Pareado
  com a asserção determinística na função pura (`connectionTestMode.test.ts`),
  que reprova no mesmo defeito sem depender de render.
- **Demonstração (convenção 15):** cada guarda é escrito, o defeito é
  reintroduzido de propósito no componente que a correção toca, a reprovação é
  registrada no `tasks.md` com o nome do teste e a mensagem, e só então o
  defeito sai. G2 é checado nos dois lugares (função e formulário), porque o
  guarda no formulário é o que prova que o botão usa a função.
- **Negativas como `it()` próprio** (convenção 18): sem prévia em `None` e em
  `BearerToken`; sem aviso na saída de `None`; sem `authHeaderName` no payload
  com outro tipo; sem "Falha na conexão" no 400 do teste; sem linha "Header" no
  card com outros tipos; sem nota de configuração salva com `None` em edição;
  sem a mensagem do submit anterior depois de editar o campo.
- **Precedência de erros (D5):** submit com 400 em `authHeaderName`, editar o
  campo, testar com outro 400 no mesmo campo: aparece a mensagem nova. Defeito
  para reprovar: voltar ao `errors?.<campo> ?? form.errors.<campo>` de hoje.
- **Extração (D1):** `KnowledgeBaseOriginCard.test.tsx` roda sem nenhuma
  asserção editada; o `git diff` do arquivo de teste fica vazio.

## Risks / Trade-offs

- **[`N` do empilhamento errado na prática]** → medido na tarefa 7.4: corte em
  760px de container, com 13,98px de folga no pior caso (D3). Não testável na
  suíte (jsdom não faz layout, convenção 14); coberto pela medição e pela
  conferência. O comentário do `cardsColumns` carrega a medida e o gatilho
  para medir de novo.
- **[O 400 do teste deixa de aparecer no alerta]** → quem lia o motivo no
  alerta passa a lê-lo no campo; teste de formulário afirma cada chave no campo
  certo e a ausência de "Falha na conexão".
- **[Chave de 400 sem campo na tela]** → vai para o aviso inline (D5), com teste.
- **[Afirmação da tela envelhecer quando outro app mudar]** → comentário com
  arquivo, linha e gatilho em cada uma (D6). Não testável por construção: o
  gatilho é mudança em outro app; o registro é o mecanismo.
- **[Renomear a classe CSS quebrar o card de origem]** → os testes dele não
  olham a classe; a conferência visual confere o card de origem marcado nos
  dois esquemas (tarefa 7.6).
- **[`None` em edição mudar de teste salvo para digitado]** → mudança
  deliberada (D4), com teste próprio e cenário na spec.

## Migration Plan

Só frontend; sem migração de dado. A API com a #160 precisa estar no ar antes
deste painel (é o caso: a #160 está mergeada em `f505114`). Rollback: reverter
o merge; o backend continua aceitando `CustomHeader` pela API, e a janela da D7
volta.

## Open Questions

Nenhuma de produto. O `N` do empilhamento é medida, não pergunta (D3).

## Achados fora do escopo

Cada um virou issue na hora (convenção 23) e entra no PR como `Refs`, nunca como
`Closes` (convenção 24).

- **#170: campos de senha com erro de validação não marcam `aria-invalid`.**
  Achado na implementação (D8, segunda correção): o `PasswordInput` do Mantine
  9.4.2 não põe `aria-invalid` no `<input>` (`PasswordInput.mjs:159` e `:165`).
  Esta change corrigiu só o campo de credencial/token do `McpServerForm`. Ficam
  na issue os três outros campos com validação: `LoginForm.tsx:31`,
  `WahaCredentialFields.tsx:27` e `TelegramCredentialFields.tsx:13`. Se a
  correção da #170 for central (componente do painel ou versão nova do
  Mantine), o `aria-invalid` explícito do `McpServerForm` sai na mesma change,
  para não haver dois mecanismos. Criada em 18/10/2026 com `bug`, `tipo: bug` e
  `app: frontend`, no `Backlog`.
- **#171: navbar abaixo de `sm` cobre o conteúdo.** Achado na medição do `N`
  (tarefa 7.4): em viewport ≤ 768px a navbar ocupa a largura inteira, sem
  `transform` e sem `Burger`, e cobre o formulário (`AppShell.tsx:56`,
  `navbar={{ width: 224, breakpoint: 'sm' }}`
  sem `collapsed`). O comentário de `AppShell.tsx:51-55` diz que abaixo desse
  ponto "o conteúdo aperta em vez de virar gaveta", o que não é o que a tela faz.
  Anterior a esta change, que não toca o `AppShell`. Criada em 18/10/2026 com
  `bug`, `tipo: bug` e `app: frontend`, depois de buscar duplicatas (nenhuma;
  busca de controle por "aria-invalid" achou a #170).

