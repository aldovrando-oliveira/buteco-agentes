## MODIFIED Requirements

### Requirement: Página Identidade em /identity
`apps/frontend` SHALL prover a página `/identity` dentro da casca, com a mesma exigência de sessão das outras rotas autenticadas. A página SHALL ter o título "Identidade" e o subtítulo "O Buteco emite os tokens de usuários, aplicações e módulos. Provedores federados são opcionais: sem nenhum, o painel funciona só com usuários locais.".

Com o `GET /identity` em `200`, a página SHALL mostrar os cartões "Emissor do Buteco" e "Chaves de assinatura" do quadro `Provedores` (`entrega=m3`), nesta ordem. Abaixo deles, e independente do `GET /identity`, SHALL mostrar o cartão "Provedores federados" (requisito "Cartão Provedores federados").

Enquanto o `GET /identity` não responde, a página SHALL mostrar um indicador de carregamento.

#### Scenario: Sem sessão, /identity vai ao login
- **WHEN** alguém sem sessão abre `/identity`
- **THEN** o painel vai a `/entrar` com `voltarPara=/identity`

#### Scenario: Os dois cartões, sem provedores federados
- **WHEN** o `GET /identity` responde `200`, e `GET /identity/federated-providers` responde `{ provedores: [] }`
- **THEN** a página mostra o subtítulo da M3, "Emissor do Buteco" e "Chaves de assinatura", nesta ordem, e o cartão "Provedores federados" com "Nenhum provedor federado configurado. O login fica só com e-mail e senha."

## ADDED Requirements

### Requirement: Cartão Provedores federados
O cartão "Provedores federados" do quadro `Provedores` (`entrega=m3`) SHALL ler `GET /identity/federated-providers` numa consulta própria, com "Definidos na configuração do servidor" no cabeçalho, e SHALL ser só leitura: SHALL NOT ter "Adicionar provedor", "Editar" nem a coluna "Chaves". A tabela, um provedor por linha, na ordem da resposta:

| coluna | o valor |
|---|---|
| Provedor | o `nome` e, abaixo, a `chave` em fonte monoespaçada |
| Usado para | `"login"` → o selo "Login de usuários"; outro valor, cru e neutro |
| Issuer | `issuer`, em mono |
| ID do cliente | `clientId`, em mono |
| URI de retorno | `uriDeRetorno`, em mono |
| Vínculos | "1 usuário" quando `vinculos` é 1; "`<n>` usuários" em qualquer outro valor, inclusive 0 |

Os estados:
- carregando: "Carregando os provedores…" (`role="status"`);
- `provedores: []`: "Nenhum provedor federado configurado. O login fica só com e-mail e senha.";
- erro (exceto o `403`, que leva a página ao acesso negado, `panel-roles`): "Não foi possível carregar os provedores federados agora." com "Tentar de novo" (`role="alert"`), sem nova tentativa automática.

O erro de uma consulta SHALL NOT esconder o cartão da outra: o `GET /identity` em erro mantém o cartão dos provedores, e o `GET /identity/federated-providers` em erro mantém o emissor e as chaves.

O rodapé SHALL ser "Quem entra pelo Google precisa estar ligado a um usuário do Buteco, pelo aceite de um convite ou conectando a conta em Minha conta. O Google confirma a identidade; o Buteco emite a sessão e pede o próprio código da verificação em 2 etapas. Vínculos é o número de usuários com uma conta conectada a este provedor.".

#### Scenario: A lista
- **WHEN** a rota traz `{ chave: "corporativo", tipo: "google", nome: "Google", issuer: "https://accounts.google.com/", usadoPara: ["login"], clientId: "412893650127-k3v9.apps.googleusercontent.com", uriDeRetorno: "https://buteco.exemplo.test/auth/federated/callback", vinculos: 3 }`
- **THEN** a linha mostra "Google" e "corporativo", "Login de usuários", o issuer, o ID do cliente, a URI de retorno e "3 usuários"

#### Scenario: Um vínculo no singular
- **WHEN** `vinculos` é 1
- **THEN** a coluna Vínculos mostra "1 usuário"

#### Scenario: Nenhum vínculo
- **WHEN** `vinculos` é 0
- **THEN** a coluna Vínculos mostra "0 usuários"

#### Scenario: Uso desconhecido sai cru
- **WHEN** `usadoPara` é `["aplicacoes"]`
- **THEN** a coluna mostra "aplicacoes", neutro

#### Scenario: Carregando os provedores
- **WHEN** a rota ainda não respondeu
- **THEN** o cartão mostra "Carregando os provedores…"

#### Scenario: Erro com Tentar de novo
- **WHEN** a rota falha por rede
- **THEN** o cartão mostra "Não foi possível carregar os provedores federados agora." e "Tentar de novo"
- **AND** acionar "Tentar de novo" chama a rota de novo, e a tabela aparece quando ela responde `200`

#### Scenario: O erro dos provedores não derruba o emissor
- **WHEN** `GET /identity` responde `200`, e `GET /identity/federated-providers` falha por rede
- **THEN** a página mostra "Emissor do Buteco" e "Chaves de assinatura" com os dados, e o cartão dos provedores com o erro

#### Scenario: O erro do emissor não derruba os provedores
- **WHEN** `GET /identity` falha, e `GET /identity/federated-providers` responde `200` com um provedor
- **THEN** a página mostra o aviso do emissor e a tabela dos provedores

#### Scenario: Só leitura
- **WHEN** o cartão mostra a lista
- **THEN** não há "Adicionar provedor", "Editar" nem a coluna "Chaves"
