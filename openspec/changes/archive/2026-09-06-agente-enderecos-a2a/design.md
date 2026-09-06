## Context

O protocolo A2A já está entregue. Cada agente tem dois endereços públicos, os
dois derivados da mesma configuração de URL pública do servidor:

```
{urlPublica}/agents/{id}/a2a                        execução, JSON-RPC
{urlPublica}/agents/{id}/.well-known/agent-card.json descoberta, anônimo
```

O painel não expõe nenhum dos dois. O handoff de design tinha um card para eles
e o removeu do protótipo justamente porque o backend não os expunha, deixando a
nota de como deveriam voltar — e o aviso de não montar a URL no navegador por
concatenação de host com id.

Dois comportamentos já especificados são o que dá conteúdo ao card:

- o card de descoberta é servido **independente do estado operacional** do
  agente: inativo ou sem provedor configurado, responde `200 OK`
  (`a2a-agent-card`);
- o endpoint de execução **rejeita** trabalho quando o agente está inativo.

Ou seja: um agente inativo continua descobrível e deixa de ser útil. É
exatamente a distinção que um operador não tem como adivinhar, e que o card
existe para dizer.

A configuração de URL pública hoje tem valor padrão vazio e nenhuma validação.
Com ela ausente, o card de descoberta já emite endereços quebrados, em silêncio.

## Goals / Non-Goals

**Goals:**
- Tornar os dois endereços visíveis no painel, prontos para copiar.
- Manter a montagem do endereço no servidor, onde a configuração vive.
- Dar comportamento definido, e visível, ao caso de URL pública não
  configurada.
- Dizer no painel a diferença entre estar descobrível e aceitar trabalho.

**Non-Goals:**
- Mudar o formato do card de descoberta, o comportamento do endpoint de
  execução ou a autenticação de qualquer um dos dois.
- Validar a URL pública no startup, derrubando o processo. Isso mudaria o
  comportamento de inicialização de um sistema em uso, e é decisão maior que
  esta change.
- Expor os endereços na listagem como coluna. Eles chegam ali por virem da mesma
  resposta, mas nenhuma tela de listagem os usa.

## Decisions

### D1 — O servidor monta os endereços; o navegador só exibe

O handoff é explícito, e a razão é boa: a URL pública é configuração do
servidor. O navegador conhece o host de onde a página foi servida, que num
sistema atrás de proxy, em container ou com domínio próprio não é o host público
da API. Concatenar host com id no frontend produziria endereço certo em
desenvolvimento e errado em produção — o pior tipo de erro.

A montagem reusa a mesma configuração e a mesma forma que o card de descoberta
já usa, para que os dois nunca divirjam.

### D2 — Sem URL pública configurada, o bloco vem ausente

A configuração tem valor padrão vazio. Hoje, nesse caso, o card de descoberta
emite `/agents/{id}/a2a` como se fosse endereço absoluto — quebrado, e em
silêncio.

A resposta de agente **não** vai repetir esse comportamento. Sem URL pública, o
bloco de endereços vem ausente, e o painel diz que o endereço público do
servidor não está configurado.

Trocar um endereço quebrado por uma ausência declarada é o que transforma uma
configuração faltando em informação, em vez de em suporte. É a mesma regra que
as etapas anteriores do redesenho aplicaram várias vezes: não afirmar o que não
se sabe.

*Alternativa descartada:* devolver o caminho relativo e deixar o navegador
resolver. Volta a montar endereço no cliente, com o problema do D1 disfarçado.

*Alternativa descartada:* validar no startup e recusar subir sem a
configuração. Resolve de verdade, mas muda o comportamento de inicialização de
um sistema já em uso — é decisão própria, e fica registrada como candidata.

### D3 — Sem campo de "habilitado"

O handoff sugeriu a forma `{ url, agentCardUrl, enabled }`. O campo `enabled`
não entra.

A2A não é opcional neste sistema: todo agente tem os dois endereços, sempre. O
que varia é o agente aceitar trabalho, e isso já é o `isActive` que a resposta
carrega desde sempre. Um segundo campo que precisa concordar com o primeiro é um
campo que um dia vai discordar.

O painel deriva o aviso do estado que já existe.

### D4 — O card diz a diferença entre descobrível e útil

Um agente inativo responde ao card de descoberta e rejeita mensagens. As duas
coisas são verdade ao mesmo tempo, e nenhuma delas é óbvia.

O card mostra os endereços sempre — eles não deixam de existir — e, quando o
agente está inativo, avisa que ele continua descobrível mas rejeita o que
receber. Sem isso, o operador conclui do endereço visível que o agente está
atendendo.

### D5 — Adição compatível na resposta, sem forma alternativa

Os campos entram na resposta de agente que já existe, que serve a listagem e o
detalhe. Nenhum campo existente muda.

Os endereços chegam também à listagem, onde nenhuma tela os usa. É desperdício
pequeno e conhecido; a alternativa seria duas formas de resposta para o mesmo
recurso, com o custo de manter as duas em sincronia para economizar dois campos.

### D6 — Testes nos dois lados

No servidor: que os endereços aparecem na consulta por id e na listagem, que
casam com os que o card de descoberta anuncia, e que o bloco vem ausente sem URL
pública configurada.

No painel: que o card exibe os dois endereços, que o aviso de inativo aparece
apenas para agente inativo, e que a ausência de configuração vira mensagem em
vez de endereço vazio.

### D7 — O campo pode chegar ausente, não só nulo (descoberto na conferência)

O card quebrou a tela inteira com `Cannot read properties of undefined`. A causa
é a janela de migração que o próprio Migration Plan desta design descreve: uma
API que ainda não subiu com a mudança **omite** o campo, e ele chega como
ausente, não como nulo. O componente comparava com nulo e seguia adiante.

O tipo do painel passa a declarar o campo como opcional, e não só anulável, e o
componente testa a ausência em vez da igualdade com nulo. O caso ganhou teste,
verificado contra a versão que quebrava.

*Por que não estava previsto:* a design descreveu a ordem de implantação e a
tratou como propriedade da operação, não do código. Quem precisa tolerar a
janela é o componente.

### D8 — A instabilidade da suíte era prazo, não lógica (fechada nesta change)

A instabilidade registrada desde a etapa da casca do painel passou a falhar em
metade das execuções e virou obstáculo. Diagnosticada: os testes de formulário
abrem dropdowns que montam em portal depois de uma transição, enquanto
cinquenta e quatro arquivos rodam em paralelo. O prazo padrão de um segundo das
consultas assíncronas do Testing Library acaba antes de a opção existir.

Não era lógica de teste errada — os helpers já usavam consulta assíncrona. Era
prazo apertado. O prazo das consultas sobe para cinco segundos e o do teste
para quinze, que precisa ser maior para um teste que espera por um dropdown não
ser cortado pelo próprio limite.

Entra aqui, e não em change própria, porque estava impedindo verificar esta.

### D9 — O nome do campo no fio é fixado, não derivado (descoberto na conferência)

O card apareceu com a mensagem de configuração ausente mesmo com a url pública
configurada, a API reconstruída e o ambiente certo. O card de descoberta, que usa
o mesmo ponto de montagem, anunciava a url absoluta corretamente — ou seja, o
servidor estava certo.

A causa é o nome da chave. A política camelCase do `System.Text.Json` minúscula
apenas a primeira letra, então a propriedade `A2A` vai para o fio como **`a2A`**,
e o painel lê `a2a`. Campo ausente, e a mensagem de ausência aparece exatamente
como projetada — para o problema errado.

A propriedade passa a declarar o nome no fio explicitamente.

**Os quatro testes de contrato da API não pegaram isso, e não podiam.** Eles
desserializam a resposta para o mesmo record, então a chave passa pela mesma
política na ida e na volta e sempre casa. O teste que pega é o que inspeciona o
JSON como texto, e é ele que entrou.

É o mesmo padrão dos guardas da etapa anterior: um teste só vale depois de ter
falhado contra o defeito real. Este foi escrito antes da correção, visto falhar,
e só então a correção entrou.

### Árvore de arquivos

```
apps/api/
├── src/Buteco.Api/
│   ├── Agents/Responses/
│   │   ├── AgentResponse.cs          (M) + bloco de endereços A2A (D1, D5)
│   │   └── AgentA2AResponse.cs       (N) os dois endereços
│   ├── Agents/Queries/               (M) repasse da URL pública aos handlers
│   └── A2A/AgentCardEndpoints.cs     (M) montagem extraída para reuso (D1)
└── tests/Buteco.Api.Tests/
    └── AgentEndpointsTests.cs        (M) contrato dos endereços (D6)

apps/frontend/src/
├── test/setup.ts                     (M) prazo das consultas assíncronas (D8)
├── vite.config.ts                    (M) prazo do teste (D8)
└── features/agents/
├── types/agent.ts                    (M) + os dois endereços, opcionais
├── components/
│   ├── AgentA2ACard.tsx              (N) quarto card da coluna direita (D4)
│   ├── AgentA2ACard.test.tsx         (N)
│   └── AgentOverviewTab.tsx          (M) monta o card novo
└── (fixtures de teste)               (M) campos novos
```

Nenhuma mudança em `apps/workers`. Nenhuma referência de projeto entre apps.

## Risks / Trade-offs

**A montagem do endereço pode divergir da do card de descoberta** — são dois
lugares construindo a mesma URL. → *Mitigação:* a montagem é extraída para um
ponto único que os dois usam, e um teste afirma que o endereço anunciado na
resposta de agente é igual ao que o card de descoberta publica.

**A URL pública continua sem validação no startup** — um sistema mal configurado
sobe, e agora mostra a ausência em vez de um endereço quebrado. → *Mitigação:*
é melhoria sobre o comportamento atual, não regressão. A validação no startup
fica registrada como candidata a change própria, por mudar o comportamento de
inicialização.

**O painel passa a exibir endereço de execução, que é superfície de ataque
conhecida** — → *Mitigação:* o endereço já é público por construção: o card de
descoberta é anônimo e anuncia o mesmo endereço para qualquer um que peça.
Exibi-lo a um operador autenticado não revela nada que já não estivesse
publicado. O esquema de segurança do endpoint continua o que a capability do
card declara.

## Migration Plan

Duas implantações independentes, e a ordem importa pouco: a API primeiro faz o
painel receber campos que ainda não usa; o painel primeiro faz o card exibir a
mensagem de configuração ausente até a API subir. Nenhuma das duas quebra.

Nenhuma migração de dados. Nenhum contrato removido ou renomeado.

**Rollback:** reverter o commit. A resposta volta a não carregar os endereços e
o card some. Nada persistido depende da mudança.

## Open Questions

Nenhuma. A forma da resposta, o comportamento sem URL pública e a ausência do
campo de habilitado estão decididos acima, com a razão de cada um.
