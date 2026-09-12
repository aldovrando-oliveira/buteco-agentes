import { describe, expect, it } from 'vitest';
import {
  collectFiles,
  formatFileSize,
  isAcceptedExtension,
  titleFromFileName,
} from './documentUpload';

function file(name: string, content = 'conteúdo'): File {
  return new File([content], name, { type: 'text/plain' });
}

describe('isAcceptedExtension', () => {
  it('aceita .md, .markdown e .txt, em qualquer caixa', () => {
    expect(isAcceptedExtension('politica.md')).toBe(true);
    expect(isAcceptedExtension('politica.MARKDOWN')).toBe(true);
    expect(isAcceptedExtension('politica.Txt')).toBe(true);
  });

  it('recusa binário e arquivo sem extensão', () => {
    expect(isAcceptedExtension('manual.pdf')).toBe(false);
    expect(isAcceptedExtension('foto.png')).toBe(false);
    expect(isAcceptedExtension('LEIAME')).toBe(false);
  });
});

describe('titleFromFileName', () => {
  it('remove a extensão e troca hífen e sublinhado por espaço', () => {
    expect(titleFromFileName('politica-de_reembolso.md')).toBe('politica de reembolso');
    expect(titleFromFileName('horarios internos.txt')).toBe('horarios internos');
  });

  it('colapsa separadores repetidos', () => {
    expect(titleFromFileName('faixas--de__atraso.markdown')).toBe('faixas de atraso');
  });
});

describe('formatFileSize', () => {
  it('usa B, KB e MB com vírgula decimal', () => {
    expect(formatFileSize(512)).toBe('512 B');
    expect(formatFileSize(2048)).toBe('2,0 KB');
    expect(formatFileSize(3 * 1024 * 1024)).toBe('3,0 MB');
  });
});

describe('collectFiles', () => {
  it('aceita arquivo de texto com título sugerido e conteúdo lido', async () => {
    const [entrada] = await collectFiles([
      file('politica-de_reembolso.md', '# Reembolso\n7 dias.'),
    ]);

    expect(entrada).toMatchObject({
      fileName: 'politica-de_reembolso.md',
      accepted: true,
      title: 'politica de reembolso',
      content: '# Reembolso\n7 dias.',
    });
  });

  it('recusa extensão não aceita com o motivo, sem descartar em silêncio', async () => {
    const [entrada] = await collectFiles([file('manual.pdf')]);

    expect(entrada.accepted).toBe(false);
    expect(entrada.accepted === false && entrada.error).toContain('.pdf');
  });

  // O defeito real do protótipo: ele empurra a recusa de forma síncrona e o
  // aceite de dentro do callback `onload` do FileReader, e por isso o `.pdf`
  // recusado aparece ANTES dos aceitos que vieram na frente na seleção.
  // Percorrido ao vivo antes de escrever este teste.
  it('preserva a ordem da seleção com aceite e recusa intercalados', async () => {
    const entradas = await collectFiles([
      file('primeiro.md'),
      file('segundo.pdf'),
      file('terceiro.txt'),
      file('quarto.png'),
    ]);

    expect(entradas.map((e) => e.fileName)).toEqual([
      'primeiro.md',
      'segundo.pdf',
      'terceiro.txt',
      'quarto.png',
    ]);
    expect(entradas.map((e) => e.accepted)).toEqual([true, false, true, false]);
  });

  it('seleção vazia devolve lista vazia', async () => {
    await expect(collectFiles([])).resolves.toEqual([]);
  });

  it('arquivo ilegível vira recusa, não exceção', async () => {
    const quebrado = file('quebrado.md');
    Object.defineProperty(quebrado, 'text', {
      value: () => Promise.reject(new Error('falha de leitura')),
    });

    const [entrada] = await collectFiles([quebrado]);

    expect(entrada.accepted).toBe(false);
    expect(entrada.accepted === false && entrada.error).toContain('Não foi possível ler');
  });
});
