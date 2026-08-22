## MODIFIED Requirements

### Requirement: Ingestão de mensagem normalizada resolve Contact e Session
O sistema SHALL expor, via `apps/inbox`, um serviço interno que recebe uma
mensagem normalizada `(ChannelId, ExternalId, texto, receivedAt, metadado do
contato, identificador externo da mensagem)`, resolve `Contact`/`Session`
usando o resolvedor já existente — repassando o metadado do contato para
captura na criação (ver capability `inbox-contact-session`, Requirement
"Captura de metadado do contato na criação") — persiste um registro durável
da mensagem de entrada (ver capability `inbox-message-history`, Requirement
"Mensagem de entrada persistida durante a ingestão") e adiciona a mensagem
ao buffer de debounce da sessão resolvida — sem exigir nenhum endpoint HTTP
para essa ingestão. Quando o identificador externo da mensagem já
corresponder a uma mensagem de entrada persistida para a mesma `Session`, o
serviço SHALL tratar a ingestão como deduplicada (ver capability
`inbox-message-history`, Requirement "Deduplicação de mensagem de entrada
por identificador externo"), sem adicionar conteúdo novo ao buffer de
debounce.

#### Scenario: Mensagem recebida é associada à Session resolvida
- **WHEN** o serviço interno é chamado com um `(ChannelId, ExternalId)`, um
  texto de mensagem, um metadado de contato e um identificador externo de
  mensagem inédito para essa `Session`
- **THEN** a `Session` correspondente é resolvida (criada ou reaproveitada,
  conforme as regras já existentes de resolução de sessão), o metadado
  informado é repassado à resolução de `Contact`, um registro durável da
  mensagem de entrada é persistido, e a mensagem é adicionada ao buffer de
  debounce dessa `Session`

#### Scenario: Mensagem com identificador externo já processado não é adicionada ao buffer novamente
- **WHEN** o serviço interno é chamado com um identificador externo de
  mensagem que já corresponde a uma mensagem de entrada persistida para a
  mesma `Session`
- **THEN** nenhum conteúdo novo é adicionado ao buffer de debounce dessa
  `Session` por essa chamada
