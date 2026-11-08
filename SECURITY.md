# Security Policy

## Reporting a Vulnerability

**Please do not open a public issue for a security vulnerability.** A public
report makes the problem exploitable before a fix exists.

Report privately through either channel:

- **GitHub Security Advisories** — use the repository's *Security* tab →
  *Report a vulnerability*. This is the preferred channel, because it keeps
  the discussion attached to the repository and private until disclosure.
- **Email** — `aldovrando.oliveira@gmail.com`, with `SECURITY` in the
  subject line.

Please include, as far as you can determine it:

- the affected component (`apps/api`, `apps/workers`, `apps/inbox`,
  `apps/frontend`, or the deployment stack);
- a description of the problem and its impact;
- steps to reproduce, or a proof of concept;
- the commit or version you tested against.

## What to Expect

This project is maintained by a single person, so response times reflect
that rather than an SLA:

| Stage | Target |
|---|---|
| Acknowledgement of your report | within 5 business days |
| Initial assessment and severity | within 10 business days |
| Fix or mitigation plan communicated | depends on severity and scope |

You will be credited in the release notes for the fix unless you prefer
otherwise. Please give a reasonable window for a fix before disclosing
publicly.

## Sensitive Areas

These are the parts of the system where a vulnerability would have the
greatest impact. Reports touching them are prioritized.

**Credential encryption.** MCP server credentials (`apps/api`), channel
credentials (`apps/inbox`) and TOTP secrets (`apps/api`, below) are encrypted
with AES-GCM using per-domain keys supplied through environment variables.
Credentials are write-only and are never returned in any API response; the one
exception is the TOTP secret, returned once when enrollment starts. Anything that
could expose a stored credential, or that weakens the encryption, is in scope.

**Authentication tokens.** `apps/api` is an OpenID Connect issuer (OpenIddict)
and issues every token the system accepts. People sign in to the panel with an
email and password through the authorization code flow with PKCE, and receive an
RS256 access token (15 minutes) and a rotating refresh token (14 days). Each
service that calls `apps/api` (`apps/inbox`, `apps/connectors`) obtains its own
access token through the client credentials grant, with a secret of its own that
only it and `apps/api` hold; `apps/api` signs the token it sends to
`apps/connectors` in-process, with the same keys. Signing keys are rotated and
published as a JWKS. All three HTTP apps validate access tokens locally, accept
nothing else, and take the subject from a closed `client_id` table rather than
from `sub`; each subject is restricted to its own routes (in `apps/api`, a
subject table; in `apps/inbox`, only the panel). Token forgery, signature bypass,
`alg`/`aud`/`iss`/`typ` confusion, scope escalation from a service token, a
service secret letting one service act as another, refresh token reuse, and
lifetime handling are all in scope.

**Panel session in the browser.** The panel keeps its tokens (access, refresh
and ID token) in `localStorage`, shared across tabs. This is an accepted cost:
a script running in the panel's origin can read them. It is contained by a
Content Security Policy that allows only the panel's own scripts (the inline
script of `index.html` by hash) and by `Referrer-Policy: no-referrer`, both
sent by the panel's nginx. The authorization code is removed from the URL
before it is exchanged. After signing out ("Sair"), or after a user is deactivated, an
access token already issued stays valid for up to 15 minutes, because it is
validated by signature only; the next refresh is refused. Anything that lets
script run in the panel's origin, bypasses the CSP, leaks a token or code
through a URL or referrer, or keeps a session alive past sign-out beyond that
window is in scope.

**Two-step verification (TOTP).** A second factor is mandatory for every user
(RFC 6238, HMAC-SHA1, 6 digits, 30-second steps, one step of tolerance each way).
Someone without it enrolls during login, after the correct password and before any
session: the pending session cookie (HttpOnly, SameSite=Strict, bound to the user and
the security stamp) is the only credential the two enrollment routes accept, and the
authorize endpoint and token renewal refuse any session of a user without the second
factor. The TOTP
secret is stored encrypted with AES-256-GCM under its own key
(`Identity:TwoFactorEncryptionKey`, distinct from the other keys, checked at
startup), with the user id as associated data. It is returned once, when
enrollment starts, and never again. A code is accepted only for a step later than
the last accepted one, recorded atomically, so a code cannot be replayed. The ten
recovery codes (80 bits each) are stored only as SHA-256 hashes and are single-use.
Wrong codes count toward the same per-email lockout as wrong passwords, and a
correct password does not reset that count while the code is pending. Recovery-code
regeneration requires a valid code, and a person cannot remove their own
verification. Any user can reset another user's verification, which also
invalidates that user's sessions; this is accepted while there are no roles. Whoever
runs the stack can reset a user's verification with the `two-factor-reset` command
of the `apps/api` binary, which has no HTTP route. Until each person's first login
after the second factor became mandatory, someone who knows that person's password
could enroll their own authenticator first; this window is accepted and documented in
the deployment notes. Bypassing the second
step (including reaching a session or a token without it), replaying a code, recovering a secret or a recovery code from storage or a
response, or brute-forcing the code past the lockout are in scope.

**Shared secrets across processes.** No token signing key is shared: only
`apps/api` holds signing keys, and the other apps hold only the public JWKS.
`Mcp:CredentialEncryptionKey` (shared between `apps/api` and `apps/workers`) and
each service client secret (shared only between `apps/api` and that service)
must hold identical values on both sides. Anything that leaks these values, or
that would let one process accept material signed with a different key, is in
scope.

**Anonymous route allowlist.** Every HTTP route in `apps/api` and
`apps/inbox` requires a token by default. A route becomes anonymous only
with an explicit classification, validated at startup. A route reachable
without authentication that is not on that allowlist is a vulnerability, not
a configuration detail.

**Prompt injection through agent context.** Values assigned by a channel
provider or adapter may enter the agent's context block; free text typed by
an end user deliberately does not. The textual marker separating context
from user messages is a hint, not a structural boundary. Reports showing a
path for user-controlled text to reach the context block are in scope.

**Webhook authenticity.** The Telegram adapter verifies inbound webhooks
using a per-channel `secret_token`. The WAHA adapter does **not** verify
webhook authenticity — this is a known, accepted risk, classified explicitly
in the anonymous route allowlist *(accepted before any deployment served real
traffic; that acceptance is under reassessment)*, and does not need to be
reported as a new finding. Reports about the *consequences* of that gap beyond
what is already documented are welcome.

## Known Operational Risk

`.env.prod.example` ships placeholder values beginning with `changeme`, and
no startup check rejects them — the existing checks cover absence and empty
values, not content. A stack deployed from an unedited `.env.prod` will start
normally with public, predictable secrets. **Verify before every deploy that
no value in `.env.prod` still begins with `changeme`.** This is documented in
[`docs/deployment.md`](docs/deployment.md) *(accepted before any deployment
served real traffic; that acceptance is under reassessment)* and does not need
to be reported.

## Scope

In scope: the four applications in `apps/`, the shared library in `libs/`,
the deployment stack (`docker-compose.prod.yml`, `deploy/`), and the
documented configuration surface.

Out of scope: vulnerabilities in third-party services this project integrates
with (WAHA, Telegram Bot API, LLM providers, MCP servers) — report those to
the respective vendors. Also out of scope: findings that require an already
compromised host, or that depend on deliberately misconfigured deployments
contradicting [`docs/configuration.md`](docs/configuration.md).
