## ADDED Requirements

### Requirement: Quem chama sem senha confirma com o código do app
Em `POST /applications/` e em `POST /applications/{id}/secrets`, a conferência da senha de quem chama SHALL ser a confirmação de quem chama de `user-access-links`: o campo `senha` para quem tem senha gravada, e o campo `codigo` (o código do app autenticador, nunca o de recuperação) para quem não tem. O lugar da conferência na ordem de cada rota, o `423` do bloqueio e a contagem de falhas SHALL continuar os mesmos. A recusa de quem não tem senha SHALL ser `401` "Código inválido.".

#### Scenario: Admin sem senha cria a aplicação com o código
- **WHEN** um Admin sem senha, com a verificação ligada, cria a aplicação "Portal do Parceiro" com `{ nome, descricao, codigo }` e um código válido do app
- **THEN** a resposta é `201`, como na criação com a senha certa

#### Scenario: Admin com senha não gera segredo com o código
- **WHEN** um Admin com senha gera um segredo com um `codigo` válido e sem `senha`
- **THEN** a resposta é `401` "Senha inválida.", e nenhum segredo é gerado
