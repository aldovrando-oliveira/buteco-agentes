## ADDED Requirements

### Requirement: Aparência padrão do badge declarada no tema
O sistema SHALL declarar no tema a aparência padrão do badge — a variante
clara, que combina fundo e texto da mesma cor semântica, e a preservação da
caixa do texto como escrito.

Nenhuma tela SHALL precisar declarar variante ou caixa para obter essa
aparência, e todo badge do painel SHALL apresentá-la, independentemente da tela
em que aparece.

A variante preenchida com texto branco SHALL NOT ser usada nas cores semântica
de sucesso e de aviso, por não atingir o contraste mínimo exigido pelo
requisito de contraste desta capability.

#### Scenario: Um badge tem a mesma aparência em qualquer tela
- **WHEN** o operador percorre telas diferentes que exibem badges — listagens,
  detalhes e histórico de mensagens
- **THEN** todos os badges apresentam a variante clara, sem que nenhuma tela
  declare variante

#### Scenario: O texto do badge preserva a caixa como escrito
- **WHEN** um badge exibe um rótulo escrito em caixa de sentença
- **THEN** ele é apresentado em caixa de sentença, sem conversão para caixa alta

#### Scenario: O padrão é verificado automaticamente
- **WHEN** a suíte de testes de `apps/frontend` é executada
- **THEN** um teste afirma que o tema declara a variante clara e a preservação
  da caixa como padrão do badge
