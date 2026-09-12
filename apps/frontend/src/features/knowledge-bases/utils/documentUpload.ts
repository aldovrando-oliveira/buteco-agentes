// Extensões aceitas. A validação é do CLIENTE e não some com o `accept` do
// input: arquivo arrastado não passa por ele, então a checagem tem de existir
// depois da seleção, em ponto único.
export const ACCEPTED_EXTENSIONS = ['md', 'markdown', 'txt'] as const;

export const ACCEPTED_ATTRIBUTE = '.md,.markdown,.txt';

export function extensionOf(fileName: string): string {
  const parts = fileName.split('.');
  return parts.length > 1 ? (parts.pop() ?? '').toLowerCase() : '';
}

export function isAcceptedExtension(fileName: string): boolean {
  return (ACCEPTED_EXTENSIONS as readonly string[]).includes(extensionOf(fileName));
}

// Título sugerido a partir do nome do arquivo: extensão removida, `-` e `_`
// viram espaço. Editável pelo operador — é sugestão, não imposição.
export function titleFromFileName(fileName: string): string {
  return fileName
    .replace(/\.(md|markdown|txt)$/i, '')
    .replace(/[-_]+/g, ' ')
    .trim();
}

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1).replace('.', ',')} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1).replace('.', ',')} MB`;
}

export type SelectedFile =
  | {
      fileName: string;
      size: string;
      accepted: true;
      title: string;
      content: string;
    }
  | {
      fileName: string;
      size: string;
      accepted: false;
      error: string;
    };

function rejection(file: File, error: string): SelectedFile {
  return { fileName: file.name, size: formatFileSize(file.size), accepted: false, error };
}

// PONTO ÚNICO DE CONVERGÊNCIA das duas entradas de arquivo (design.md, D13).
//
// O seletor (FileButton) e o arrastar (onDrop) chamam ESTA função, e nada mais.
// O adaptador de `drop` só faz preventDefault, extrai `dataTransfer.files` e
// delega — nenhuma lógica própria. É essa convergência que faz o teste do
// caminho do seletor, exercitado com File real, cobrir a lógica dos dois: sem
// ela, metade do tratamento de arquivo ficaria com a conferência manual como
// única rede, porque o jsdom não implementa DataTransfer nem DragEvent.
//
// Lê com `await file.text()`, e NÃO com FileReader + callback `onload`.
//
// O motivo é ordem, não estilo: o protótipo empurra a recusa por extensão de
// forma SÍNCRONA e o arquivo aceito de dentro do `onload`, e por isso renderiza
// o `.pdf` recusado ANTES dos `.md` aceitos que vieram na frente na seleção.
// Percorrido ao vivo com três arquivos e confirmado. Aqui a leitura é sequencial
// e a lista sai na ordem em que o operador escolheu.
//
// Continua sendo leitura de texto no cliente com envio como string no corpo
// JSON — a D3 da etapa 1 não é reaberta, só o mecanismo de leitura muda.
export async function collectFiles(files: File[]): Promise<SelectedFile[]> {
  const collected: SelectedFile[] = [];

  for (const file of files) {
    if (!isAcceptedExtension(file.name)) {
      const extension = extensionOf(file.name);
      collected.push(
        rejection(
          file,
          extension
            ? `Formato .${extension} não é aceito. A base guarda texto markdown — converta o arquivo para .md ou .txt e suba de novo.`
            : 'Arquivo sem extensão reconhecida. A base guarda texto markdown — use .md, .markdown ou .txt.',
        ),
      );
      continue;
    }

    try {
      const content = await file.text();
      collected.push({
        fileName: file.name,
        size: formatFileSize(file.size),
        accepted: true,
        title: titleFromFileName(file.name),
        content,
      });
    } catch {
      collected.push(rejection(file, 'Não foi possível ler este arquivo.'));
    }
  }

  return collected;
}
