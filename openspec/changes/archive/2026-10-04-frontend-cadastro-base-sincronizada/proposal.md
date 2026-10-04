**Issue:** #106

## Why

O `apps/api` já cria base sincronizada (`POST /knowledge-bases` com
`contentMode: "Synced"`, #104) e o `apps/connectors` já navega pelas pastas que
a conta de serviço enxerga (#103), mas o painel só sabe criar base manual. Sem
a tela, criar uma base sincronizada exige chamar a API à mão e adivinhar o id da
pasta, sem ver o que a conta de fato enxerga.

## What Changes

- **Card "Origem dos documentos"** no formulário de nova base (pranchas 2a e
  2b): Manual ou Sincronizada, com o aviso de que a origem não muda depois de
  criada. Manual vem marcada, e o cadastro manual continua enviando o mesmo
  corpo de hoje.
- **Sincronizada:** provedor entre os configurados no `apps/connectors`, e-mail
  da conta de serviço com botão de copiar e a instrução de compartilhar a pasta
  como Leitor, e a pasta escolhida com o link "Abrir no Drive".
- **Seletor de pasta em modal** (pranchas 2c e 2d): Drives Compartilhados e
  pastas compartilhadas com a conta no nível de cima, descida por subpastas,
  caminho de volta, e os estados de carregamento, vazio e erro. A pasta já
  usada por outra base aparece desabilitada com o nome dela, pelo cruzamento com
  a listagem de bases (`syncSource`); o `409` do `apps/api` continua sendo a
  garantia.
- **Texto em português para cada código de erro** que o cadastro (#104) e a
  navegação (#103) podem devolver. O de pasta em uso nomeia a base e não manda
  excluir nada (a orientação de exclusão é da #136).
- **Cliente do `apps/connectors` no frontend**, no padrão do cliente do
  `apps/inbox`, dentro da feature de bases, para a #107 reaproveitar no
  "Sincronizar agora".
- **`VITE_CONNECTORS_BASE_URL` opcional no build.** Sem ela, a opção
  Sincronizada fica indisponível com uma explicação e nenhuma chamada ao
  `apps/connectors` é feita. O `apps/connectors` só chega em produção com a
  #119, e a variável obrigatória quebraria o próximo deploy do frontend.
- A nota de rodapé do formulário muda conforme a origem.
- O tipo `KnowledgeBase` do frontend passa a ler `contentMode` e `syncSource`,
  que o `apps/api` já devolve desde a #102.

Nenhuma mudança em `apps/api`, `apps/connectors`, `apps/workers` ou
`apps/inbox`: a tela usa só rotas que já existem. A implantação em produção
(compose, nginx, `deployment.md`) é da #119.

## Capabilities

### New Capabilities

- `knowledge-base-sync-source-ui`: escolha da origem sincronizada no cadastro
  de base — provedor, conta de serviço, seletor de pasta, pasta em uso, texto de
  cada código de erro, e o comportamento sem `VITE_CONNECTORS_BASE_URL`.

### Modified Capabilities

- `knowledge-base-catalog-ui`: o requisito "Criação e edição de base de
  conhecimento" ganha o card de Origem na criação, a nota de rodapé por origem,
  e a garantia de que o cadastro manual e a edição continuam como hoje.

## Impact

- **`apps/frontend`:** formulário e página de nova base, componentes novos do
  card de Origem e do seletor, `connectorsApi.ts` com hooks próprios, tipo
  `KnowledgeBase`, `vite-env.d.ts`, `.env.example` e `Dockerfile` (argumento
  de build opcional, sem a checagem de presença dos outros dois).
- **Documentação:** `docs/development.md` e `docs/configuration.md` (variável de
  build opcional), `02-HISTORICO_E_STATUS.md` (entrada e "Correções de
  protótipo"), `CHANGELOG.md`. Comentário na #119 com a variável que o
  frontend precisa em produção.
- **Sem dependência nova.** `CopyButton` e `Modal` já estão no Mantine
  instalado.
