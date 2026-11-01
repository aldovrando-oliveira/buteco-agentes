import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { createHash } from 'node:crypto';
import { describe, expect, it } from 'vitest';

// Os cabeçalhos de segurança do shell do SPA (design.md da change
// painel-entra-pelo-emissor, D2; spec `server-deployment`, "Shell do SPA entregue com
// CSP e Referrer-Policy"). O nginx não roda no teste: ele é conferido pelo texto da
// configuração, e de verdade pelos curl do fechamento (D11).
// O vitest roda com a raiz em apps/frontend.
const nginx = readFileSync(resolve(process.cwd(), 'deploy/nginx.conf'), 'utf8');
const indexHtml = readFileSync(resolve(process.cwd(), 'index.html'), 'utf8');

/** As diretivas `add_header` do nível do `server`, fora de todo `location`. */
function serverLevelHeaders(conf: string): string[] {
  const server = conf.slice(conf.indexOf('server {'));
  const lines: string[] = [];
  let depth = 0;
  for (const raw of server.split('\n')) {
    const line = raw.replace(/#.*$/, '');
    if (depth === 1 && /^\s*add_header\b/.test(line)) lines.push(line.trim());
    depth += (line.match(/\{/g) ?? []).length - (line.match(/\}/g) ?? []).length;
  }
  return lines;
}

function indexHtmlBlockHeaders(conf: string): string[] {
  const block = /location = \/index\.html \{([^}]*)\}/.exec(conf)?.[1] ?? '';
  return block
    .split('\n')
    .map((l) => l.replace(/#.*$/, '').trim())
    .filter((l) => l.startsWith('add_header'));
}

function header(lines: string[], name: string): string | undefined {
  return lines.find((l) => l.startsWith(`add_header ${name} `));
}

describe('nginx.conf: cabeçalhos de segurança do shell', () => {
  const server = serverLevelHeaders(nginx);
  const indexBlock = indexHtmlBlockHeaders(nginx);

  it('a CSP leva o hash do script inline do index.html', () => {
    const scripts = [...indexHtml.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/g)].filter(
      ([, attrs]) => !/\bsrc=/.test(attrs),
    );
    expect(scripts).toHaveLength(1);
    const hash = createHash('sha256').update(scripts[0][2], 'utf8').digest('base64');

    expect(header(server, 'Content-Security-Policy')).toContain(`script-src 'self' 'sha256-${hash}'`);
  });

  it.each([
    ['no server', () => server],
    ['no location = /index.html', () => indexBlock],
  ])('a CSP e o Referrer-Policy estão %s, com always', (_, lines) => {
    expect(header(lines(), 'Content-Security-Policy')).toMatch(/" always;$/);
    expect(header(lines(), 'Referrer-Policy')).toBe('add_header Referrer-Policy "no-referrer" always;');
  });

  it('o location = /index.html repete a mesma CSP do server', () => {
    expect(header(indexBlock, 'Content-Security-Policy')).toBe(header(server, 'Content-Security-Policy'));
  });

  it('a CSP tem as diretivas do D2', () => {
    const csp = header(server, 'Content-Security-Policy') ?? '';
    for (const directive of [
      "default-src 'self'",
      "style-src 'self' 'unsafe-inline'",
      "img-src 'self' data:",
      "font-src 'self'",
      "connect-src 'self'",
      "object-src 'none'",
      "base-uri 'none'",
      "frame-ancestors 'none'",
      "form-action 'self'",
    ]) {
      expect(csp).toContain(directive);
    }
  });
});
