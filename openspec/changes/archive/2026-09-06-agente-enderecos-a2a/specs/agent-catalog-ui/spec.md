## ADDED Requirements

### Requirement: Endereços A2A no detalhe do agente
O sistema SHALL exibir, na visão geral do detalhe do agente em
`apps/frontend`, os dois endereços públicos A2A do agente — o endpoint de
execução e o card de descoberta — cada um com uma ação de copiar, e o card de
descoberta também com uma ação de abri-lo.

Os endereços SHALL vir prontos da API. A interface SHALL NOT construí-los a
partir do endereço de onde a página foi servida.

Quando a resposta não trouxer os endereços, a interface SHALL informar que o
endereço público do servidor não está configurado, e SHALL NOT exibir endereço
vazio nem parcial.

Quando o agente estiver inativo, a interface SHALL avisar que ele continua
descobrível pelos endereços exibidos, mas rejeita as mensagens que receber —
duas condições que valem ao mesmo tempo e que o endereço visível, sozinho, não
comunica.

#### Scenario: Os dois endereços aparecem com ação de copiar
- **WHEN** o operador abre a visão geral de um agente cuja resposta traz os
  endereços
- **THEN** os endereços do endpoint de execução e do card de descoberta são
  exibidos, cada um com uma ação de copiar

#### Scenario: O card de descoberta pode ser aberto
- **WHEN** o operador visualiza o endereço do card de descoberta
- **THEN** há uma ação que o abre

#### Scenario: Agente inativo é avisado como descobrível mas indisponível
- **WHEN** o operador abre a visão geral de um agente desativado
- **THEN** os endereços continuam exibidos, acompanhados do aviso de que o
  agente segue descobrível mas rejeita as mensagens que receber

#### Scenario: Agente ativo não exibe o aviso
- **WHEN** o operador abre a visão geral de um agente ativo
- **THEN** os endereços são exibidos sem o aviso de indisponibilidade

#### Scenario: Sem endereços na resposta, a interface explica a ausência
- **WHEN** o operador abre a visão geral de um agente cuja resposta não traz os
  endereços
- **THEN** a interface informa que o endereço público do servidor não está
  configurado, sem exibir endereço vazio ou parcial
