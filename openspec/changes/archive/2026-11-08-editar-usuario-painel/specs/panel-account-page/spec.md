## MODIFIED Requirements

### Requirement: Cartão Dados
O cartão "Dados" SHALL vir do `GET /account`, e não do id token, e SHALL mostrar, só para leitura:
- "Nome": `nome`;
- "E-mail": `email`, em fonte monoespaçada;
- "No painel desde": `criadoEm`, como `dd/mm/aaaa`;
- "Último acesso": `ultimoAcesso`, pelo formato relativo de datas desta capacidade.

Abaixo, SHALL mostrar o texto "Para mudar o nome ou o e-mail, use Usuários.", em que "Usuários" é um link para o detalhe do próprio usuário, `/users/{id}`, com o `id` do `GET /account`. É a forma do quadro `MinhaConta`. Nenhum campo do cartão SHALL ser editável.

#### Scenario: Os dados da conta
- **WHEN** `GET /account` responde `{ nome: "Ana Souza", email: "ana@exemplo.test", criadoEm: "2026-08-12T…", ultimoAcesso: <hoje às 14:15> }`
- **THEN** o cartão mostra "Ana Souza", "ana@exemplo.test", "12/08/2026" e "hoje, 14:15"

#### Scenario: "Usuários" leva ao detalhe do próprio usuário
- **WHEN** `GET /account` responde com `id: "u-ana"`
- **THEN** o texto "Para mudar o nome ou o e-mail, use Usuários." aparece, e "Usuários" é um link para `/users/u-ana`, e não para `/users`
