import { describe, expect, it } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';

// Este teste existe porque o mesmo defeito apareceu três vezes na conferência
// visual desta etapa e da anterior: um tom fixo da escala neutra usado como
// fundo de superfície. Funciona no tema claro e quebra no escuro, porque
// `gray[n]` é claro nos dois esquemas e `dark[n]` é escuro nos dois.
//
// Aconteceu com o fundo da página (D13 da identidade visual), com a faixa de
// cabeçalho de card e tabela (D8), e com a sessão selecionada no histórico. Nos
// três casos a suíte passou verde — jsdom não enxerga cor — e só a comparação
// com o protótipo pegou.
//
// Fundo de superfície precisa de uma variável declarada por esquema, ou de um
// token do Mantine que já troque sozinho.

const RAIZ = join(import.meta.dirname, '../..');
// Duas partes, e não uma: o valor pode estar dentro de um ternário
// (`bg={x ? 'gray.1' : undefined}`), então basta a linha declarar um fundo e
// citar um tom da escala neutra.
const DECLARA_FUNDO = /\bbg=|backgroundColor:/;
const TOM_NEUTRO_FIXO = /["'`](?:var\(--mantine-color-)?(?:gray|dark)[.-]\d/;

function arquivosDeComponente(dir: string): string[] {
  return readdirSync(dir).flatMap((nome) => {
    const caminho = join(dir, nome);
    if (statSync(caminho).isDirectory()) return arquivosDeComponente(caminho);
    if (!/\.tsx?$/.test(nome) || /\.test\.tsx?$/.test(nome)) return [];
    return [caminho];
  });
}

describe('tons de superfície', () => {
  it('nenhum componente usa tom fixo da escala neutra como fundo', () => {
    const infratores = arquivosDeComponente(RAIZ)
      .flatMap((caminho) =>
        readFileSync(caminho, 'utf8')
          .split('\n')
          .map((linha, i) => ({ caminho, linha: linha.trim(), numero: i + 1 }))
          .filter(({ linha }) => DECLARA_FUNDO.test(linha) && TOM_NEUTRO_FIXO.test(linha)),
      )
      .map(({ caminho, numero, linha }) => `${caminho.replace(RAIZ, 'src')}:${numero} → ${linha}`);

    expect(infratores).toEqual([]);
  });
});
