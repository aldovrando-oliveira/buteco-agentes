## Context

O detalhe do servidor MCP hoje é um bloco de ações (editar, ativar ou
desativar, testar conexão), um alerta de resultado do teste e um card
único com nome, estado, url, tipo de autenticação, descrição e datas. A
listagem tem quatro colunas: nome, url, autenticação e estado. O diálogo
de desativação tem texto fixo.

A API não tem consulta inversa: não existe `GET /mcp-servers/{id}/agents`.
O que existe é `GET /agents`, em que cada agente traz
`mcpServers: [{ id, name, allowedTools }]`. Toda informação de uso que
esta change exibe sai de percorrer esse catálogo — é derivação no
cliente, e o handoff já registra isso como dívida a ser paga com um
endpoint quando o número de agentes crescer.

O teste de conexão devolve `{ success, failureReason, message }`. O
`failureReason` é um enum de quatro valores e a interface o descarta
hoje. A `message` é montada no servidor concatenando texto de exceção
(por exemplo, "Não foi possível conectar ao servidor MCP: " seguido da
mensagem da exceção), então ela é diagnóstico técnico, não orientação.

A descoberta de tools (`GET /mcp-servers/{id}/tools`) já é usada pela aba
de ferramentas do agente, através de um hook com `staleTime` infinito e
`enabled` controlado — exatamente o formato de que o catálogo no detalhe
precisa.

Importar o catálogo de agentes de dentro da feature de servidores MCP não
inaugura nada: a feature de canais já importa `useAgentsQuery` e o tipo
`Agent` em cinco arquivos.

## Goals / Non-Goals

**Goals:**
- Responder, a partir do servidor, quem o usa e com quais tools.
- Tornar a desativação uma decisão informada, nomeando quem ela afeta.
- Transformar cada motivo de falha de conexão em uma orientação com ação.
- Fazer o teste no formulário testar aquilo que o operador acha que está
  testando, inclusive quando a credencial fica em branco na edição.

**Non-Goals:**
- Nenhuma mudança em `apps/api`, `apps/workers` ou `apps/inbox`, e nenhum
  endpoint novo — inclusive nenhuma consulta inversa no backend, que fica
  como dívida registrada (Decision 1).
- Nenhuma busca, filtro ou paginação nas listagens.
- Nenhum histórico ou saúde de conexão: o resultado do teste continua sem
  ser persistido, e a interface passa a dizer isso (Decision 5).
- Nenhuma edição de vínculo a partir do servidor: a visão inversa é
  leitura, e o vínculo continua sendo editado a partir do agente.
- Nenhuma biblioteca nova.

## Estrutura de pastas proposta

```
apps/frontend/src/features/mcp-servers/
├── api/
│   └── useMcpServers.ts                 # inalterado
├── components/
│   ├── ConnectionTestResultAlert.tsx    # mensagem por motivo + marca temporal
│   ├── McpServerAgentsCard.tsx          # novo — visão inversa
│   ├── McpServerConfigCard.tsx          # renomeia McpServerDetailCard
│   ├── McpServerForm.tsx                # teste unificado + validação de url
│   ├── McpServerTable.tsx               # + coluna "Usado por"
│   └── McpServerToolsCatalog.tsx        # novo — catálogo ocioso + atualizar
├── pages/
│   ├── McpServerDetailPage.tsx          # cabeçalho, grade, diálogo com nomes
│   ├── McpServerEditPage.tsx            # repassa o id do servidor ao form
│   └── McpServerListPage.tsx            # busca o catálogo de agentes
└── utils/
    └── agentUsage.ts                    # novo — derivação pura do uso
```

Cada componente novo ou alterado tem seu arquivo de teste ao lado,
omitido acima por brevidade.

## Decisions

### Decision 1: A derivação de uso vive em um módulo puro, fora dos componentes

`utils/agentUsage.ts` exporta funções sem estado sobre uma lista de
agentes: quem usa um servidor e com quais tools, quantos usam, quantos
estão vinculados sem nenhuma tool, e em quantos agentes uma tool
específica está permitida.

Três telas consomem a mesma derivação — a coluna da listagem, o card de
visão inversa e o diálogo de desativação. Concentrá-la em um módulo puro
evita três implementações da mesma regra e a torna testável sem montar
nenhum componente.

É também o ponto único a trocar quando o backend ganhar a consulta
inversa: as chamadas passam a vir de uma query, e as telas não mudam.

**Alternativa descartada**: derivar dentro de cada componente. Rejeitada
por espalhar a mesma travessia por três lugares, com o risco de as três
divergirem no tratamento do caso "vinculado sem tools", que é justamente
o que esta change existe para tornar visível.

### Decision 2: A feature de servidores MCP importa o catálogo de agentes

As páginas de listagem e de detalhe de servidor chamam `useAgentsQuery()`
e repassam o resultado por propriedade, como toda página do projeto faz.

Isso cria uma dependência da feature de servidores para a de agentes, no
sentido inverso ao que já existe (a aba de ferramentas do agente importa
o hook de tools de servidores). A alternativa seria duplicar a query
dentro da feature de servidores, o que criaria dois caches para o mesmo
recurso e duas chaves diferentes no React Query — pior sob qualquer
ângulo.

Não é precedente novo: a feature de canais já importa `useAgentsQuery` e
o tipo `Agent`.

Consequência aceita: a listagem de servidores passa a fazer duas
requisições em vez de uma. É o mesmo que a listagem de canais já faz, e o
catálogo de agentes tende a ser pequeno.

### Decision 3: A explicação da falha é do cliente; a mensagem da API vira detalhe

O alerta de resultado passa a exibir, para cada `failureReason`, uma
explicação com a ação correspondente:

- `HostUnreachable` — a URL não respondeu; verificar endereço e se o
  servidor está no ar.
- `CredentialRejected` — o servidor respondeu recusando a credencial;
  gerar um novo token e salvar de novo.
- `CredentialDecryptionFailed` — a chave de criptografia do ambiente
  mudou desde que a credencial foi salva; salvar a credencial de novo.
- `Unknown` — falha desconhecida ao conectar.

O motivo também aparece como rótulo técnico, para que o operador possa
citá-lo ao pedir ajuda.

A `message` da API continua sendo exibida, mas como detalhe secundário e
não como a mensagem principal, porque ela embute texto de exceção: é útil
para diagnóstico (distinguir DNS de recusa de conexão) e ruim como
orientação.

**Alternativa descartada**: descartar a `message` e mostrar só a
explicação curada, como no protótipo. Rejeitada porque o operador deste
painel é quem configura os servidores, e o texto da exceção é
frequentemente a informação que resolve o caso.

### Decision 4: O sucesso do teste não afirma quantidade de tools

O protótipo mostra "conexão bem-sucedida, {n} tools disponíveis". A
resposta do teste não traz contagem alguma: são três campos, e nenhum é o
número de tools. Exibir o número exigiria uma segunda requisição, ao
endpoint de tools, disfarçada de resultado do teste.

O sucesso diz apenas que a conexão foi estabelecida. Quem quiser ver as
tools tem, na mesma tela, o catálogo com a ação de atualizar.

### Decision 5: O catálogo de tools começa ocioso e reusa o cache da descoberta

O card de tools do detalhe não dispara nada ao abrir a página: exibe que
as tools são descobertas ao vivo e oferece a ação de atualizar. Depois
disso percorre os mesmos quatro estados que a aba de ferramentas do
agente já tem — carregando, erro com nova tentativa, vazio e lista.

Ele usa o mesmo hook de descoberta, com a mesma chave de cache. Duas
consequências, ambas desejadas: se o operador já descobriu as tools
daquele servidor na aba do agente, o catálogo aparece preenchido sem
nova requisição; e a ação de atualizar refaz a busca para as duas telas.

O resultado do teste de conexão nunca é persistido. A interface passa a
exibir quando ele foi feito, a partir de uma marca temporal em memória, e
a dizer explicitamente que não fica guardado — para que ninguém leia
aquilo como histórico ou monitoramento.

### Decision 6: O teste no formulário escolhe o endpoint pelo estado da credencial

Em edição, com o campo de credencial em branco, o teste passa a chamar o
endpoint do servidor salvo, e o formulário avisa que a credencial
guardada é a que será usada. Em cadastro, ou em edição com credencial
digitada, continua chamando o endpoint que recebe a configuração.

Sem isso, o caso mais comum da edição — mexer na URL sem tocar na
credencial — testa uma configuração sem credencial nenhuma e falha por um
motivo que não é o do servidor salvo, o que é pior do que não ter teste.

O formulário também passa a exigir URL preenchida antes de testar, em vez
de mandar a requisição e esperar o 400 de validação.

### Decision 7: O card de detalhe vira card de configuração e ganha a linha de credencial

O card único do detalhe é reorganizado como card de configuração — url,
autenticação, credencial e datas — e passa a exibir, quando o tipo de
autenticação exige credencial, uma linha que mostra a credencial como
mascarada e cifrada.

A linha existe para responder a uma pergunta que hoje fica no ar: se há
ou não credencial salva. Ela nunca exibe valor, porque a API nunca o
devolve, e dizê-lo na própria linha evita que alguém interprete a máscara
como truncamento.

## Risks / Trade-offs

- [Risco] A derivação percorre todos os agentes e todos os seus vínculos
  a cada render das telas envolvidas → [Mitigação] o custo é linear e o
  catálogo é pequeno; se crescer, o gargalo real será a requisição, não a
  travessia, e a resposta é o endpoint inverso no backend (Decision 1).
- [Risco] Compartilhar a chave de cache da descoberta entre o detalhe do
  servidor e a aba do agente faz a ação de atualizar em uma tela afetar a
  outra → [Mitigação] é o comportamento correto, já que a fonte é a
  mesma; o dado é do servidor, não da tela.
- [Risco] Exibir a mensagem crua da API como detalhe expõe texto de
  exceção ao operador → [Mitigação] o painel é interno e operado por quem
  configura os servidores; a orientação vem antes, em linguagem própria,
  e o texto técnico fica em segundo plano.
- [Trade-off] A listagem de servidores passa a depender de duas
  requisições, e a coluna de uso fica vazia enquanto o catálogo de
  agentes não chega. A alternativa seria não ter a coluna.

## Open Questions

(nenhuma)
