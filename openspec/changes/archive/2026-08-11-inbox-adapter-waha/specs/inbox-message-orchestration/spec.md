## MODIFIED Requirements

### Requirement: Ingestão de mensagem normalizada resolve Contact e Session
O sistema SHALL expor, via `apps/inbox`, um serviço interno que recebe uma
mensagem normalizada `(ChannelId, ExternalId, texto, receivedAt, metadado do contato)`,
resolve `Contact`/`Session` usando o resolvedor já existente — repassando o
metadado do contato para captura na criação (ver capability
`inbox-contact-session`, Requirement "Captura de metadado do contato na
criação") — e adiciona a mensagem ao buffer de debounce da sessão
resolvida — sem exigir nenhum endpoint HTTP para essa ingestão.

#### Scenario: Mensagem recebida é associada à Session resolvida
- **WHEN** o serviço interno é chamado com um `(ChannelId, ExternalId)`, um
  texto de mensagem e um metadado de contato
- **THEN** a `Session` correspondente é resolvida (criada ou reaproveitada,
  conforme as regras já existentes de resolução de sessão), o metadado
  informado é repassado à resolução de `Contact`, e a mensagem é adicionada
  ao buffer de debounce dessa `Session`
