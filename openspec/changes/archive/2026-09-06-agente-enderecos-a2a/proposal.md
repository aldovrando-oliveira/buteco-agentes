## Why

O painel não diz em lugar nenhum como um sistema externo alcança um agente. Os
dois endereços existem e funcionam desde a entrega do A2A — o endpoint JSON-RPC
por agente e o card de descoberta — mas quem opera o painel não tem como
descobri-los sem ler código ou documentação.

O handoff de design tinha um card "Protocolo A2A" no detalhe do agente e o
**removeu do protótipo**, com a nota de que o backend ainda não expunha essa
informação e de que ela deveria voltar quando expusesse. A nota é explícita
sobre o ponto que importa: *"não montar a URL no frontend por concatenação de
host + id"*. O endereço público é configuração do servidor, não conhecimento do
navegador.

Esta é a última etapa do redesenho do painel, e a única que precisa do backend.

## What Changes

- A resposta de agente da API passa a carregar os dois endereços A2A do agente:
  o endpoint de execução e o card de descoberta.
- Os endereços são **montados no servidor**, a partir da URL pública já
  configurada e já usada para montar o card. O frontend recebe endereço pronto e
  não concatena nada.
- Quando a URL pública não está configurada, a resposta **não inventa
  endereço**: o bloco vem ausente, e o painel diz que o endereço público do
  servidor não está configurado. Hoje esse caso produz silenciosamente um card
  de descoberta com endereço quebrado.
- O detalhe do agente ganha um quarto card na coluna da direita, com os dois
  endereços, ação de copiar em cada um, link para abrir o card de descoberta, e
  o aviso de que um agente inativo continua descobrível mas rejeita as mensagens
  que receber.

Fora de escopo: alterar o formato do card de descoberta, o comportamento do
endpoint de execução, ou a autenticação de qualquer um dos dois. Esta change só
expõe o que já existe.

## Capabilities

### New Capabilities
Nenhuma. A mudança acrescenta a duas capabilities existentes.

### Modified Capabilities
- `agent-catalog`: os requisitos de listagem e de consulta por id descrevem o
  que a resposta de agente carrega. Ganham os endereços A2A, com a regra de que
  eles são montados no servidor e ausentes quando a URL pública não está
  configurada.
- `agent-catalog-ui`: o requisito de detalhe do agente ganha o card de
  endereços A2A, com cópia, link para o card de descoberta, e a distinção entre
  estar descobrível e aceitar trabalho.

## Impact

Afeta `apps/api` e `apps/frontend`. Nenhuma mudança em `apps/workers`.

- A resposta de agente ganha campos; nenhum campo existente muda de nome, tipo
  ou significado. É adição compatível: quem já consome a resposta continua
  funcionando.
- Os endereços aparecem também na listagem, por virem da mesma resposta. Não há
  tela que os use ali, e omiti-los exigiria duas formas de resposta para o mesmo
  recurso.
- A URL pública passa a ter um comportamento definido quando ausente, em vez de
  produzir endereço quebrado em silêncio.
- Os testes de contrato da resposta de agente e os testes do detalhe no painel
  são estendidos.
