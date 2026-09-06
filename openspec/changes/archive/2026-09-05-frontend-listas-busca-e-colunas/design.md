## Context

A listagem de agentes tem hoje quatro colunas: nome com link, provider,
model e estado, esta última acumulando o badge de ativo ou inativo e o de
"precisa de reconfiguração". A listagem de servidores MCP tem cinco, e
acabou de ganhar a coluna de uso na change anterior. Nenhuma das duas tem
busca, filtro, contagem ou subtítulo, e as duas tratam lista vazia com uma
única mensagem.

Tudo que as colunas novas precisam já vem na resposta de `GET /agents`:
cada agente traz `description`, `mcpServers` com `allowedTools` por
servidor, e `delegatesTo` com os agentes-alvo. Não há query nova nesta
change.

O que não existe é busca no backend. Nem `GET /agents` nem
`GET /mcp-servers` aceitam filtro, ordenação ou paginação: as duas
devolvem a coleção inteira. Isso decide a arquitetura desta change e é
dívida registrada na proposta.

## Goals / Non-Goals

**Goals:**
- Permitir achar um agente ou um servidor pelo nome, sem rolar a lista.
- Mostrar, na própria lista de agentes, o que leva alguém a abrir um
  agente: descrição, ferramentas vinculadas, delegações.
- Tornar visível na lista o estado "servidor vinculado sem nenhuma tool",
  que as changes anteriores tornaram visível no detalhe.
- Dizer ao operador quando a lista está vazia porque não há nada
  cadastrado, e quando está vazia porque a busca não encontrou nada.

**Non-Goals:**
- Nenhuma mudança em `apps/api`, `apps/workers` ou `apps/inbox`, e
  nenhuma busca, ordenação ou paginação no backend.
- Nenhum bloco de primeiros passos e nenhuma preferência de densidade,
  ambos adiados por decisão explícita.
- Nenhuma ordenação por coluna, nenhuma seleção múltipla, nenhuma ação em
  massa.
- Nenhuma persistência do que foi buscado ou filtrado entre visitas.
- Nenhuma biblioteca nova.

## Estrutura de pastas proposta

```
apps/frontend/src/
├── utils/
│   └── searchText.ts                     # novo — normalização para busca
└── features/
    ├── agents/
    │   ├── components/AgentTable.tsx     # colunas novas
    │   └── pages/AgentListPage.tsx       # busca, filtro, subtítulo, vazios
    └── mcp-servers/
        ├── components/McpServerTable.tsx # inalterado
        └── pages/McpServerListPage.tsx   # busca, subtítulo, vazios
```

Cada arquivo novo ou alterado tem seu teste ao lado, omitido acima por
brevidade.

## Decisions

### Decision 1: Busca e filtro no cliente, sobre a coleção inteira

As duas listagens filtram em memória o array que a query já devolveu. Não
há requisição nova a cada tecla, nem estado de carregamento durante a
busca.

É a única opção disponível: a API não tem parâmetro de busca. Também é a
opção certa para o tamanho atual do catálogo, em que a lista inteira já
cabe numa resposta e o filtro é instantâneo.

O ponto em que isso deixa de servir é claro e vale registrar: quando a
resposta de listagem ficar pesada o bastante para incomodar, a resposta é
paginação e busca no backend, e a interface passa a mandar o termo em vez
de filtrar. Nada do que esta change constrói impede essa migração — o
componente de tabela continua recebendo uma lista pronta.

**Alternativa descartada**: paginar no cliente para adiar o problema.
Rejeitada porque paginar uma coleção que já foi inteiramente carregada
não resolve nada e ainda esconde do operador o tamanho real do catálogo.

### Decision 2: A comparação de busca ignora acentuação

Um utilitário compartilhado normaliza os dois lados da comparação:
minúsculas e remoção de sinais diacríticos, decompondo o texto e
descartando as marcas de acento.

Sem isso, procurar por "cobranca" não encontraria "Cobrança", e é
exatamente assim que se digita depressa num painel em português. O custo é
uma função de duas linhas usada nos dois lados da comparação.

O utilitário fica fora das features, em `utils/`, porque as duas
listagens o usam e ele não sabe nada de agentes nem de servidores.

### Decision 3: O que foi buscado ou filtrado vive em estado local, não na URL

A busca e o filtro são estado de componente, e não parâmetros de rota.

A aba do detalhe do agente foi para a URL porque é um lugar dentro da
página, que o operador quer compartilhar e retomar. Um termo de busca em
digitação é o oposto: mudaria a rota a cada tecla, enchendo o histórico
de navegação de entradas inúteis, e o ganho de compartilhar "a lista
filtrada por 'cob'" é imaginário.

**Alternativa descartada**: refletir busca e filtro na URL com
substituição de entrada de histórico. Evitaria o lixo no histórico, mas
paga complexidade para tornar linkável algo que ninguém linka.

### Decision 4: Coluna de ferramentas resume o vínculo e denuncia o vínculo vazio

A coluna nova mostra quantos servidores estão vinculados e quantas tools
estão permitidas no total e, quando existe, um aviso de quantos desses
servidores estão vinculados sem nenhuma tool.

O aviso é o mesmo estado que a aba de ferramentas e o detalhe do servidor
já denunciam — um servidor vinculado com lista de tools vazia não oferece
ferramenta nenhuma ao modelo em runtime. Trazê-lo para a listagem fecha o
ciclo: dá para varrer o catálogo inteiro e ver de uma vez quais agentes
estão nessa situação.

O cálculo sai de `agent.mcpServers`, que a listagem já recebe, e vive na
própria tabela: é uma soma sobre a lista de vínculos daquele agente, com
um consumidor só. Diferente da derivação de uso da change anterior, que
virou módulo próprio por ter três consumidores.

### Decision 5: O subtítulo diz a contagem, não os canais atendidos

Cada listagem ganha um subtítulo com quantos itens existem e uma frase
curta sobre o que aquilo é.

O protótipo de design sugere, na listagem de agentes, dizer que eles
atendem mensagens de Telegram e WhatsApp. Ficou de fora: a lista de
canais suportados é dado do sistema, muda quando um adaptador novo entra,
e escrevê-la à mão numa tela que não consulta canais garante que ela
estará errada em algum momento. O subtítulo diz o que é verdade sem
consulta nenhuma.

### Decision 6: Lista vazia por busca é uma mensagem diferente de catálogo vazio

Quando não há nada cadastrado, a interface continua dizendo isso e
mantendo visível a ação de cadastrar. Quando há itens mas nenhum
corresponde ao que foi buscado ou filtrado, a mensagem passa a ser outra,
que fala da busca.

São dois problemas diferentes com duas saídas diferentes — cadastrar algo
ou limpar a busca — e a mesma frase para os dois manda o operador para o
lado errado.

## Risks / Trade-offs

- [Risco] Filtrar no cliente dá a impressão de que a busca é do sistema,
  e ela para de encontrar o que estiver além da resposta atual se um dia
  a listagem for paginada no backend → [Mitigação] hoje não há paginação
  alguma, então a lista é sempre completa; a dívida está registrada na
  proposta e no design.
- [Risco] A coluna de agente passa a mostrar descrição, e descrições
  longas podem quebrar o layout da tabela → [Mitigação] a descrição é
  truncada com reticências na própria célula.
- [Trade-off] Busca e filtro não sobrevivem a um recarregamento nem são
  compartilháveis por link, consequência aceita da Decision 3.

## Open Questions

(nenhuma)
