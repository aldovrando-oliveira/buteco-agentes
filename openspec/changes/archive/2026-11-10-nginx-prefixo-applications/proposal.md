**Issue:** #268 (defeito de entrega da M2; envolvidos: #186, PR #263, as rotas; #188, PR #267, a tela). Acompanhamento: #195, fechada; a issue entra na milestone da M2 sem reabrir o acompanhamento.

## Why

Na homologação da M2, a lista de Aplicações externas não carrega: o `GET /applications/` devolve o `index.html`
do painel (`200`, `text/html`) em vez do JSON do `apps/api`. A regex do bloco do `apps/api` em
`apps/frontend/deploy/nginx.conf` não tem `applications`. A #186 criou o grupo `/applications`
(`ExternalApplicationEndpoints.GroupPath`) sem acrescentar o prefixo, e a requisição cai no fallback do SPA, o
caso que o comentário do topo do arquivo descreve ("200 com HTML, a resposta errada com o status certo").

A spec `server-deployment` já exige a cobertura de todo prefixo servido, mas o único guarda é um `grep` manual
citado no comentário do topo. Nada na suíte reprova uma rota nova sem prefixo, e o defeito só apareceu na
homologação. O dono corrigiu o contêiner da homologação à mão (`sed`), e essa correção some na próxima imagem.

## What Changes

**`apps/frontend/deploy/nginx.conf`:**
- `applications` entra na regex do bloco do `apps/api`, que já tem o desvio de navegação (`/applications`
  também é rota de página do painel, desde a #188).
- O comentário do topo passa a citar o teste de cobertura como o guarda, no lugar do "Para reconferir". O
  `grep` e o parágrafo do ruído conhecido continuam, como explicação de onde os prefixos vêm.

**O guarda que faltou, um teste de cobertura por app** (`apps/api`, `apps/inbox`, `apps/connectors`):
- deriva os prefixos do host construído do próprio app (a `EndpointDataSource` da fixture de cada suíte, como a
  checagem da `RoleRoutes` já faz) e os compara com os blocos do `nginx.conf` que encaminham para ele, lido pelo
  caminho do repositório, nos dois sentidos;
- as exceções ficam nomeadas no teste, cada uma com o motivo: `/health` nos três; `/sync` no `apps/api`; os
  quatro blocos exatos do emissor OIDC no `apps/api`;
- reprova também: exceção que deixou de valer, bloco exato fora das exceções, bloco de regex sem o desvio de
  navegação, prefixo repetido em dois blocos e bloco que encaminha para um upstream que nenhum dos três
  testes conhece;
- o leitor do `nginx.conf` é um arquivo só, `tests/Shared/NginxRouting.cs`, compilado nos três projetos de teste por
  `<Compile Include … Link>`, como o `tests/ApiConnectorsRoundTrip.Tests` já faz com arquivos de teste.

**Spec `server-deployment`:** o requisito "Nginx do stack roteia por prefixo" ganha as exceções nomeadas
(`/sync` e os endpoints do emissor, além do `/health`), o teste como forma da conferência e os cenários de
`/applications`.

**Documentação:** `CHANGELOG.md`, `02-HISTORICO_E_STATUS.md` e a nota de deploy em `docs/deployment.md` (só a
imagem do `frontend` muda; a correção manual da homologação é substituída pela imagem nova).

**Fora:** nenhum código de produção dos três apps muda. As rotas, as respostas, os papéis e as telas ficam
como estão. O `apps/workers` não tem rota HTTP. Os acordos de `tests/` não passam pelo nginx.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `server-deployment`: o requisito "Nginx do stack roteia por prefixo, sem expor porta pública diretamente",
  com as exceções nomeadas, o teste por app como forma da conferência e os cenários de `/applications`.

## Impact

- **Arquivos:** `apps/frontend/deploy/nginx.conf`; `tests/Shared/NginxRouting.cs` (novo); um
  `NginxRouteCoverageTests.cs` em cada um dos três projetos de teste, e a linha do `<Compile Include … Link>` em
  cada `.csproj`.
- **Deploy:** só a imagem do `frontend`. Pode ir sozinha, a qualquer momento depois do PR #267: o `apps/api` já
  serve `/applications`.
- **Suítes:** `api`, `inbox`, `connectors` (o teste novo) e `frontend` (o `nginxHeaders.test.ts` lê o mesmo
  arquivo).
