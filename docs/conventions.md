# Convenções e premissas

As regras que governam como código é escrito, testado e organizado neste
repositório. Elas não estão codificadas em lugar nenhum — são o padrão que se
formou ao longo do projeto, e vale conferir contra elas antes de propor
qualquer mudança.

Para a arquitetura e o modelo de domínio, ver
[architecture.md](architecture.md). Para o processo de contribuição, ver
[CONTRIBUTING.md](../CONTRIBUTING.md).

---

## Índice

- [Estrutura do repositório](#estrutura-do-repositório)
- [Isolamento entre apps e a régua de `libs/`](#isolamento-entre-apps-e-a-régua-de-libs)
- [Gerenciamento de pacotes](#gerenciamento-de-pacotes)
- [Sequenciamento de trabalho](#sequenciamento-de-trabalho)
- [Sem abstração prematura](#sem-abstração-prematura)
- [Credenciais e segredos](#credenciais-e-segredos)
- [Degradação graciosa](#degradação-graciosa)
- [Checagem de integridade no startup](#checagem-de-integridade-no-startup)
- [Contratos entre apps](#contratos-entre-apps)
- [Testes](#testes)
- [Frontend](#frontend)
- [Documentação](#documentação)

---

## Estrutura do repositório

```
docker-compose.yml          # Postgres + RabbitMQ + WAHA para desenvolvimento
docker-compose.prod.yml     # stack completo de servidor
global.json                 # fixa a versão do SDK do .NET
Directory.Build.props       # propriedades comuns aos projetos .NET
Directory.Packages.props    # Central Package Management
libs/
  ProviderCatalog/          # identidade de cada provedor de LLM
  ProviderCatalog.Tests/
apps/
  api/                      # ASP.NET Core Minimal API
  workers/                  # .NET Worker Service
  inbox/                    # ASP.NET Core Minimal API, banco próprio
  frontend/                 # Vite + React + TypeScript + Mantine
tests/
  CrossAppTaskStoreCompatibility.Tests/   # acordo de schema api ↔ workers
  InboxOrchestratorRoundTrip.Tests/       # round-trip inbox → api → workers
deploy/
  migrate/                  # migration bundle one-shot
docs/                       # esta documentação
openspec/                   # propostas, specs, design e tasks de cada mudança
scripts/                    # utilitários de verificação
```

Cada app .NET tem sua própria solution (`Api.sln`, `Workers.sln`,
`Inbox.sln`), com `src/` e `tests/` dentro da pasta do app.

---

## Isolamento entre apps e a régua de `libs/`

Nenhum `.csproj` ou arquivo do frontend pode referenciar código de outro app
diretamente. Referências entre domínios de apps diferentes são sempre
validadas via HTTP autenticado, nunca por FK.

**`libs/` só existe quando houver necessidade real de compartilhamento**, com
justificativa explícita no `design.md` da mudança que a criar. Hoje há um
único caso: `libs/ProviderCatalog`, referenciada por `apps/api` e
`apps/workers` (nunca um pelo outro), carregando só a identidade de cada
provedor de LLM e o nome da seção de configuração — o mínimo que os dois
processos precisam concordar, não um mecanismo geral de código compartilhado.

Os dois projetos em `tests/` são exceções de natureza diferente: referenciam
múltiplos apps de propósito, só para verificar acordos que nenhum app
sozinho consegue verificar. Nenhum app referencia esses projetos de volta,
nem eles são publicados junto.

---

## Gerenciamento de pacotes

**Central Package Management.** As versões de pacotes NuGet ficam em
`Directory.Packages.props`, na raiz; os `.csproj` referenciam pacotes **sem**
o atributo `Version`.

Antes de fixar qualquer versão de runtime, linguagem, framework ou biblioteca
como decisão, verifique a versão estável e ainda suportada na data — nunca
assuma a partir de memória.

---

## Sequenciamento de trabalho

**Catálogo/cadastro → vínculo → execução → UI**, sempre nessa ordem, em toda
linha de trabalho.

Corolário: se a etapa de UI descobrir que precisa de um dado que o backend
não serve, isso é **achado a reportar e sequenciar** — nunca backend feito de
improviso dentro da mudança de tela.

---

## Sem abstração prematura

Espere dois ou três consumidores reais antes de extrair código compartilhado.

O mesmo vale para configuração: uma opção só existe quando há cenário real de
alguém precisar de outro valor. O TTL do token de operador é configurável
(política de produto, varia por ambiente); o TTL do token de serviço não é
(nunca sai do processo).

O mesmo raciocínio decide **jsonb versus tabela relacional**: jsonb quando
não há necessidade de FK ou de consulta relacional; tabela quando os dois
lados do vínculo são entidades com identidade própria.

No frontend a régua é a mesma, e o gatilho é repetição **já observada**,
nunca prevista. Os componentes visuais compartilhados do painel saíram de
cópias idênticas contadas antes de extrair.

---

## Credenciais e segredos

- Sempre **write-only**: nunca retornadas em nenhuma resposta.
- Criptografadas com **AES-GCM**, com chave por domínio via variável de
  ambiente.
- Padrão "deixe em branco para manter a atual" na edição.
- Segredo compartilhado entre apps (como a chave de assinatura de token) vai
  por configuração com o mesmo valor nos dois, **nunca** por chamada entre
  processos.

---

## Degradação graciosa

Falha de dependência externa — MCP, alvo de delegação, entrega de webhook,
envio ao canal — **nunca derruba a task principal**. É logada, não propagada.

Quando a falha precisa ser visível para o operador, ela vira **estado
persistido além do log**: a degradação continua graciosa, só deixa de ser
invisível.

Todo `BackgroundService` deste repositório captura suas próprias falhas
recuperáveis **por unidade de trabalho**, e nunca depende de
`HostOptions.BackgroundServiceExceptionBehavior` para isso. `Ignore` não
reinicia o serviço após a primeira exceção — fica "vivo mas morto", pior que
o crash que evita — e é política de host, não de dependência específica.

> **O defeito que essa degradação evita já apareceu três vezes com a mesma
> forma**: um `try/catch` correto existe, mas a chamada que mais
> realisticamente falha (consulta ao banco, decifragem de credencial) está
> posicionada **fora** dele, então a exceção escapa antes de chegar à
> proteção. Ao revisar qualquer `try/catch` de degradação graciosa, confira
> se **todas** as chamadas capazes de falhar antes do resultado esperado
> estão dentro dele — não só a que motivou o `catch` originalmente. Não é
> uma checagem restrita a `BackgroundService`: um handler de endpoint HTTP
> comum já teve exatamente o mesmo defeito.

---

## Checagem de integridade no startup

**É o padrão para todo registro que possa ficar incompleto em silêncio — não
documentação em prosa.**

Já aplicado a quatro casos de natureza diferente:

| Caso | Checagem | App |
|---|---|---|
| DI keyed para ponto de extensão tipo-plugin | `ValidateChannelAdapterRegistrations` | `apps/inbox` |
| Classificação de rotas anônimas | `RouteAuthenticationExtensions.ValidateRouteAuthenticationClassification` | `apps/api`, `apps/inbox` |
| Fuso horário do sistema | `ValidateTimeZoneConfiguration` | `apps/workers` |
| Extratores de conteúdo por `SourceType` | checagem bidirecional no startup | `apps/api` |

Cada uma é chamada no fim do `Program.cs` correspondente e derruba o boot se
a coerência não se sustentar.

A checagem vale nos **dois sentidos** quando houver lista esperada e
realidade mapeada: item declarado sem contraparte real é tão problema quanto
o inverso.

### As duas formas do padrão, e por que a escolha importa

| Forma | Roda sobre | Quando usar |
|---|---|---|
| **Host construído** | `IHost` / `WebApplication`, depois do `Build()` | o caso geral — derruba o boot de verdade |
| **`IServiceCollection`** | antes do `Build()` | obrigatória para inspecionar **descritores de DI keyed**, porque é ali que as chaves registradas são enumeráveis |

O custo da forma `IServiceCollection` é que testar a checagem diretamente
verifica **a extensão**, não o caminho de boot: mover ou remover a chamada do
`Program.cs` deixa esses testes verdes e a aplicação sobe com registro
divergente.

Quem usar essa forma paga **um teste a mais**, que captura a
`IServiceCollection` real pelo `ConfigureServices` da `WebApplicationFactory`
(que roda depois de todos os registros do `Program.cs`) e afirma a coerência
sobre a composição de produção. É barato — sem container, sub-segundo — e é o
único que pega o caso "chamada removida **e** registro divergente".

---

## Contratos entre apps

**Contrato entre apps inclui o formato de fio, não só os campos.** Nome, tipo
e forma de serialização (enum como string, nunca ordinal) fazem parte do
requisito.

Defeito de formato pertence a quem **expõe**, e se corrige lá, em mudança
própria sequenciada antes — não se contorna no consumidor.

Vale também para as **opções de serialização** em si (encoder de escaping,
naming policy, tratamento de null), não só a representação de um campo:
dois pontos que serializam o mesmo tipo com opções diferentes produzem
payloads estruturalmente diferentes mesmo com os campos certos.

> **A cláusula de naming policy morde de forma sutil.** A política camelCase
> minúscula apenas a primeira letra, então uma propriedade `A2A` vai para o
> fio como `a2A`, e o consumidor que lê `a2a` recebe campo ausente. **Um
> teste que desserializa a resposta para o mesmo tipo é cego a isso** — a
> chave passa pela mesma política na ida e na volta e sempre casa. O teste
> que pega inspeciona o **texto** do JSON. Sempre que um nome de propriedade
> tiver sigla, número ou maiúsculas consecutivas, fixe o nome no fio
> explicitamente.

**Nunca confie em SDK de terceiro de memória.** Decompile (`ilspycmd`) ou
busque documentação real antes de codificar contra qualquer comportamento não
verificado. Vale também para nomes de header, campos de payload e rotas de
sistemas externos citados dentro de uma spec: requisito errado sobrevive ao
archive.

---

## Testes

**Testes de integração com infraestrutura real** — Testcontainers com
Postgres e RabbitMQ, não mocks, para os caminhos principais. Fakes apenas
para dependências HTTP externas.

Toda mudança de comportamento pede asserção explícita — nunca "processado
corretamente" genérico — e todo caso "com item" ganha o par "sem item" ou
vazio testado.

### Teste de acordo entre dois lados

Usa o **artefato real produzido por um lado contra o outro**, nunca um
artefato forjado no teste com a mesma configuração dos dois lados. Fixture
montado pelo próprio teste passa igual com o comportamento certo e com o
errado, e por isso não prova nada.

Na prática: token emitido pela outra API de verdade, JSON bruto lido da
resposta real em vez de round-trip pelo mesmo tipo, e — quando o ponto é
provar independência — a outra ponta deliberadamente inalcançável durante o
teste.

### Um guarda só vale depois de ter falhado contra o defeito real

Escreva o teste, **reintroduza o defeito de propósito**, veja reprovar, e só
então mantenha a correção.

Guardas desta base já passaram verde **com o defeito presente** antes de
serem consertados. Guarda não verificado é pior que nenhum, porque dá
impressão de cobertura sem ter.

### Todo risco nomeado tem contraparte verificável

Um risco listado na seção de Risks de um `design.md` precisa de cenário na
spec e teste, ou de justificativa explícita de por que não é testável. Risco
listado e não coberto é o padrão de falha mais caro desta base, porque parece
cuidado sem ser.

---

## Frontend

- **Página busca dados e repassa como prop.** Componente apresentacional
  nunca importa hook de query ou mutation diretamente.
- **Cada feature mantém seu próprio `request<T>` / `ApiError` fino** — sem
  cliente HTTP compartilhado entre features. Preocupação transversal (token)
  entra como módulo fino que cada `request<T>` importa, não como cliente
  comum.
- **Feature se organiza por conceito de domínio, não por origem do dado.**
  Uma rota `/channels/{id}/algo` pode pertencer a outra feature que não
  `channels`.

### A UI nunca afirma mais do que o sistema sabe

Se o dado não é coletado, a interface não o insinua. Um único indicador de
envio, jamais dois checks, porque entrega e leitura são recibos que o desenho
escolhido não coleta. Valor de enum desconhecido renderiza indicador neutro,
nunca reaproveita o indicador de sucesso.

A asserção que protege isso é a **negativa** — afirmar a ausência do segundo
indicador — porque é ela que impede a regressão bem-intencionada de "deixar
parecido com o WhatsApp".

### Mudança visual só é verificada por olho humano

A suíte roda em jsdom, que não enxerga cor, contraste nem layout. Uma mudança
de tema ou de composição pode deixar a suíte inteira verde e o painel
ilegível.

Mudança que mexe em aparência traz **conferência manual como tarefa
própria**, tela a tela, nos dois esquemas de cor. E a conferência é
**iterativa**, porque cada correção muda o que fica visível.

O que a suíte pode cobrir é contrato: que o token vale o que a spec diz, que
o componente recebe o que promete, que o link aponta para a rota certa.

### Papel visual que troca de ponta da escala precisa de variável por esquema

`gray[n]` é claro nos dois esquemas e `dark[n]` é escuro nos dois, então um
tom fixo usado como fundo de superfície funciona num tema e quebra no outro.
A causa é conceitual: no tema claro a superfície sutil é *mais clara* que o
card; no escuro, *mais escura*.

A saída é uma variável declarada nos dois esquemas pelo resolver, ou um token
da biblioteca que já troque sozinho.

---

## Documentação

### Fronteira de idioma

A divisão é por **camada**, não por arquivo:

| Camada | Idioma | Arquivos |
|---|---|---|
| Vitrine e legal | inglês | `README.md`, `LICENSE`, `NOTICE`, `SECURITY.md`, `CODE_OF_CONDUCT.md` |
| Trabalho real | pt-BR | `docs/**`, `CONTRIBUTING.md`, `CHANGELOG.md` |

`CONTRIBUTING.md` e `CHANGELOG.md` ficam em pt-BR apesar de estarem na raiz
porque ambos são derivados de material em português — as mudanças
arquivadas, o fluxo OpenSpec, os documentos internos. Traduzi-los criaria uma
segunda fonte de verdade em outro idioma.

O `README.md` declara essa fronteira explicitamente, para que ninguém
descubra o idioma clicando num link.

### Link para mudança arquivada usa o caminho de `archive/`

Uma mudança arquivada muda de caminho: `openspec/changes/<nome>/` vira
`openspec/changes/archive/AAAA-MM-DD-<nome>/`. Todo link escrito durante a
mudança quebra no arquivamento.

**Sempre escreva o link já com o caminho de `archive/`** quando a mudança
estiver arquivada. Esta é a causa mecânica mais comum de link quebrado neste
repositório.

### Integridade é verificada por script

Manter a documentação correta não depende só de disciplina. O script
[`scripts/check-docs.py`](../scripts/check-docs.py) verifica as invariantes e
falha identificando arquivo e problema:

- todo link relativo em `.md` resolve para um caminho existente;
- nenhum link aponta para `openspec/changes/<nome>/` quando a mudança está
  arquivada;
- todo diretório em `apps/` aparece em `docs/architecture.md`, e todo app
  descrito lá existe em `apps/` (checagem nos dois sentidos);
- `CHANGELOG.md` contém a seção `[Unreleased]`.

A varredura cobre a **documentação viva**: raiz, `docs/`, `.github/` e
mudanças ativas. Ficam de fora os dois documentos internos do mantenedor e
`openspec/changes/archive/`, porque mudança arquivada é registro histórico
congelado — corrigir um link dentro de uma delas falsificaria o que foi
escrito naquele momento. Violação irreparável mantida no relatório treina
qualquer pessoa a ignorar o relatório inteiro.

Rode-o antes de abrir um pull request — ver [CONTRIBUTING.md](../CONTRIBUTING.md).

### Documentos internos do mantenedor

`01-ARQUITETURA_E_CONVENCOES.md` e `02-HISTORICO_E_STATUS.md`, na raiz, são
notas de trabalho do mantenedor: memória de decisões, histórico de mudanças e
itens em aberto. São **fonte** para esta documentação, nunca destino — o
fluxo é em sentido único, e `docs/` descreve o estado atual sem narrar
histórico.

### Quando a implementação diverge do design

Toda vez que um achado técnico durante a implementação muda uma decisão, o
`design.md` é corrigido para refletir a causa real — nunca fica registrado
apenas no resumo da conversa.
