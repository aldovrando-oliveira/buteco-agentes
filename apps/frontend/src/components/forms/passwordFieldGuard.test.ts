import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// Todo campo de senha do painel é o PasswordField, que marca aria-invalid com erro (#170;
// spec `frontend-password-field`). Um PasswordInput importado direto do Mantine voltaria
// a deixar o campo inválido sem o atributo. O vitest roda com a raiz em apps/frontend.
const SRC = resolve(process.cwd(), 'src');
// O PasswordField é o único autorizado; este arquivo traz exemplos de import no caso
// que confere o reconhecimento, e por isso fica fora da varredura.
const ALLOWED = new Set(['components/forms/PasswordField.tsx', 'components/forms/passwordFieldGuard.test.ts']);

function sourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) return sourceFiles(path);
    return /\.(ts|tsx)$/.test(name) ? [path] : [];
  });
}

/** Os nomes importados de `@mantine/core`, em qualquer forma de import nomeado, numa linha ou em várias. */
function mantineImports(source: string): string[] {
  const names: string[] = [];
  for (const match of source.matchAll(/import\s+(?:type\s+)?\{([^}]*)\}\s*from\s*['"]@mantine\/core['"]/g)) {
    for (const part of match[1].split(',')) {
      const name = part.trim().replace(/^type\s+/, '').split(/\s+as\s+/)[0].trim();
      if (name) names.push(name);
    }
  }
  return names;
}

describe('PasswordInput só no PasswordField', () => {
  it('nenhum arquivo de src/ fora do PasswordField importa PasswordInput de @mantine/core', () => {
    const offenders = sourceFiles(SRC)
      .map((path) => relative(SRC, path).split('\\').join('/'))
      .filter((path) => !ALLOWED.has(path))
      .filter((path) => mantineImports(readFileSync(join(SRC, path), 'utf8')).includes('PasswordInput'));

    expect(offenders).toEqual([]);
  });

  it('o reconhecimento pega as formas de import que existem no painel', () => {
    expect(mantineImports("import { PasswordInput, Stack, TextInput } from '@mantine/core';")).toContain('PasswordInput');
    expect(mantineImports("import {\n  Button,\n  PasswordInput,\n} from '@mantine/core';")).toContain('PasswordInput');
    expect(mantineImports("import { PasswordInput as Senha } from \"@mantine/core\";")).toContain('PasswordInput');
    expect(mantineImports("import { Stack } from '@mantine/core';")).not.toContain('PasswordInput');
  });
});
