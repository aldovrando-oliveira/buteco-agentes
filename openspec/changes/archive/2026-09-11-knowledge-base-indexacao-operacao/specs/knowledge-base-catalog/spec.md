## Purpose

Cobre o **cadastro da base de conhecimento em `apps/api`**: criar, listar,
consultar por id, editar, ativar e desativar. A base é o *recipiente* — nome,
descrição e estado de ativação —, nunca o conteúdo que mora dentro dela.

A capability existe por uma pergunta de operação, não por CRUD: **o agente deve
consultar esta base quando o cliente perguntar isso?** Duas decisões saem daí e
são o que esta capability afirma de mais próprio:

- **A descrição é obrigatória**, ao contrário da de `McpServer`. Ela não é texto
  decorativo: a partir da etapa de execução é o texto que o modelo lê para
  decidir se a base é relevante para a pergunta. Base sem descrição seria uma
  ferramenta que o modelo não sabe quando chamar.
- **Desativar não é excluir, e não existe rota de exclusão.** Base é entidade de
  catálogo com vínculos de agente apontando para ela; desativar impede o *uso
  pelo agente* e preserva tudo — os documentos ficam intactos, e o histórico de
  quem a consultou continua fazendo sentido. É o mesmo padrão de
  `mcp-server-catalog`, e é o lado oposto de `knowledge-document-catalog`, que
  tem exclusão real porque documento é conteúdo e nada aponta para ele.

O que a distingue das vizinhas, que é onde a confusão nasce:

- `knowledge-document-catalog` é dona do **conteúdo** dentro da base — os
  documentos, o teto de tamanho, o ciclo de vida de indexação de cada um.
- `knowledge-document-indexing` é dona do que **transforma** esse conteúdo em
  fragmentos consultáveis, e do estado agregado de indexação por base. Esta
  capability afirma explicitamente que a resposta de base **não** carrega essa
  contagem: seis sítios a constroem, quatro deles operações de escrita sem
  relação nenhuma com indexação, e encarecer o catálogo inteiro serviria uma
  única tela.
- `agent-knowledge-binding` é dona de **qual agente consulta qual base**. Esta
  capability nunca sabe quem a consome.
- `knowledge-base-catalog-ui` é a contraparte de tela desta, em
  `apps/frontend`.

## ADDED Requirements

### Requirement: A resposta de base não carrega contagem agregada
A resposta de base de conhecimento NÃO SHALL carregar contagem de documentos
nem contagem de estado de indexação — nem em `GET /knowledge-bases`, nem em
`GET /knowledge-bases/{id}`, nem nas respostas de criação, atualização,
ativação e desativação. O estado de indexação agregado vive em recurso próprio,
`GET /knowledge-bases/indexing-summary`.

Isto é requisito, e não detalhe de implementação, porque é exatamente a coisa
que alguém "corrige" acrescentando um campo sem ver a causa. As razões, todas
verificáveis no código:

- A resposta é construída em **seis** lugares, e **quatro** deles são operações
  de escrita — criar, atualizar, ativar e desativar uma base. Nenhuma delas tem
  relação com indexação, e todas passariam a arcar com uma agregação sobre
  documentos para montar a própria resposta.
- A alternativa a essa agregação seria devolver zero nessas quatro, o que seria
  **falso** em atualização, ativação e desativação — a interface afirmando uma
  contagem que ninguém fez.
- `GET /knowledge-bases` tem consumidores que nunca olham contagem alguma,
  inclusive as telas de vínculo entre agente e base. Encarecer o catálogo para
  todos eles serve uma única tela.

O catálogo SHALL, portanto, permanecer uma consulta simples sobre as bases, sem
junção com documentos.

#### Scenario: Listagem de bases não devolve contagem de documentos
- **WHEN** um cliente envia `GET /knowledge-bases` e as bases têm documentos em
  estados variados
- **THEN** a resposta traz apenas os campos da própria base — id, nome,
  descrição, estado de ativação e datas — e nenhum campo de contagem

#### Scenario: Criar uma base não afirma contagem nenhuma
- **WHEN** um cliente cria uma base de conhecimento
- **THEN** a resposta da criação não traz campo de contagem de documentos nem de
  estado de indexação
