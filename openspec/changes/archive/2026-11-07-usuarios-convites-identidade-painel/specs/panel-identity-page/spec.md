## ADDED Requirements

### Requirement: Página Identidade em /identity
`apps/frontend` SHALL prover a página `/identity` dentro da casca, com a mesma exigência de sessão das outras rotas autenticadas. A página SHALL ter o título "Identidade" e o subtítulo "O Buteco emite os tokens de usuários e módulos, e é com estes dados que outros sistemas conferem se um token é legítimo.".

Com o `GET /identity` em `200`, a página SHALL mostrar os cartões "Emissor do Buteco" e "Chaves de assinatura" do quadro `Provedores` (`entrega=m1`), nesta ordem. SHALL NOT mostrar o cartão "Provedores federados" (M3).

Enquanto o `GET /identity` não responde, a página SHALL mostrar um indicador de carregamento.

#### Scenario: Sem sessão, /identity vai ao login
- **WHEN** alguém sem sessão abre `/identity`
- **THEN** o painel vai a `/entrar` com `voltarPara=/identity`

#### Scenario: Os dois cartões, sem provedores federados
- **WHEN** o `GET /identity` responde `200`
- **THEN** a página mostra "Emissor do Buteco" e "Chaves de assinatura", nesta ordem, e não mostra "Provedores federados"

### Requirement: Cartão Emissor do Buteco
Com o `GET /identity` em `200`, o cartão SHALL ter o selo "Operando" na faixa e SHALL mostrar estas linhas:

| linha | conteúdo |
|---|---|
| Issuer | `emissor.issuer` |
| Discovery | `emissor.discovery` |
| Endpoint de token | `emissor.token` |
| Chaves públicas (JWKS) | `emissor.jwks` |
| Validade do token | "{validadeDoTokenSegundos} para usuários e módulos" |
| Sessão do painel | "Renovada enquanto houver uso; termina depois de {sessaoSemAtividadeSegundos} sem atividade" |

- Os quatro endereços SHALL aparecer em fonte monoespaçada, exatamente como vieram.
- Os valores em segundos SHALL ser escritos pelo formato de durações desta capacidade.

#### Scenario: O emissor com os valores de hoje
- **WHEN** o `GET /identity` traz `issuer: "https://buteco.exemplo.test/"`, `validadeDoTokenSegundos: 900` e `sessaoSemAtividadeSegundos: 1209600`
- **THEN** o cartão mostra o selo "Operando", "https://buteco.exemplo.test/", "15 min para usuários e módulos" e "Renovada enquanto houver uso; termina depois de 14 dias sem atividade"

#### Scenario: A validade segue a rota
- **WHEN** o `GET /identity` traz `validadeDoTokenSegundos: 600`
- **THEN** o cartão mostra "10 min para usuários e módulos"

### Requirement: Cartão Chaves de assinatura
O cartão SHALL ter na faixa "Troca automática a cada {intervaloDeTrocaSegundos}". O texto explicativo SHALL ser o do quadro, com as durações saídas da rota:
- a próxima chave "aparece {prePublicacaoSegundos} antes de começar a assinar";
- a anterior "continua publicada por {retencaoSegundos} depois de parar";
- "A troca acontece em até {verificacaoSegundos} depois da data marcada.".

A tabela SHALL ter as colunas "Identificador (kid)", "Estado", "Criada em" e "Próximo passo". As linhas SHALL vir na ordem de `chavesDeAssinatura.chaves`, sem reordenação.

| `estado` | selo | Próximo passo |
|---|---|---|
| `em-uso` | "Em uso" (verde) | "Assina desde {assinaDesde}; é substituída em {assinaAte}" |
| `proxima` | "Próxima" (azul) | "Já publicada; passa a assinar em {assinaDesde}" |
| `aposentando` | "Aposentando" (cinza) | "Não assina desde {assinaAte}; sai da publicação em {saiDaPublicacaoEm}" |
| qualquer outro valor | neutro, com o valor cru | "—" |

- O `kid` SHALL aparecer em fonte monoespaçada.
- "Criada em" e as datas do Próximo passo SHALL sair como `dd/mm/aaaa`, no fuso do navegador.
- O selo de um estado desconhecido SHALL NOT usar a cor de "Em uso" (convenção 13).

#### Scenario: Depois da troca
- **WHEN** as chaves são `[em-uso, aposentando]`, com a em uso `assinaDesde` 20/10/2026 e `assinaAte` 18/01/2027, e a aposentando `assinaAte` 20/10/2026 e `saiDaPublicacaoEm` 04/11/2026
- **THEN** a tabela mostra "Em uso" com "Assina desde 20/10/2026; é substituída em 18/01/2027" e, abaixo, "Aposentando" com "Não assina desde 20/10/2026; sai da publicação em 04/11/2026"

#### Scenario: Antes da troca
- **WHEN** as chaves são `[em-uso, proxima]`, com a próxima `assinaDesde` 18/01/2027
- **THEN** a segunda linha mostra "Próxima" e "Já publicada; passa a assinar em 18/01/2027"

#### Scenario: Só a chave em uso
- **WHEN** as chaves são só `[em-uso]`
- **THEN** a tabela tem uma linha

#### Scenario: A ordem é a da resposta
- **WHEN** as chaves vêm como `[aposentando, em-uso]`
- **THEN** a primeira linha é a "Aposentando"

#### Scenario: Estado desconhecido
- **WHEN** uma chave vem com `estado: "revogada"`
- **THEN** o selo mostra "revogada" no tom neutro, e o Próximo passo mostra "—"

#### Scenario: As durações da política
- **WHEN** a rota traz `intervaloDeTrocaSegundos: 7776000`, `prePublicacaoSegundos: 604800`, `retencaoSegundos: 1296000` e `verificacaoSegundos: 3600`
- **THEN** o cartão mostra "Troca automática a cada 90 dias", "7 dias antes", "por 15 dias depois" e "em até 1 h"

#### Scenario: A troca segue a rota
- **WHEN** a rota traz `intervaloDeTrocaSegundos: 2592000`
- **THEN** o cartão mostra "Troca automática a cada 30 dias"

### Requirement: Formato das durações
As durações em segundos SHALL ser escritas na maior unidade que as divide exatamente:
- dias: "1 dia" e "N dias";
- horas: "N h";
- minutos: "N min";
- segundos: "N s", quando nenhuma das anteriores divide.

A página SHALL NOT escrever nenhuma duração à mão: todas saem dos campos da rota.

#### Scenario: Unidades exatas
- **WHEN** o formato recebe 86400, 3600, 900 e 90
- **THEN** devolve "1 dia", "1 h", "15 min" e "90 s"

#### Scenario: Sem unidade arredondada
- **WHEN** o formato recebe 5400 (uma hora e meia)
- **THEN** devolve "90 min"

### Requirement: Identidade indisponível e nova tentativa automática
Quando o `GET /identity` falha (`503`, rede ou outra falha), depois das tentativas do QueryClient do painel, a página SHALL mostrar o cartão "Emissor do Buteco" (`role="alert"`) com:
- o selo amarelo "Indisponível";
- o texto "Não foi possível carregar os dados do emissor agora. O painel tenta de novo sozinho; se continuar, confira se o api está no ar.";
- o botão "Tentar de novo".

A página SHALL NOT mostrar o selo "Operando" nem o cartão das chaves nesse estado.

Enquanto houver erro, a página SHALL repetir o `GET /identity` sozinha:
- no `Retry-After` do `503`, quando ele é legível, preso entre 1 e 30 s;
- senão, a cada 30 s.

A repetição SHALL parar quando o `GET` responder `200` e quando a pessoa sair da página. O intervalo SHALL vir de uma função pura exportada, que devolve `false` quando há dado sem erro (convenção 20).

"Tentar de novo" SHALL chamar o `GET /identity` na hora.

#### Scenario: Indisponível
- **WHEN** o `GET /identity` responde `503`
- **THEN** a página mostra "Indisponível", o texto do quadro e "Tentar de novo", e não mostra "Operando"

#### Scenario: Tenta de novo sozinha e para no 200
- **WHEN** o `GET /identity` falha, e 30 s depois responde `200`
- **THEN** a página chama o `GET /identity` de novo sem nenhuma ação da pessoa, e mostra os dois cartões
- **AND** depois do `200`, nenhuma chamada nova acontece com o passar do tempo

#### Scenario: Usa o Retry-After do 503
- **WHEN** a função do intervalo recebe uma consulta em erro `503` com `retryAfter: 5`
- **THEN** devolve 5000

#### Scenario: Sem erro, sem intervalo
- **WHEN** a função do intervalo recebe uma consulta com dado e sem erro
- **THEN** devolve `false`
