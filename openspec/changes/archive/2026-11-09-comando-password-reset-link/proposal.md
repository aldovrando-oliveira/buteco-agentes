**Issue:** #257 (M2 · 2 de 6; depende da #220, mergeada em `ebf7b2d`, e da #184, mergeada em `a00966a`).

## Why

O último Admin de uma instalação que perde a senha não tem saída. O link de redefinição só sai de `POST /users/{id}/password-reset`, que exige um Admin (`CurrentUser.FindAdminAsync`, `apps/api/src/Buteco.Api/Users/AccessLinks/PasswordResetEndpoints.cs` l. 51) e recusa a si mesmo com `403` "Use Minha conta para trocar a própria senha." (l. 61–64). Com a #184, só o Admin gera o link, e numa instalação com um Admin só, ninguém o gera para ele. A #220 fechou o mesmo buraco do lado das 2 etapas com o comando `two-factor-reset`; esta change faz o irmão, do lado da senha.

**Premissas do corpo da issue** (convenção 9): nenhuma muda comportamento ou contrato. Duas são inexatas e ficam registradas no design (j): o "SQL à mão" é viável, só arriscado; e "o link sai só na saída do comando, sem o token no log" trata como dois canais o que, no contêiner, é um só (stdout e stderr vão os dois para o driver de log do Docker).

## What Changes

**`apps/api`**
- **Comando de emergência** `password-reset-link --email <e-mail>` no binário do `apps/api`, no ramo de `args` do `Program.cs` da #220, antes do host web: host mínimo, sem Kestrel, sem rotas, sem serviços em segundo plano, sem migrations. Em produção:
  `docker compose -f docker-compose.prod.yml --env-file .env.prod run --rm --no-deps api password-reset-link --email <e-mail>`.
- **Imprime o link de 24 h** (`<base do painel>/redefinir-senha#<token>`) uma vez, sozinho numa linha da saída padrão. A linha `Warning` com `EventId` próprio (`1197`) vai para a saída de erro e não traz o token, o link nem o hash. Sem serviço novo no compose.
- **Extração `PasswordResetLink`**: a geração do link sai de `PasswordResetEndpoints.CreateAsync` e passa a ser a mesma implementação para a rota e para o comando. O efeito não muda: o link anterior deixa de valer, a senha, o carimbo de segurança e as sessões continuam como estão.
- **Quem o comando atende:** usuário ativo com papel Admin (Q2, decisão do dono). Convidado, desativado e quem não é Admin saem com código próprio, sem efeito. Diferente do `two-factor-reset`, que aceita qualquer papel: cada comando sozinho não dá acesso, mas no roteiro do último Admin os dois rodam juntos e o link vira credencial completa por 24 h.
- **Códigos de saída:** `0` gerado, `1` sem usuário, `2` desativado, `3` convidado, `4` não é Admin, `64` uso errado, `70` falha de infraestrutura, `78` url do painel não configurada. Uma exceção nunca sai com código de resultado.
- **Sem mudança de contrato da rota:** `POST /users/{id}/password-reset` responde igual. A url do painel passa a ser resolvida antes da escrita também na rota, sem efeito visível, porque o boot já exige o issuer.

**`docs/deployment.md`:** o comando, o cuidado com o link (não colar em chat, ticket nem e-mail; conferir o driver de log; com driver persistente, usar o link logo; sem uso imediato, rodar o comando de novo quando for), o destaque de que, no roteiro do último Admin, o link é credencial completa por 24 h, e o roteiro do último Admin que perdeu a senha e as 2 etapas, com os dois comandos na ordem.

**Sem tela:** o painel não muda.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `user-access-links`: entra o requisito "Comando de emergência para gerar o link de redefinição de senha", e o requisito "Gerar o link de redefinição de senha" passa a dizer que a rota e o comando usam a mesma implementação.

## Impact

- **Contrato:** nenhum. A rota responde igual; o comando é novo e não tem rota HTTP.
- **Sem migration:** as colunas do link são da #181.
- **Dependências:** nenhum pacote novo.
- **Suítes:** só `apps/api` (justificativa no design, "Suítes").
- **Apps:** `apps/api` (código e testes) e `docs/deployment.md`.
