## Context

A linha de bases sincronizadas já tem o backend pronto:

- **#102** (`catalogo-base-sincronizada`): toda base tem `contentMode`, e a base
  `Synced` traz `syncSource` com `provider`, `folderId`, `folderName` e
  `folderUrl`, em toda resposta do catálogo, inclusive `GET /knowledge-bases`.
- **#103** (`apps-connectors-google-drive`): o `apps/connectors` serve, para o
  subject `operator`, só duas rotas: `GET /connectors/providers`
  (`[{ key, accountEmail }]`) e `GET /connectors/providers/{providerKey}/folders?parentId=`
  (`[{ id, name, kind: "SharedDrive" | "Folder", webUrl }]`). A descrição de pasta
  (`/folder?id=`) é do `service:api`, e o operador recebe `403` nela. Erros são
  `ProblemDetails` com `code`: `404` `provider-not-configured`; `422`
  `access-denied` (detalhe com o e-mail da conta), `not-a-folder`,
  `folder-trashed`; `502` `api-not-configured`, `provider-auth-failed`,
  `provider-error`; `503` `rate-limited`, `provider-unavailable`. A D8 dela
  adiou `VITE_CONNECTORS_BASE_URL` para o primeiro consumidor, que é esta change.
- **#104** (`criacao-base-sincronizada`): `POST /knowledge-bases` aceita
  `contentMode: "Synced"` com `provider` e `folderId`, valida a pasta no
  `apps/connectors` e devolve os códigos dele (`404`/`422` viram `422`), mais os
  próprios: `409` `folder-in-use` (com `knowledgeBaseId` e `knowledgeBaseName`),
  `503` `connectors-not-configured`, `503` `connectors-unavailable` e `502`
  `connectors-error` (detalhe com o status recebido). A pasta em uso é detectada
  antes da chamada externa, e a comparação de id é sensível a caixa.
- **#105** (`ciclo-de-sincronizacao`): os documentos entram pela rodada periódica
  do `apps/connectors`, que só roda com `Api__BaseUrl` configurada nele. Em
  produção isso só existe depois da #119.

O frontend hoje: `KnowledgeBaseForm` serve criação e edição com nome e descrição;
`KnowledgeBaseCreatePage` envia `{ name, description }`; o tipo `KnowledgeBase`
espelha só os seis campos antigos. Os endereços de build (`VITE_API_BASE_URL`,
`VITE_INBOX_BASE_URL`) são obrigatórios no `Dockerfile` e caem em
`http://localhost:50x7` quando `undefined` no código.

O protótipo é o canvas "Bases sincronizadas — protótipo"
(<https://claude.ai/artifact/XzvXSpPRpqaCjNZ45ooAPY>), pranchas 2a, 2b, 2c e 2d,
copiadas em `design/` (ver `design/README.md`). É referência visual; a issue e a
spec mandam, e regra que o sistema já tem vence o protótipo (convenção 17).

Três trabalhos correm em paralelo nesta máquina (#106 aqui, #138 e #47), e os
três podem editar `01`, `02`, `CHANGELOG.md` e `docs/`.

## Goals / Non-Goals

**Goals:**

- Card de Origem na criação, com Manual igual a hoje e Sincronizada completa:
  provedor, conta, seletor de pasta, pasta em uso, erros por código.
- Cliente do `apps/connectors` no frontend que a #107 reaproveita.
- Build do frontend que não quebra sem o `apps/connectors` implantado.

**Non-Goals:**

- Detalhe, listagem, filtro "Com falha" e "Sincronizar agora" (#107).
- Exclusão de base e a mensagem de `folder-in-use` que orienta excluir (#136).
- Qualquer mudança em `apps/api`, `apps/connectors`, `apps/workers` ou
  `apps/inbox`.
- `docker-compose.prod.yml`, `.env.prod.example`, nginx do stack e
  `deployment.md` (#119).
- Drive Compartilhado como pasta da base (#114, não medido).
- Colar o link da pasta em vez de navegar (alternativa recusada na issue).

**Versões:** nenhuma dependência nova nem troca de versão. `Modal`, `Radio`,
`Select`, `CopyButton` e `Breadcrumbs` estão no `@mantine/core` 9 instalado
(`CopyButton` já é usado em `AgentA2ACard.tsx` e `ChannelDetailPage.tsx`).

## Decisions

### D1. `VITE_CONNECTORS_BASE_URL` opcional; ausente desliga a opção Sincronizada

Três estados, lidos uma vez num módulo da feature:

| valor no build | significado | tela |
|---|---|---|
| `undefined` | o painel não conhece o `apps/connectors` | Sincronizada desabilitada, com explicação; nenhuma requisição ao `apps/connectors` |
| `""` | caminho relativo, mesmo domínio (stack com nginx, depois da #119) | Sincronizada habilitada |
| URL absoluta | aquele endereço (desenvolvimento: `http://localhost:5037`) | Sincronizada habilitada |

- **Sem fallback para `localhost` quando `undefined`**, ao contrário de
  `VITE_API_BASE_URL`. O fallback lá existe para `npm run dev` funcionar sem
  `.env`; aqui ele faria um build de produção sem a variável chamar
  `http://localhost:5037` no navegador do operador, que é exatamente o defeito que
  o comentário do `Dockerfile` (D2 da containerização) descreve. O
  `.env.example` do frontend passa a trazer `VITE_CONNECTORS_BASE_URL=http://localhost:5037`.
- **`Dockerfile`:** `ARG VITE_CONNECTORS_BASE_URL`, **sem** o `RUN test -n` dos
  outros dois e **sem** `ENV`. O `ENV X=${X}` com o argumento não passado grava
  `X=""`, que viraria caminho relativo, o estado errado. Argumento declarado já
  chega como variável de ambiente ao `RUN npm run build` quando passado, e fica
  ausente quando não passado (é o que a checagem `${X+x}` dos outros dois já
  usa). A tarefa de build da imagem confere os dois casos no bundle.

  **Medido antes do código (tarefa 1.2, 03/10/2026, Podman 5.8.3,
  `node:24-alpine`),** num `Dockerfile` descartável com `ARG X` e dois `RUN`, um em
  `sh` (`${X+set}`) e outro em Node (`hasOwnProperty(process.env, "X")`), que é o
  que o Vite enxerga:

  | build | `sh` | Node |
  |---|---|---|
  | sem `--build-arg` | `AUSENTE` | `AUSENTE` |
  | `--build-arg X=` | `DEFINIDA=[]` | `DEFINIDA=[]` |
  | `--build-arg X=valor` | `DEFINIDA=[valor]` | `DEFINIDA=[valor]` |

  O argumento declarado e não passado chega **ausente**, e não como `""`. A D1 se
  sustenta: a imagem de produção construída sem a variável compila a opção
  Sincronizada desabilitada, e o caminho relativo exige passar `""` de propósito.
- **Explicação na tela:** "A sincronização com pastas não está habilitada neste
  painel." mais uma linha para quem administra: falta o endereço do serviço de
  conectores na construção do painel.
- **Efeito documentado:** `docs/configuration.md` (variável opcional, os três
  estados) e `docs/development.md` (build sem a variável continua passando).
  Na #119, comentário: o build do frontend no `docker-compose.prod.yml` precisa
  de `VITE_CONNECTORS_BASE_URL: ""` junto com o bloco `/connectors` do nginx, e
  não antes dele; passar `""` sem o nginx faz as chamadas caírem no SPA.

- *Descartado, variável obrigatória como as outras (e como a D8 da #103
  pressupunha):* o próximo deploy do frontend em produção falharia no build,
  ou exigiria passar `""` sem o `apps/connectors` e sem o nginx, e a tela
  mostraria "fora do ar" para um serviço que não existe ali. A D8 da #103 recusou
  "declarar agora como opcional" porque a #106 teria de torná-la obrigatória de
  novo; o motivo que muda a conta é a ordem real de entrega: a #106 chega antes
  da #119.
- *Descartado, fallback para `localhost` em modo de desenvolvimento
  (`import.meta.env.DEV`):* deixaria o `npm run dev` sem `.env` igual aos outros
  endereços, mas cria um quarto estado que só existe fora do build e que os
  testes teriam de simular. O `.env.example` resolve o desenvolvimento.

### D2. Cliente do `apps/connectors` em `features/knowledge-bases/api/connectorsApi.ts`

No molde de `sessionsApi.ts` (cliente do `apps/inbox`): `request<T>` e erro
próprios, token do operador pelo módulo fino `auth/token`, sem cliente HTTP
comum (convenção 7). Mora na feature `knowledge-bases` porque a feature se
organiza por conceito de domínio, não por origem do dado: os dois consumidores
são telas de base (este cadastro e o "Sincronizar agora" do detalhe, #107).

Diferenças deliberadas do molde, cada uma por um motivo:

- **`ConnectorsError` carrega `status`, `code`, `detail` e
  `kind: "network" | "http"`.** Falha de rede (`fetch` rejeitado) vira
  `kind: "network"`, que é o "fora do ar" que o aceite da issue pede explícito.
  O molde deixa o `TypeError` escapar, e a tela não distingue rede de bug.
- **`401` não limpa o token nem redireciona.** No `apps/api` e no `apps/inbox`,
  `401` é token vencido. Aqui também pode ser chave de assinatura divergente
  entre os processos (`configuration.md`, "Restrições entre processos"), e
  deslogar o operador a cada abertura do seletor seria um laço sem saída. Token
  vencido de verdade é pego pela próxima chamada ao `apps/api`, que já desloga.
  É o mesmo raciocínio da #104, que não repassa `401` do `apps/connectors`.
- **Endereço de D1 lido por função** (`connectorsBaseUrl(): string | null`), para
  os testes trocarem o valor com `vi.stubEnv` sem recarregar módulo.
- **Hooks em `useConnectors.ts`:** `useConnectorProvidersQuery({ enabled })` e
  `useConnectorFoldersQuery(provider, parentId, { enabled })`, chaves
  `['connectors', 'providers']` e `['connectors', provider, 'folders', parentId ?? null]`,
  com `retry: false`. O `QueryClient` do app não tem `defaultOptions`, então
  valeriam três novas tentativas com espera crescente, e o seletor ficaria
  vários segundos "carregando" um erro que já chegou. O operador tem "Tentar de
  novo" e "Recarregar".

**Para a #107:** acrescentar `requestKnowledgeBaseSync(id)`
(`POST /connectors/knowledge-bases/{id}/sync`) neste mesmo arquivo, e reusar
`ConnectorsError` e a tabela da D3 (`utils/connectorErrors.ts`). O comentário do
módulo diz isso.

- *Descartado, `features/connectors/`:* feature por origem do dado, contra a
  convenção 7, e sem tela própria.
- *Descartado, chamar o `apps/connectors` através do `apps/api`:* não existe
  rota de proxy, e criá-la é tarefa em `apps/api`, fora desta change. A #103
  desenhou as rotas do operador para o painel chamar direto.

### D3. Texto por código de erro

Função pura `connectorErrorMessage(error)` em
`features/knowledge-bases/utils/connectorErrors.ts`, usada pelo seletor, pela
lista de provedores e pelo erro do cadastro. O texto vem do `code`, nunca do
`title` (que é do servidor, e muda sem a tela saber); do `detail`, só os dados
marcados abaixo. No cadastro, toda mensagem termina com "Nenhuma base foi
criada."

| origem | status | `code` | texto da tela |
|---|---|---|---|
| rede | — | (falha de `fetch`) | Não foi possível falar com o serviço de conectores. Ele pode estar fora do ar; tente de novo em instantes. |
| navegação, cadastro | 404 / 422 | `provider-not-configured` | O provedor {nome} não está configurado no serviço de conectores desta instalação. |
| navegação, cadastro | 422 | `access-denied` | A conta {detail: e-mail} não tem acesso a esta pasta. Compartilhe a pasta com essa conta como Leitor e tente de novo. |
| navegação, cadastro | 422 | `not-a-folder` | O item escolhido não é uma pasta. Escolha uma pasta. |
| navegação, cadastro | 422 | `folder-trashed` | Esta pasta está na lixeira do Drive. Restaure a pasta ou escolha outra. |
| navegação, cadastro | 502 | `api-not-configured` | A Drive API não está ativada no projeto da conta de serviço. É configuração da instalação, não da pasta. |
| navegação, cadastro | 502 | `provider-auth-failed` | O Google recusou a credencial da conta de serviço. É configuração da instalação, não da pasta. |
| navegação, cadastro | 502 | `provider-error` | O Google recusou a operação ({detail, quando houver}). |
| navegação, cadastro | 503 | `rate-limited` | O Google limitou as chamadas por um momento. A falha é passageira: tente de novo em instantes. |
| navegação, cadastro | 503 | `provider-unavailable` | O Google não respondeu. Tente de novo em instantes. |
| navegação | 401 | — | O serviço de conectores não aceitou a sua sessão. Saia e entre de novo no painel; se continuar, é configuração da instalação. |
| navegação | 403 | — | O serviço de conectores recusou esta consulta para o operador. É configuração da instalação. |
| cadastro | 409 | `folder-in-use` | Esta pasta já é usada pela base “{knowledgeBaseName}”. Uma pasta alimenta uma base só, e continua ocupada mesmo com a base inativa. Escolha outra pasta. |
| cadastro | 503 | `connectors-not-configured` | O servidor desta instalação ainda não está ligado ao serviço de conectores. É configuração da instalação; a base manual continua disponível. |
| cadastro | 503 | `connectors-unavailable` | O servidor não conseguiu falar com o serviço de conectores para validar a pasta. Tente de novo em instantes. |
| cadastro | 502 | `connectors-error` | O serviço de conectores respondeu de forma inesperada ao validar a pasta (status {detail}). |
| ambos | qualquer | código desconhecido no formato | Erro inesperado (código `{code}`). |
| ambos | qualquer | sem `code` | Erro inesperado (status {status}). |

- **`folder-in-use`** nomeia a base e diz que a pasta segue ocupada com a base
  inativa, e **não** tem `exclu`, `remov` nem `apag`: a rota de exclusão já
  existe (#108), mas o painel não oferece a ação, e mandar o operador fazer o que
  a tela não deixa é o problema que a #136 resolve junto com o botão. Quando a
  #136 entrar, ela troca este texto e a asserção negativa.
- **`rate-limited`** diz que é passageira e não fala de acesso, pelo comentário
  da #105 na #107. No cadastro a nova tentativa é manual; o "próxima tentativa
  automática" é do ciclo, e fica para a #107.
- **`api-not-configured`** é falha da implantação, e o texto diz isso, não "sem
  acesso à pasta" (etapa 0, comentário da #107).
- **`400` do cadastro** continua virando erro por campo
  (`ValidationProblemDetails`); `provider` e `folderId` aparecem no campo da
  pasta.
- O erro do cadastro sincronizado aparece num `Alert` dentro do card de Origem,
  persistente, mantendo o formulário preenchido. O erro genérico do cadastro
  manual continua sendo a notificação de hoje.

- *Descartado, exibir o `title` do `ProblemDetails`:* é o que o
  `knowledgeBasesApi.ts` faz no `message` do `ApiError`, mas o título é texto do
  servidor, em inglês em parte das rotas, e a #104 fixou que o código chega
  "sem traduzir o código em frase" justamente para a tela traduzir.
- *Descartado, notificação (toast) para o erro do cadastro sincronizado:* some
  sozinha, e o texto de pasta em uso ou de acesso negado precisa ficar na tela
  enquanto o operador age sobre ele.

### D4. Pasta em uso pelo cruzamento com `GET /knowledge-bases`

A página de criação consulta `useKnowledgeBasesQuery()` (a mesma chave da
listagem) **só com o seletor aberto**, e passa ao seletor um mapa
`provider + "\u0000" + folderId → nome da base`, montado das bases com
`syncSource`, ativas e inativas. Comparação exata e sensível a caixa, como o
índice único do `apps/api` (#104: `AbC` e `abc` não conflitam).

- Pasta em uso: escolha desabilitada, "Já sincronizada pela base “<nome>”",
  e continua podendo ser aberta (as subpastas dela podem estar livres).
- **Sem link para a base.** O protótipo põe link; dentro do modal, navegar para o
  detalhe descarta o formulário preenchido.
- Listagem de bases com erro: aviso no seletor de que não foi possível conferir
  as pastas usadas, e que o cadastro confere ao criar; nenhuma pasta desabilitada.
  Carregando: nenhuma marcação e nenhum aviso. Nos dois casos a tela não afirma
  que a pasta está livre; afirma só o que sabe (convenção 13).
- O `409` `folder-in-use` continua sendo a garantia: a base pode ter sido criada
  depois da listagem, por outra aba ou outro operador.

- **Corrigido na implementação: a consulta só nasce com o seletor aberto.** A
  primeira versão desta decisão consultava a listagem ao montar a página, "já em
  cache quando o operador vem dela". A causa da correção: o cadastro manual passaria
  a fazer uma requisição que não fazia, contra o aceite de que ele continua igual
  ao de hoje, e o mapa só serve ao seletor. Efeito colateral medido: a consulta usa
  o hook da listagem, com as três novas tentativas padrão do React Query, então o
  aviso de listagem indisponível aparece ~7s depois de abrir o seletor; até lá o
  seletor não marca nem avisa nada, que é o estado "carregando" acima. O
  `retry: false` dos hooks do `apps/connectors` (D2) não foi estendido a este
  hook, que é da tela de listagem.

- *Descartado, consultar uma rota de "pasta em uso" no `apps/api`:* não existe, e
  seria tarefa de backend. A listagem já traz `syncSource` de todas as bases.
- *Descartado, esconder a pasta em uso:* o operador não saberia por que a pasta
  que ele compartilhou não aparece.

### D5. Navegação no seletor

- **Estado na página, componente apresentacional no modal (convenção 7).** A
  página guarda o caminho (pilha de `{ id, name }`), o nível atual vem de
  `useConnectorFoldersQuery(provider, topo da pilha, { enabled: modal aberto })`,
  e o `FolderPickerModal` recebe itens, estado da consulta, mapa de pastas em uso
  e callbacks. Ele não importa hook de consulta.
- **Nível de cima:** dois grupos pelo `kind`, "Drives compartilhados" e "Pastas
  compartilhadas com a conta", na ordem devolvida (a rota já ordena por nome e
  id). Grupo vazio não aparece; os dois vazios viram a explicação de que nada foi
  compartilhado com a conta, com o e-mail.
- **Dentro de um item:** a contagem devolvida ("3 pastas", "1 pasta") é medida e
  pode ser exibida; `[]` vira "Esta pasta não tem subpastas." — resposta que
  chegou dizendo que não há, nunca travessão nem "0 pastas" (convenção 13, quarto
  estado).
- **Escolher × abrir.** `Folder` tem a escolha (rádio) e o botão de abrir;
  `SharedDrive` só abre. A raiz do Drive Compartilhado não foi medida como pasta
  de base (#114), e o protótipo também não a oferece para escolha. A escolha
  sobrevive à navegação, e o rodapé mostra "Os arquivos da raiz de “X” entram na
  base." com o botão "Selecionar “X”".
- **Carregando:** `Loader` com "Carregando pastas…", e **nenhum** texto de erro
  ou de vazio. É o defeito que a #99 corrigiu no card de agentes: afirmar falha
  enquanto a consulta não respondeu.
- **Erro:** `Alert` com o texto da D3 e "Tentar de novo"; nunca lista vazia no
  lugar do erro (a #103 garante que pasta sem acesso é `422`, não `200 []`).
- **Recarregar:** uma faixa fixa entre a lista e o rodapé diz que só aparece o
  que foi compartilhado com o e-mail da conta e oferece "Recarregar", que refaz o
  nível atual (`refetch`). É o "compartilhe e recarregue" do protótipo, com o
  botão que ele pressupõe, fora da área rolável (C10).
- **Provedor:** uma consulta só, quando a opção Sincronizada é marcada. Nome de
  exibição por mapa local (`google-drive` → "Google Drive"); chave desconhecida
  aparece como veio, sem inventar nome. Um provedor vem escolhido; trocar de
  provedor limpa a pasta.

- *Descartado, árvore expansível no lugar da descida por nível:* uma consulta
  por nó aberto, estado de expansão por nó, e a prancha desenha a descida com
  caminho.
- *Descartado, deixar escolher a raiz do Drive Compartilhado:* o acesso da
  service account em Drive Compartilhado não foi medido (#114); a tela não
  oferece o que ninguém conferiu.

### D6. Correções de protótipo

Divergências entre as pranchas 2a–2d e o código ou as regras de hoje, e o que
prevalece. Entram na lista viva "Correções de protótipo" do `02`.

| # | prancha | o protótipo | o que prevalece | por quê |
|---|---|---|---|---|
| C1 | 2a, 2b | bloco da descrição resumido ("Prévia … sem mudança nesta proposta"), orientação curta | o bloco de hoje, inteiro | a própria prancha diz que não muda; a orientação de hoje vem da medição `0d` |
| C2 | 2a, 2b | prosa com `max-width: 760px` | `PROSE_MAX_WIDTH` (620px) | régua de leitura já medida no formulário |
| C3 | 2c | "Pastas compartilhadas com a conta" sem opção de escolha no nível de cima | pasta compartilhada direto com a conta **pode** ser escolhida | é o caminho documentado pela etapa 0 (compartilhar a pasta como Leitor); sem isso, o caso mais comum não teria como ser escolhido |
| C4 | 2d | nome da base em uso como link | nome sem link, no seletor e no erro `folder-in-use` | navegar descarta o formulário |
| C5 | 2b | rodapé "Os documentos entram na primeira sincronização, que começa depois de criar a base" | nota sem prazo: os documentos entram pela sincronização com a pasta depois de criar a base | a primeira rodada depende do intervalo do ciclo e de `Api__BaseUrl` no `apps/connectors` (#105, #119); "começa depois de criar" insinua imediato (convenção 13) |
| C6 | 2b | seletor "Google Drive" | nome de exibição por mapa local, chave crua para provedor desconhecido | a rota devolve só `key` e `accountEmail` |
| C7 | 2c, 2d | paleta própria do modal (`#2a6ecb`, `#dee2e6`, raio 4px) | `Modal` do Mantine com os tokens do tema | variável por esquema (convenção 16); a paleta da prancha só existe no claro |
| C8 | — | não desenhados: Sincronizada indisponível, provedores carregando/erro/nenhum, nenhuma pasta escolhida, seletor carregando/erro/vazio, listagem de bases indisponível, erro do cadastro | desenhados por esta change com os padrões do painel (`Alert`, `Loader`, texto `dimmed`) | estados que existem no sistema; o aceite da issue exige o erro explícito |
| C9 | 2b | a nota de documentos da base manual como bloco sem borda | o `Paper` com borda de hoje | aceite: o formulário manual continua igual |
| C10 | 2c | "Só aparece o que foi compartilhado… recarregue" no fim da lista, dentro da rolagem | faixa fixa entre a lista e o rodapé, com o botão "Recarregar" | achado da conferência (R1-5): com mais pastas que a altura, a orientação e o botão ficariam escondidos embaixo da lista |

A conferência visual (D7) acrescentou a C10; as outras correções dela são de
medida, e não contrariam a prancha (tabela da D7).

### D7. Conferência visual, no molde da #99

- **Painel:** `npm run dev` deste worktree numa porta livre da faixa
  56100–56199 (conferida com `lsof -i :<porta>`), com `VITE_API_BASE_URL` e
  `VITE_CONNECTORS_BASE_URL` apontando para portas da mesma faixa **sem nenhum
  processo escutando**: toda resposta do `apps/api` e do `apps/connectors` é
  montada pelo CDP (`Fetch.enable` com os dois padrões, `Fetch.fulfillRequest`
  com cabeçalhos CORS; requisição pausada sem continuar para "carregando";
  `Fetch.failRequest` para "fora do ar"). Nem `apps/connectors` real, nem Google,
  nem o `apps/api` antigo da 5017. Token do operador gravado no
  `sessionStorage` pelo CDP. Se o preflight `OPTIONS` não puder ser atendido pelo
  `Fetch`, o Chrome da conferência sobe com perfil próprio e
  `--disable-web-security`, registrado no `tasks.md`. Portas usadas registradas
  no `tasks.md`.
- **Variável ausente:** uma segunda instância do Vite sem
  `VITE_CONNECTORS_BASE_URL`, para o estado "Sincronizada indisponível" e para
  conferir, pelo log do CDP, que nenhuma requisição sai para o `apps/connectors`.
- **Chrome headless por CDP**, claro e escuro, a 1440px e a ~1860px.
- **Estados:** formulário manual (2a); Sincronizada indisponível; provedores
  carregando; provedores com erro de rede; nenhum provedor; provedor sem pasta
  escolhida; pasta escolhida (2b); erro de cadastro `folder-in-use`; seletor no
  nível de cima (2c); seletor dentro de um Drive com pasta escolhida e pasta em
  uso (2d); seletor carregando; seletor com erro; pasta sem subpastas; listagem
  de bases indisponível no seletor.
- **Dimensões comparadas** contra as pranchas: largura dos cards (igual à do
  bloco da descrição), grade das duas opções (duas colunas, vão de 12px), borda e
  raio da opção marcada, altura do campo da pasta, largura e altura do modal,
  altura das linhas do seletor e recuo do caminho. Iterar até uma rodada sem
  achado; capturas em `design/capturas/`, nomeadas
  `<estado>_<esquema>_<largura>.png`, com as medidas em
  `design/capturas/medidas-r<N>.json`.
- **A validação manual pelo mantenedor fica pendente** (convenção 14), e o
  archive espera por ela.

**Resultado (03/10/2026): duas rodadas, a segunda sem achado.** O preflight
`OPTIONS` foi atendido pelo próprio `Fetch`, sem `--disable-web-security`. Achados
da rodada 1, todos corrigidos:

| # | o quê | medido | correção |
|---|---|---|---|
| R1-1 | conteúdo do cartão Manual centralizado na vertical quando o vizinho tem duas linhas | 25px do topo contra 16px | `display: flex; align-items: flex-start` no `Radio.Card`, que é um botão |
| R1-2 | descrição das opções maior que na prancha | 14px contra 13px | `fz={13}` |
| R1-3 | modal mais baixo que as pranchas 2c/2d | 543px contra 640px | área rolável de 417px; modal final de 720 × 639px |
| R1-4 | caixa da conta no tom da prévia da descrição | `--buteco-surface-subtle` contra o `#f5f4f2` da prancha 2b | `--buteco-page-bg`, declarada nos dois esquemas |
| R1-5 | orientação e "Recarregar" cortadas no fim da rolagem | metade da linha visível no nível de cima | faixa fixa (C10) |
| R1-6 | linhas do seletor mais altas que na prancha | 53px contra ~45px | botão de abrir `size="sm"`; linha final de 47px |

O raio das opções ficou no do tema (9px, o mesmo dos outros cards do formulário),
contra os 8px da prancha: não é correção, é o raio que o painel já usa.

- *Descartado, subir `apps/api` e `apps/connectors` locais com conector falso:*
  exigiria Postgres, porta nova para cada um e dados gravados, para estados que o
  CDP produz sem efeito colateral; e o `apps/connectors` do worktree não tem
  conector falso fora da suíte.

### D8. Onde fica cada peça, e o tipo `KnowledgeBase`

- `KnowledgeBaseForm` ganha a prop `origin?: ReactNode`, renderizada depois do
  bloco da descrição, e a prop `footerNote?: ReactNode` que substitui a nota de
  documentos quando passada. A edição não passa nenhuma das duas e fica igual. O
  formulário segue apresentacional; a validação de "pasta obrigatória" fica com
  quem monta o card (a página), porque só ela sabe a origem marcada. Na
  implementação ela entrou como a prop `validateExtra?: () => boolean`, chamada
  junto com a validação de nome e descrição (também quando esta falha, para os
  erros aparecerem de uma vez).
- **Testes da página em dois arquivos.** `KnowledgeBaseCreatePage.test.tsx`
  mocka `createKnowledgeBase` no módulo inteiro, e continua cobrindo o cadastro
  manual como antes. Os aceites desta change estão em
  `KnowledgeBaseCreatePage.sync.test.tsx`, sem mock de módulo e com o `fetch`
  global interceptado, porque as negativas de rede ("nenhuma chamada ao
  `apps/connectors`") precisam afirmar sobre tudo o que saiu, e cada uma confere
  antes que o interceptor viu as chamadas ao `apps/api`.
- `KnowledgeBaseOriginCard` (apresentacional): as duas opções, o bloco
  sincronizado (provedor, conta, pasta) e o `Alert` de erro do cadastro.
- `FolderPickerModal` (apresentacional), D5.
- `KnowledgeBaseCreatePage`: estado da origem, consultas (provedores, pastas,
  bases), montagem do corpo e tratamento do erro.
- **Tipo:** `KnowledgeBase` ganha `contentMode: 'Manual' | 'Synced'` e
  `syncSource: KnowledgeBaseSyncSource | null`, obrigatórios, como o fio (#102).
  `syncState` **não** entra: nenhum código desta change o lê, e campo declarado
  sem consumidor é a convenção 25; a #107 o acrescenta. Os testes que montam
  `KnowledgeBase` à mão ganham os dois campos.
- `CreateKnowledgeBaseInput` ganha a variante sincronizada como união
  (`{ name, description }` | `{ name, description, contentMode: 'Synced', provider, folderId }`),
  para o corpo manual continuar sem as chaves novas por construção.

- *Descartado, formulário separado para a criação:* duplicaria nome, descrição,
  orientação e preview, que são o grosso da tela e têm guardas próprias.
- *Descartado, campos novos opcionais no tipo:* o fio sempre os traz; opcional
  esconderia do compilador o teste que esquece de declarar a origem.

## Árvore de pastas proposta

```
apps/frontend/
├── .env.example                                   (+ VITE_CONNECTORS_BASE_URL)
├── Dockerfile                                     (+ ARG opcional, sem test nem ENV)
└── src/
    ├── vite-env.d.ts                              (+ VITE_CONNECTORS_BASE_URL?)
    ├── index.css                                  (+ estado marcado da opção de origem)
    └── features/knowledge-bases/
        ├── api/
        │   ├── connectorsApi.ts                   (novo: request<T>, ConnectorsError, listProviders, listFolders)
        │   ├── knowledgeBasesApi.ts               (+ code, detail e knowledgeBaseName no ProblemDetails)
        │   ├── connectorsApi.test.ts              (novo)
        │   ├── useConnectors.ts                   (novo: hooks com retry: false)
        │   └── useConnectors.test.ts              (novo)
        ├── components/
        │   ├── KnowledgeBaseForm.tsx              (+ props origin e footerNote)
        │   ├── KnowledgeBaseForm.test.tsx
        │   ├── KnowledgeBaseOriginCard.tsx        (novo)
        │   ├── KnowledgeBaseOriginCard.test.tsx   (novo)
        │   ├── FolderPickerModal.tsx              (novo)
        │   └── FolderPickerModal.test.tsx         (novo)
        ├── pages/
        │   ├── KnowledgeBaseCreatePage.tsx        (origem, consultas, corpo, erro)
        │   ├── KnowledgeBaseCreatePage.test.tsx   (inalterado: mock de módulo do cadastro manual)
        │   ├── KnowledgeBaseCreatePage.sync.test.tsx (novo: fetch interceptado, aceites da issue)
        │   └── KnowledgeBaseEditPage.test.tsx     (+ edição de base sincronizada)
        ├── types/
        │   ├── knowledgeBase.ts                   (+ contentMode, syncSource, input sincronizado)
        │   └── connectors.ts                      (novo: ConnectorProvider, ConnectorFolder)
        └── utils/
            ├── connectorErrors.ts                 (novo: tabela da D3 e nome de exibição do provedor)
            ├── connectorErrors.test.ts            (novo)
            ├── foldersInUse.ts                    (novo: mapa da D4)
            └── foldersInUse.test.ts               (novo)

openspec/changes/frontend-cadastro-base-sincronizada/design/
├── README.md, NovaBaseManual.dc.html, NovaBase.dc.html,
│   SeletorPasta.dc.html, SeletorPastaDrive.dc.html, canvas.json
└── capturas/                                      (conferência, D7)
```

Nada em `libs/`.

## Risks / Trade-offs

- **[Risco] Preflight CORS em produção.** O `apps/connectors` hoje só tem CORS de
  desenvolvimento; em produção, com `""`, a chamada é do mesmo domínio e não há
  preflight. → Fica na #119, pelo comentário da D1; aqui o caso relativo é
  coberto por teste de URL montada.
- **[Risco] Cruzamento de pasta em uso desatualizado.** → O `409` continua sendo
  a garantia, e o texto dele (D3) é testado; o cruzamento é ajuda visual (D4).
- **[Risco] `ARG` sem `ENV` não chegar ao `npm run build`.** → Tarefa de build da
  imagem com e sem o argumento, procurando o endereço no bundle.
- **[Risco] Texto de `folder-in-use` contradizer a #136 quando ela entrar.** →
  A asserção negativa é explícita e nomeada, e a #136 já declara trocá-la.
- **[Trade-off] `401` do `apps/connectors` não desloga.** Token vencido aparece
  como erro no seletor até a próxima chamada ao `apps/api`. → Texto do `401`
  orienta sair e entrar de novo.
- **[Risco] Conflito de edição com a #138 e a #47** em `01`, `02`,
  `CHANGELOG.md` e `docs/`. → Seções acrescentadas, nunca trechos reescritos;
  registrado no `tasks.md`.

## Migration Plan

Sem migração de dados. O deploy do frontend sem `VITE_CONNECTORS_BASE_URL`
publica a tela com Sincronizada indisponível; a #119 liga a variável junto com o
`apps/connectors` e o nginx. Reverter é reverter o frontend: o backend não muda.

## Open Questions

Nenhuma de produto. Os textos da D3 passam pela revisão do mantenedor junto com
este documento.
