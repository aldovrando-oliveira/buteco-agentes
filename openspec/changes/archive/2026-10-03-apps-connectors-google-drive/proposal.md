**Issue:** #103

## Why

A linha de bases sincronizadas precisa de alguém que converse com o Google Drive
(e, depois, com outros provedores) sem pôr SDK de provedor e credencial dentro do
`apps/api`, que continua sendo o único dono da escrita de documentos (#102). O
operador também precisa ver as pastas que a conta de serviço enxerga para escolher a
pasta de uma base (#106), e o `apps/api` precisa de quem valide essa pasta (#104).
A etapa 0 (#100) mediu o Drive e decidiu a forma do conector: exportação com imagem
embutida, tipos por `mimeType`, atalhos, bloqueio de download e erros distinguidos
pelo `reason`.

## What Changes

- **App novo `apps/connectors`, sem banco:** projeto `Buteco.Connectors`,
  `Connectors.sln`, imagem `buteco-connectors`, `/health` anônimo e classificado,
  checagem de rotas anônimas no boot, CORS para o frontend.
- **Contrato de conector** com quatro operações, em dois contratos registrados por
  chave no DI: navegar e descrever o local (consumidos pelas rotas desta change);
  listar os arquivos da raiz, com o motivo de cada ignorado, e entregar o markdown
  de um arquivo (consumidos pelo ciclo da #105). Checagem de integridade no boot:
  conector com contrato faltando derruba o processo.
- **Conector Google Drive**, por chamadas REST com a service account, sem
  biblioteca do Google:
  - suporte decidido pelo `mimeType`, nunca pela extensão;
  - Google Doc exportado como `text/markdown`, com as imagens embutidas retiradas;
  - `.md` baixado direto, sem transformação;
  - atalho, subpasta, tipo não suportado e arquivo com `canDownload=false`
    listados como ignorados, cada um com o seu código, sem exportar nem resolver
    destino;
  - erros do Google distinguidos pelo `reason` e mapeados para códigos no formato
    da D1 da #102; pasta sem acesso responde erro, nunca lista vazia;
  - parâmetros de Drive Compartilhado em toda chamada.
- **Conector falso** para os testes, registrado só pela fábrica de teste.
- **Credencial:** uma service account por implantação, com a chave em base64 numa
  variável de ambiente. Só o e-mail da conta aparece em resposta; a chave nunca
  aparece em resposta, log ou erro.
- **Rotas sob `/connectors`:** provedores configurados, com o e-mail da conta, e
  navegação de pastas (operador); descrição de uma pasta (`apps/api`, para a #104).
- **Autorização por tabela explícita de subjects desde o início:** `operator` só
  nas rotas do operador, `service:api` só na descrição de pasta, qualquer outro
  subject `403`. A lista de rotas por subject é conferida no boot, nos dois
  sentidos.
- **Documentação** passa a descrever cinco apps (`architecture.md`,
  `configuration.md`, `development.md`, `README.md`, `CONTRIBUTING.md`, contexto
  do OpenSpec, `01`, `02`, `CHANGELOG.md`), com a fronteira de que canais
  pertencem ao `apps/inbox`; `apps/connectors` entra nas opções de app dos
  templates de issue.
- **`Dockerfile` e imagem `buteco-connectors`**; a implantação em produção não
  entra (D10 do `design.md`, #119).

Fora desta change: criação de base sincronizada no `apps/api` (#104), ciclo de
sincronização e chamadas ao `/sync` (#105), qualquer tela (#106, #107), chave de
assinatura por serviço (#117), restrição de subject no `apps/inbox` (#116),
`VITE_CONNECTORS_BASE_URL` (entra com o primeiro consumidor, #106; D8), e a
**implantação em produção** (#119): compose de produção, `.env.prod.example`, nginx
do stack, `deployment.md` e verificação pós-deploy. Ela foi adiada por decisão do
mantenedor porque o próximo deploy subiria um terceiro processo com a chave de
assinatura, que é o gatilho da #117, sem nenhum consumidor em produção antes da
#104 e da #106.

## Capabilities

### New Capabilities

- `connectors-scaffold`: o app `apps/connectors` — projeto, solução, imagem,
  configuração, saúde, autenticação por token e classificação de rotas anônimas.
- `connector-plugin`: o contrato de conector (dois contratos keyed, quatro
  operações), o registro por chave e a checagem de integridade no boot.
- `google-drive-connector`: o comportamento do conector Google Drive — tipos,
  exportação, ignorados, erros por `reason`, credencial.
- `connectors-api`: as rotas HTTP do `apps/connectors`, o formato de fio e a
  autorização por tabela de subjects.

### Modified Capabilities

- `repository-documentation`: README, arquitetura e contexto do OpenSpec passam
  de quatro para cinco apps.
- `server-deployment`: Dockerfile por app passa a incluir `apps/connectors`.
  Compose de produção, nginx e segredos compartilhados ficam para a #119.

## Impact

- **Código novo:** `apps/connectors/` inteiro (`src/Buteco.Connectors`,
  `tests/Buteco.Connectors.Tests`, `Connectors.sln`, `Dockerfile`).
- **Nenhum código de outro app muda.** O `apps/api` passa a ter um subject
  reservado (`service:api`) no `apps/connectors`, mas só vai assiná-lo na #104.
- **Dependências:** nenhum pacote NuGet novo em produção (D2). Os testes usam os
  pacotes que o `apps/inbox` já usa.
- **Configuração:** `Auth__TokenSigningKey` passa a ser lida por três processos;
  `GoogleDrive__ServiceAccountKeyBase64` nova, opcional; `Cors__AllowedOrigins`.
  Documentadas para desenvolvimento.
- **Deploy:** nenhum. Esta change só entrega o `Dockerfile` e a imagem; o stack de
  produção não muda (#119). Os pré-requisitos medidos na etapa 0 (Drive API
  ativada no projeto da service account, pasta compartilhada como Leitor, arquivo
  com download permitido para leitores) vão para o `deployment.md` na #119.
- **Segurança:** o app guarda a chave de assinatura de token e a chave da service
  account, e fala com API externa (#117).
