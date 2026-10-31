## MODIFIED Requirements

### Requirement: Rotação automática da chave de assinatura, sem reinício
A chave de assinatura SHALL ser trocada a cada 90 dias, com três janelas:

- **antes de assinar:** a próxima chave SHALL ser criada e publicada no JWKS 7 dias antes de começar a assinar;
- **depois de parar de assinar:** a chave anterior SHALL continuar publicada e validando por 15 dias;
- **depois disso:** a chave anterior SHALL sair do JWKS.

A troca de qual chave assina SHALL acontecer sem reiniciar o processo, até uma hora depois do instante previsto. Em qualquer instante, exatamente uma chave SHALL assinar.

A janela depois de parar de assinar SHALL cobrir todo token que o emissor valida com as próprias chaves, e não só o access token. O OpenIddict valida o refresh token, o código de autorização e o `id_token_hint` com as mesmas chaves de assinatura. O boot SHALL falhar quando a retenção for menor que a maior das durações configuradas do access token e do refresh token, mais o intervalo da verificação de rotação. SHALL falhar também quando o refresh token não tiver prazo.

#### Scenario: Próxima chave publicada antes de assinar
- **WHEN** faltam menos de 7 dias para o fim de assinatura da chave ativa e a verificação de rotação roda
- **THEN** existe uma segunda chave, publicada no JWKS, e os tokens emitidos continuam com o `kid` da chave ativa

#### Scenario: Troca de chave sem reinício
- **WHEN** o relógio passa do início de assinatura da próxima chave e a verificação de rotação roda, no mesmo processo
- **THEN** o token emitido a seguir traz o `kid` da nova chave, e o JWKS ainda publica a anterior

#### Scenario: Chave aposentada some do JWKS depois da retenção
- **WHEN** passam mais de 15 dias do fim de assinatura de uma chave e a verificação de rotação roda
- **THEN** o JWKS não publica mais aquele `kid`

#### Scenario: Token assinado pela chave anterior continua verificável na retenção
- **WHEN** um token emitido pela chave anterior, ainda dentro da validade, é verificado contra o JWKS depois da troca
- **THEN** a assinatura valida

#### Scenario: Refresh emitido pela chave anterior renova 13 dias depois da troca
- **WHEN** um usuário obtém um refresh token, a chave de assinatura é trocada, e o refresh é apresentado 13 dias depois da troca, ainda dentro dos 14 dias de validade
- **THEN** a resposta traz um novo access token e um novo refresh token, assinados pela chave nova

#### Scenario: Retenção menor que a duração do refresh derruba o boot
- **WHEN** o app sobe com uma duração de refresh token maior que a retenção menos o intervalo da verificação de rotação
- **THEN** o boot falha com uma mensagem que cita a retenção e a duração do refresh token
