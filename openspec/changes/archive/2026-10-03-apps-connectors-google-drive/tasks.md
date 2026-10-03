# Tarefas — `apps-connectors-google-drive` (#103)

**Branch:** `feat/103-apps-connectors`, criada de `06ce6e5` (`main` atualizada, sem
commits à frente).

**App afetado: `apps/connectors` (novo).** Nenhuma tarefa muda código de
`apps/api`, `apps/workers`, `apps/inbox` ou `apps/frontend`. Fora de
`apps/connectors`: documentação, templates de issue e contexto do OpenSpec.
**A implantação em produção não está nesta change** (D10): compose de produção,
`.env.prod.example`, nginx do stack, `deployment.md` e a verificação pós-deploy
foram para a #119. O `Dockerfile` do app e o build da imagem ficam aqui.

---

## 1. Verificações antes do código

- [x] 1.1 [`apps/connectors`] Gerar as respostas gravadas do Google em
  `tests/Buteco.Connectors.Tests/Fixtures/GoogleDrive/` a partir de
  `~/.cache/buteco-agents/drive-0/out/` (P2, P3, P1 e acréscimos), **sanitizadas**:
  e-mails, ids, nomes de pasta e de arquivo e hashes de e-mail trocados por valores
  sintéticos, base64 da imagem trocado por um PNG mínimo válido mantendo a forma
  `[image1]: <data:image/png;base64,…>`. Conferir com `grep -rE '@gmail|@[a-z-]+\.iam\.gserviceaccount\.com'` (mais os prefixos dos ids reais das duas pastas)
  sobre a pasta que nada do original sobrou. Registrar aqui de quais arquivos cada
  fixture saiu.
  **Feito (03/10/2026).** Os registros da etapa 0 são resumos do script Python
  (status, `reason`, campos pedidos), não os corpos brutos da API; as fixtures foram
  montadas no formato real da Drive API com os campos medidos. Origem de cada
  arquivo em `Fixtures/GoogleDrive/LEIA-ME.md`: `raiz-p3.json` (P3,
  `out/p2-p3-inicial.json`, mais `doc_bloqueado` do acréscimo à P1), `pasta-
  principal.json` (`out/sonda.json`), `export-doc-com-imagem.md` (P1, imagem trocada
  por PNG 1×1), `export-doc-tabela-e-listas.md` (P1, sem alteração), `download-
  md.md` (sintético). O `grep` da tarefa, ampliado com o número do projeto Google e
  os dois hashes de dono, não achou nada.
- [x] 1.2 [`apps/connectors`] Escrever as respostas **documentadas e não medidas**
  (`404 notFound`, `403 insufficientFilePermissions`, `403 userRateLimitExceeded`,
  `429 rateLimitExceeded`, `401 authError`, `400 invalid_grant` da troca de token,
  `drives.list`, `files.list` com `sharedWithMe`) no formato do corpo de erro lido
  em `developers.google.com/workspace/drive/api/guides/handle-errors`, com um
  comentário em cada fixture dizendo "documentado, não medido" e a URL.
  **Feito, com desvio de forma:** os corpos de erro são montados em código
  (`GoogleDriveFakeHandler.GoogleError`), no formato da página de erros, em vez de
  arquivos com comentário (JSON não tem comentário). A marcação "medido" ou
  "documentado, não medido" de cada um está no `LEIA-ME.md` das fixtures;
  `accessNotConfigured` e `cannotExportFile` usam o `reason` e a mensagem medidos.
- [x] 1.3 [`apps/connectors`] Confirmar, num teste rápido contra o host construído,
  o `RoutePattern.RawText` das três rotas sob `MapGroup("/connectors")` antes de
  escrever a tabela da D1 (a D6 da #102 achou barra final em `MapGet("/")`). Se o
  padrão sair diferente do da D9, corrigir a tabela no `design.md`.
  **Medido:** `/connectors/providers`, `/connectors/providers/{providerKey}/folders`
  e `/connectors/providers/{providerKey}/folder`, sem barra final
  (`RoutePatternTests`). A tabela da D9 não mudou.

## 2. Esqueleto do app

- [x] 2.1 [`apps/connectors`] Criar `Connectors.sln`, `src/Buteco.Connectors`
  (`Microsoft.NET.Sdk.Web`, sem pacote NuGet de produção) e
  `tests/Buteco.Connectors.Tests` (`Microsoft.AspNetCore.Mvc.Testing`,
  `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`,
  `coverlet.collector`, todos já em `Directory.Packages.props`). Nenhum
  `ProjectReference` para fora de `apps/connectors`.
- [x] 2.2 [`apps/connectors`] Copiar do `apps/inbox` `TokenService`, `ITokenService`,
  `TokenValidationResult`, `TokenSigningOptions`, `OperatorTokenAuthenticationHandler`,
  `AnonymousRouteClassification` e `RouteAuthenticationExtensions`, com o namespace
  trocado e o comentário de espelhamento do `TokenService` citando as três cópias.
  `Auth:TokenSigningKey` ausente ou vazia derruba o boot.
  Os comentários de espelhamento do `apps/api` e do `apps/inbox` **não** foram
  tocados (a #116 edita o `apps/inbox` em paralelo); só a cópia nova cita as três.
  O guarda confirmou o fail-fast: sem o `if`, os três casos de
  `BootSemChave*`/`BootComChave*` reprovaram.
- [x] 2.3 [`apps/connectors`] `Program.cs` com autenticação, CORS (`Cors:AllowedOrigins`),
  `/health` anônimo classificado como `HealthProbe`,
  `ValidateRouteAuthenticationClassification("/health")`, `appsettings*.json`
  (Production com o mesmo bloco de log dos outros apps) e `launchSettings.json` na
  porta 5037.
  O `appsettings.Production.json` não repete o bloco dos outros apps
  (`Database.Command`), porque este app não tem banco: põe
  `System.Net.Http.HttpClient` em `Warning`, que gravaria a URL de cada chamada ao
  Google. **Achado de teste:** o `ConfigureAppConfiguration` da
  `WebApplicationFactory` só chega depois que o `Program.cs` lê a configuração antes
  do `Build()`; a fábrica usa `UseSetting`.
- [x] 2.4 [`apps/connectors`] `Dockerfile` no molde de `apps/inbox/Dockerfile`
  (contexto na raiz, `aspnet:10.0` sem `-alpine`, `curl` para o `HEALTHCHECK`,
  `USER app`, porta 8080).
- [x] 2.5 [`apps/connectors`] Testes: `/health` sem token responde `200`; rota sem
  token responde `401`; token de assinatura inválida e token expirado respondem
  `401`; boot sem `Auth__TokenSigningKey` falha nomeando a chave; rota anônima sem
  classificação derruba o boot; preflight com origem configurada e com origem não
  configurada.

## 3. Autorização por tabela de subjects (D1)

- [x] 3.1 [`apps/connectors`] `ConnectorsSubjectAuthorizationHandler` com a tabela
  da D1 (`operator` em duas rotas, `service:api` em uma), requisito na
  `FallbackPolicy` junto de `RequireAuthenticatedUser()`. Subject fora da tabela
  não autoriza nada.
- [x] 3.2 [`apps/connectors`] `ConnectorsSubjectRouteValidation` sobre o host
  construído, nos dois sentidos (entrada sem rota; rota autenticada sem dono), com
  `FindProblems(endpoints, tabela)` separado da extensão, como
  `ServiceScopeRouteValidation` do `apps/api`. Chamado no `Program.cs` depois de
  todos os `Map*`.
- [x] 3.3 [`apps/connectors`] Matriz de testes com token validamente assinado para
  `operator`, `service:api`, `service:inbox`, `service:connectors` e `qualquer`
  contra as três rotas: `403` em toda célula fora da tabela; nas células da tabela,
  nem `401` nem `403`. Os dois casos nomeados no aceite (operador na descrição de
  pasta; `service:api` nas rotas do operador) com nome próprio.
- [x] 3.4 [`apps/connectors`] Testes da checagem: tabela com entrada inexistente
  reprova nomeando subject, método e padrão; endpoint autenticado fora da tabela
  reprova nomeando o padrão; composição real passa (host da `WebApplicationFactory`
  construído).
- [x] 3.5 [`apps/connectors`] Guarda contra o defeito real (convenção 15): trocar
  a `FallbackPolicy` por só `RequireAuthenticatedUser()` (o defeito da #116), ver a
  matriz da 3.3 reprovar, desfazer. Registrar aqui quais testes reprovaram.
  **Guardas:** (A) `FallbackPolicy` só com `RequireAuthenticatedUser()` → 14
  reprovaram: as 12 células proibidas de `SubjectXRota` e
  `OperadorNaRotaExclusivaDoApi_Recebe403`, `ApiNasRotasDoOperador_Recebe403`. (B)
  checagem sem o segundo sentido → `RotaAutenticadaSemDono_ReprovaNomeandoOPadrao`
  reprovou. Os dois desfeitos.

## 4. Contrato de conector e conector falso

- [x] 4.1 [`apps/connectors`] `IFolderNavigator`, `IFolderContentSource`,
  `ConnectorAccount`, os modelos (`FolderEntry`, `FolderDescription`, `RootListing`,
  `RootFile`, `IgnoredFile`) e `ConnectorFailure` (código e detalhe), com
  `ConnectorCodes` validando o formato `^[a-z0-9]+(-[a-z0-9]+)*\z` até 64
  caracteres. `IFolderContentSource` com comentário apontando o consumidor (#105).
- [x] 4.2 [`apps/connectors`] `ConnectorRegistry` (chaves registradas e e-mail de
  cada uma) e `ValidateConnectorRegistrations` sobre a `IServiceCollection`, antes
  do `Build()`: toda chave com um dos três registros tem os outros dois.
- [x] 4.3 [`apps/connectors`] `FakeConnector` em `tests/.../Support`, chave `fake`,
  registrado só pela fábrica de teste.
- [x] 4.4 [`apps/connectors`] Testes da extensão: sem `IFolderContentSource`, sem
  `IFolderNavigator` e sem `ConnectorAccount`, cada um nomeando a chave e o
  registro; registro completo passa; nenhuma chave passa. Teste da composição real
  capturando a `IServiceCollection` pelo `ConfigureServices` da
  `WebApplicationFactory`, com e sem credencial do Google (convenção 8).
- [x] 4.5 [`apps/connectors`] Guarda (convenção 15): remover a chamada de
  `ValidateConnectorRegistrations` do `Program.cs` **e** registrar a chave `google-drive`
  sem `IFolderContentSource`; ver o teste da composição real reprovar e os da
  extensão continuarem verdes; desfazer.
  **Guarda:** sem a chamada no `Program.cs` e com o Google registrado sem
  `IFolderContentSource`, `ComposicaoReal_ComCredencial_TemOsTresRegistrosDoGoogle`
  reprovou e os seis testes da extensão continuaram verdes. Desfeito. **Divergência
  do design, corrigida lá:** `ConnectorAccount` é singleton não keyed com a própria
  chave, porque o contêiner não enumera chaves keyed em tempo de execução e a rota
  de provedores precisa listá-las.

## 5. Google Drive: credencial e token (D2, D3)

- [x] 5.1 [`apps/connectors`] `GoogleDriveOptions` (`GoogleDrive:ServiceAccountKeyBase64`)
  e `GoogleServiceAccountKey`: decodifica, valida `type`, `client_email`,
  `private_key` e a importação do PEM; erros com a verificação que falhou e sem
  trecho do valor; nenhum `ToString` que exponha campo.
- [x] 5.2 [`apps/connectors`] Registro condicional: sem a variável, nenhuma chave
  `google-drive`; com a variável inválida, boot falha; com a variável válida, os
  três registros sob `google-drive`.
- [x] 5.3 [`apps/connectors`] `GoogleServiceAccountTokenSource`: JWT `RS256` com
  `iss`, `scope` (`drive.readonly`), `aud`, `iat`, `exp` de uma hora; troca em
  `https://oauth2.googleapis.com/token`; cache até 5 min antes de `expires_in`;
  `SemaphoreSlim` contra troca concorrente. `HttpClient` nomeado com timeout fixo.
- [x] 5.4 [`apps/connectors`] Testes com `TestServiceAccountKey` (par RSA gerado por
  execução): cada falha de validação da 5.1; JWT com cabeçalho e claims esperados e
  assinatura verificada com a chave pública; duas operações em sequência fazem uma
  troca só; duas concorrentes também; `400 invalid_grant` vira `provider-auth-failed`.

## 6. Google Drive: operações (D4, D5, D7)

- [x] 6.1 [`apps/connectors`] `GoogleDriveClient` com as chamadas `files.get`
  (metadado), `files.list` (todas as páginas), `drives.list`, `files.export` e
  `files.get?alt=media`, sempre com `supportsAllDrives=true` e, na listagem,
  `includeItemsFromAllDrives=true`, sem `corpora`; leitura do `reason` do corpo de
  erro.
- [x] 6.2 [`apps/connectors`] `GoogleDriveErrorMapper` com a tabela da D5,
  `access-denied` com o e-mail da conta como detalhe, e o detalhe de
  `provider-error` só com o `reason` em kebab-case que case com o formato.
- [x] 6.3 [`apps/connectors`] `GoogleDriveFolderNavigator`: `BrowseAsync` sem
  pasta (`drives.list` mais `sharedWithMe`) e com pasta (lê a pasta antes, depois
  subpastas); `DescribeFolderAsync` (`not-a-folder`, `folder-trashed`).
- [x] 6.4 [`apps/connectors`] `GoogleDriveFolderContentSource.ListRootAsync`: lê a
  pasta antes; classificação pela tabela da D4 na ordem atalho, subpasta, tipo,
  `canDownload`; versão externa `modifiedTime` ou `md5Checksum`; listagem completa
  ou falha.
- [x] 6.5 [`apps/connectors`] `GetMarkdownAsync`: exportação `text/markdown` para
  Doc com `GoogleDriveMarkdown` retirando imagens `data:` (referência e inline);
  `alt=media` para `.md`, decodificado como UTF-8, sem transformação.
- [x] 6.6 [`apps/connectors`] Testes contra as fixtures da tarefa 1.1 e 1.2:
  - listagem da P3: Doc e `.md` suportados com a versão certa; planilha
    `unsupported-type` com o `mimeType` no detalhe; subpasta `subfolder-not-synced`;
    atalho `shortcut-not-followed`; Doc com `canDownload=false` `download-blocked`;
    o handler falso afirma que **nenhuma** exportação ou download foi pedido para
    os quatro ignorados, nem chamada ao id do destino do atalho;
  - consulta com `trashed = false` e os parâmetros de Drive Compartilhado, sem
    `corpora`;
  - pasta sem acesso (`404 notFound`): `access-denied` com o e-mail, e nenhuma
    listagem pedida; o par "pasta acessível e vazia" devolve lista vazia sem erro;
  - id de Doc no lugar da pasta: `not-a-folder`; pasta com `trashed: true`:
    `folder-trashed`;
  - segunda página falhando: a operação falha, sem itens da primeira;
  - os quatro `403` lado a lado (`accessNotConfigured`,
    `insufficientFilePermissions`, `userRateLimitExceeded`, `cannotExportFile`)
    viram quatro códigos diferentes; `429`, `401`, `5xx` e timeout;
  - `400 badRequest` vira `provider-error` com detalhe `bad-request`;
  - exportação com imagem (P1): saída sem `data:image`, sem `![][image1]` e sem a
    definição; o resto igual byte a byte ao exportado sem a imagem; imagem inline
    `data:` também retirada; o par "Doc sem imagem" sai idêntico ao exportado;
  - escapes preservados (`gerar\_arquivo\_texto\_1mb`);
  - `.md` baixado igual byte a byte à fixture;
  - navegação do nível de cima com um drive e uma pasta, cada um com o `kind`
    certo; pasta sem subpastas devolve lista vazia.
- [x] 6.7 [`apps/connectors`] Guardas (convenção 15), cada um reintroduzido, visto
  reprovar e desfeito, registrando aqui o teste que reprovou: (a) mapear o erro só
  pelo status; (b) listar sem ler a pasta antes; (c) decidir tipo pela extensão do
  nome; (d) checar `canDownload` antes de reconhecer o atalho; (e) devolver a
  listagem parcial quando uma página falha.
  **Guardas**, cada um desfeito: (a) mapear só pelo status → os quatro
  `MesmoStatus403_*` e quatro `ErroAoLerAPasta(...)` reprovaram; (b) listar sem ler
  a pasta → `PastaSemAcesso_AccessDeniedComOEmail_ENenhumaListagem`,
  `PastaNaLixeira_FolderTrashed`, `IdDeDocNoLugarDaPasta_NotAFolder` e
  `Listagem_FiltraLixeira...` reprovaram; (c) extensão do nome decidindo antes do
  `mimeType` → `RaizDaP3_SuportadosComAVersaoCerta` e
  `RaizDaP3_IgnoradosComOCodigoCerto` (a primeira forma deste guarda, inserida
  depois do teste de atalho, ficou inalcançável e não reprovou nada; não conta); (d)
  `canDownload` antes do atalho → `RaizDaP3_IgnoradosComOCodigoCerto`; (e) listagem
  parcial → `SegundaPaginaFalha_AOperacaoFalhaSemItensDaPrimeira`; (f) sem retirar
  as imagens → `DocComImagemEmEstiloDeReferencia_SaiSemAImagem`.

## 7. Rotas (D9)

- [x] 7.1 [`apps/connectors`] `ConnectorEndpoints` sob `MapGroup("/connectors")`
  com as três rotas, `ConnectorResponses` com nomes de fio fixados
  (`accountEmail`, `webUrl`, `kind` como string), ordenação da D9 e
  `ProblemDetails` com a extensão `code` e o status da tabela da D9.
- [x] 7.2 [`apps/connectors`] Testes pelo texto do JSON, não por desserialização no
  mesmo tipo (convenção 12): chaves `key`, `accountEmail`, `id`, `name`, `kind`,
  `webUrl`; `kind` como `"SharedDrive"`/`"Folder"`; ordenação por nome com desempate
  por id; `400` sem `id`; `404 provider-not-configured`; `422 access-denied` com o
  e-mail em `detail` (e **não** `200` com `[]`); `502 api-not-configured`;
  `503 rate-limited`; lista vazia de provedores sem credencial.

## 8. Testes de aceite transversais

- [x] 8.1 [`apps/connectors`] `SecretLeakTests`: com `CapturingLoggerProvider` em
  todas as categorias, exercitar boot com chave válida e inválida, as três rotas em
  sucesso, `422`, `502`, `503`, troca de token recusada; procurar em logs, corpos de
  resposta e mensagens de exceção um trecho de 40 caracteres do corpo do PEM, o
  `private_key_id`, o valor base64 inteiro e o `access_token` devolvido pelo token
  falso. Nenhum pode aparecer. Asserção de precondição: o teste afirma que capturou
  pelo menos uma linha de log e um corpo de cada status, para não passar vazio.
  **Medido, contra vacuidade:** 43 textos e 5.551 caracteres nas rotas (logs de
  todas as categorias e os corpos de `200`, `422`, `502`, `503`), 10 textos e 1.176
  caracteres com a troca de token recusada, 2 textos e 2.461 caracteres no boot com
  chave inválida que carrega uma chave privada de verdade. Agulhas: trecho de 40
  caracteres do PEM, `private_key_id`, base64 inteiro, trecho de 40 do meio do
  base64 e cada `access_token` emitido pelo falso.
- [x] 8.2 [`apps/connectors`] Guarda (convenção 15): logar o valor da variável numa
  mensagem de erro de validação, ver a 8.1 reprovar, desfazer.
  **Guarda:** o JSON da chave na mensagem de `type` inválido →
  `NenhumaSaidaContemAChave` reprovou com "vazou trecho do corpo do PEM". Desfeito.
- [x] 8.3 [`apps/connectors`] Teste `[Trait("Category", "Manual")]` que só roda com
  `GoogleDrive__ServiceAccountKeyBase64` e `CONNECTORS_MANUAL_FOLDER_ID` no ambiente
  (pulado sem eles), chamando `ListRootAsync` e `GetMarkdownAsync` do conector real.

## 9. Documentação

- [x] 9.1 [documentação, `docs/architecture.md`] "Os cinco apps" com a linha do
  `apps/connectors`; isolamento entre apps citando os cinco; seção "Contrato de
  conector" (dois contratos keyed, conta, checagem no boot, conector Google, falso
  só em teste); a fronteira "canais pertencem ao `apps/inbox`, provedores de
  arquivo ao `apps/connectors`"; "Autenticação" com o terceiro validador e a tabela
  de subjects do `apps/connectors` (sem operador em tudo, `service:api` reservado
  para a #104).
  Edições curtas, longe do parágrafo de autenticação do `apps/inbox` que a #116
  mudou na `main` (a #116 foi mergeada no #123 enquanto esta change rodava; a branch
  não foi atualizada, porque isso pede rebase ou merge autorizado).
- [x] 9.2 [documentação, `docs/configuration.md`] Seção `apps/connectors`
  (`Auth__TokenSigningKey` fail-fast, `GoogleDrive__ServiceAccountKeyBase64`
  opcional e validada no boot, `Cors__AllowedOrigins`); `Auth:TokenSigningKey` com
  três processos na tabela de restrições e na de obrigatórias. Só
  desenvolvimento: a seção "Stack de servidor" e o `.env.prod.example` não mudam
  (#119).
- [x] 9.3 [documentação, `docs/development.md`] Rodar o `apps/connectors` (porta
  5037, como gerar a variável a partir de uma chave de teste), rodar os testes,
  construir a imagem; índice atualizado.
- [x] 9.4 [documentação, `.env.example` da raiz] Bloco do `apps/connectors`,
  comentado, sem valor real.
- [x] 9.5 [documentação, `README.md`, em inglês] Cinco apps.
  A frase "four small services ... deployable on a single VM" virou "a handful of
  small services", porque o `apps/connectors` ainda não está no stack de servidor
  (#119).
- [x] 9.6 [documentação, `CONTRIBUTING.md`] `apps/connectors` na regra "cada tarefa
  indica o app", `dotnet test apps/connectors/Connectors.sln` na verificação, e
  `connectors` nos escopos de commit.
- [x] 9.7 [documentação, `openspec/config.yaml`] Contexto com cinco apps e o
  conector Google Drive.
- [x] 9.8 [documentação, `.github/ISSUE_TEMPLATE/`] `apps/connectors` nas opções de
  app do `bug_report.yml` e do `feature_request.yml`.
- [x] 9.9 [documentação, `01-ARQUITETURA_E_CONVENCOES.md`] Tabela de apps (cinco),
  autenticação (terceiro validador, tabela de subjects do `apps/connectors`) e
  seção de contrato de conector ao lado do de canal.
- [x] 9.10 [documentação, `02-HISTORICO_E_STATUS.md` e `CHANGELOG.md`] Seção da
  change com o que entrou, o que foi medido, guardas, divergências do `design.md` e
  o que ficou de fora; entrada em `[Unreleased]`.

## 10. Verificação final

- [x] 10.1 [`apps/connectors`] `dotnet test apps/connectors/Connectors.sln`, com o
  número de testes **medido** e o `load average` antes e depois, registrado aqui e
  no `02`.
  **Medido (03/10/2026):** 133 aprovados, 1 pulado (o manual da 8.3), 0 falhas, 6 s;
  `load average` 2,68 antes e 2,75 depois, 12 núcleos.
- [x] 10.2 [documentação] `python3 scripts/check-docs.py` sem violação, com
  `apps/connectors` presente em `apps/` e em `docs/architecture.md`. Conferir os
  dois sentidos renomeando temporariamente a menção na arquitetura e vendo o script
  reprovar; desfazer.
  **Medido:** OK. Com `apps/connectors` trocado por `apps/conectores` na
  arquitetura, o script reprovou nos dois sentidos ("existe mas não está
  documentado" e "está documentado mas não existe"). Desfeito.
- [x] 10.3 [`apps/connectors`] `docker build -f apps/connectors/Dockerfile -t
  buteco-connectors .` e o contêiner respondendo `200` em `/health`, com `podman
  build --format docker` se for Podman (o formato OCI ignora o `HEALTHCHECK`).
  **Medido:** `podman build --format docker` concluído; contêiner com só
  `Auth__TokenSigningKey`: `/health` `200`, `/connectors/providers` sem token `401`,
  processo como `app`, nenhum SDK nem `.cs` na imagem, `State.Health` `healthy`.
- [x] 10.4 [`apps/connectors`, manual, convenção 14] Contra o Drive real nas pastas
  de teste da etapa 0, com uma chave real fora do repositório: `GET
  /connectors/providers`; navegação do nível de cima e dentro da pasta principal;
  descrição da pasta principal e de uma pasta não compartilhada (`422
  access-denied`); o teste da 8.3. Resultado no `02`; o que divergir da D7 ou da D4
  corrige o `design.md` (convenção 9).
  **Medido (03/10/2026), somente leitura, app local na porta 18137 com token
  assinado localmente por subject.** (1) provedores: `google-drive` com o e-mail da
  service account. (2) nível de cima: nenhum Drive Compartilhado; as duas pastas de
  teste via `sharedWithMe`, como `Folder`; dentro da principal, só a subpasta. (3)
  descrição das duas pastas por `service:api`: `200` com nome e URL. (4) id de
  arquivo: `422 not-a-folder`; id que a conta não lê: `422 access-denied` com o
  e-mail, na descrição e na navegação, nunca `200` com lista vazia; operador na
  descrição e `service:api` nos provedores: `403`. (5) raiz da principal pelo teste
  manual: atalho `shortcut-not-followed`, planilha `unsupported-type`, subpasta
  `subfolder-not-synced`; nenhum item com download bloqueado no estado atual do
  Drive (a etapa 0 religou a opção); os dois `.md` com MD5 igual ao `md5Checksum` do
  Drive e à origem (o `02` ao repositório em `505ea24`; o `01` à cópia enviada como
  nova versão na P6); Doc com imagem entregue sem `data:` (1.365 B). Pasta pessoal:
  um Doc suportado, exportado. (6) toda exportação rodou sem `supportsAllDrives`.
  **Achado:** exportar o Doc convertido do `02` (849.530 B) levou 16,8 s a 31,1 s, e
  o limite único de 30 s falhou; corrigido com limites por natureza (D2), teste novo
  e guarda (exportação com o limite curto reprova
  `ExportacaoLenta_PassaDoLimiteDeMetadado_SemFalhar`). **Condição do ambiente:**
  nesta máquina o IPv6 até o Google não conecta, e o .NET o tenta primeiro; a
  verificação rodou com `DOTNET_SYSTEM_NET_DISABLEIPV6=1`. A validação manual pelo
  mantenedor continua pendente (convenção 14).
- [x] 10.5 `openspec validate apps-connectors-google-drive --strict`.
  **Medido:** válida, junto com `check-docs.py` OK, depois de todos os ajustes.
