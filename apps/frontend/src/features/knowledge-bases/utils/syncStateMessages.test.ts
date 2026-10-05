import { describe, expect, it } from 'vitest';
import {
  IGNORED_FILE_CODES,
  SYNC_FAILURE_CODES,
  ignoredFileReason,
  syncFailureMessage,
  type SyncFailureText,
} from './syncStateMessages';
import { connectorErrorMessage } from './connectorErrors';
import { ConnectorsError } from '../api/connectorsApi';

const EMAIL = 'sync@exemplo.iam.gserviceaccount.com';
const GUARANTEE =
  'Nenhum documento saiu da base: ela continua respondendo com o conteúdo da última sincronização concluída, que pode estar desatualizado.';
const GUARANTEE_NEVER_COMPLETED =
  'Nenhum documento saiu da base, e nenhuma sincronização foi concluída ainda.';
const AUTOMATIC = 'na próxima rodada de sincronização (a cada 5 minutos)';

function failure(code: string, detail: string | null = null, completedBefore = true) {
  return syncFailureMessage({ code, detail }, { completedBefore, providerKey: 'google-drive' });
}

function allText(text: SyncFailureText): string {
  return [text.title, text.sentence, text.guarantee, text.remedy ?? ''].join(' ');
}

// A proibição existe para a tela não mandar excluir a base, que é escopo da
// #136; a garantia foi reescrita para caber nela (design.md, D4 e C6). A #136
// revê esta varredura junto com o botão de exclusão.
const FORBIDDEN = /exclu|remov|apag/i;

describe('syncFailureMessage — falha da base', () => {
  it('access-denied mostra o e-mail, a garantia e o caminho de recuperação', () => {
    const text = failure('access-denied', EMAIL);
    expect(text.title).toBe('Sem acesso à pasta');
    expect(text.sentence).toBe(`A conta ${EMAIL} não tem acesso a esta pasta.`);
    expect(text.guarantee).toBe(GUARANTEE);
    expect(text.remedy).toBe(
      'Para resolver, compartilhe a pasta com essa conta como Leitor. Se a pasta deixou de existir no Drive, crie uma nova base com outra pasta: esta base não pode ser apontada para outra pasta.',
    );
  });

  it('access-denied sem detalhe fala da conta de serviço', () => {
    expect(failure('access-denied').sentence).toBe(
      'A conta de serviço não tem acesso a esta pasta.',
    );
  });

  it('rate-limited é passageira, automática, e não fala de acesso', () => {
    const text = failure('rate-limited');
    // A negativa vem primeiro: é ela que o guarda verificado exercita (tasks 3.4).
    expect(allText(text)).not.toMatch(/acesso|compartilh/i);
    expect(text.sentence).toBe(
      `O Google limitou as chamadas por um momento. A falha é passageira, e a próxima tentativa é automática, ${AUTOMATIC}.`,
    );
  });

  it('folder-trashed e not-a-folder têm remédio próprio', () => {
    expect(failure('folder-trashed').remedy).toBe(
      'Para resolver, restaure a pasta no Drive; a próxima sincronização volta a lê-la.',
    );
    expect(failure('not-a-folder').remedy).toBe(
      'Esta base não pode ser apontada para outra pasta: para sincronizar outra, crie uma nova base.',
    );
  });

  it('api-not-configured é configuração da instalação, não da pasta', () => {
    const text = failure('api-not-configured');
    expect(text.title).toBe('Configuração da instalação');
    expect(text.sentence).toMatch(/É configuração da instalação, não da pasta\.$/);
    expect(text.remedy).toBeNull();
  });

  it('provider-error usa o detalhe como motivo', () => {
    expect(failure('provider-error', 'bad-request').sentence).toBe(
      `O Google recusou a operação (bad-request). A próxima tentativa é automática, ${AUTOMATIC}.`,
    );
  });

  it('sem sincronização concluída, a garantia muda', () => {
    expect(failure('rate-limited', null, false).guarantee).toBe(GUARANTEE_NEVER_COMPLETED);
  });

  it('cada código conhecido tem texto próprio, diferente dos outros', () => {
    const sentences = SYNC_FAILURE_CODES.map((code) => failure(code, EMAIL).sentence);
    expect(new Set(sentences).size).toBe(SYNC_FAILURE_CODES.length);
    expect(SYNC_FAILURE_CODES).toEqual([
      'access-denied',
      'folder-trashed',
      'not-a-folder',
      'rate-limited',
      'provider-unavailable',
      'provider-error',
      'api-not-configured',
      'provider-auth-failed',
      'provider-not-configured',
      'sync-api-error',
    ]);
  });

  it('código desconhecido cai no texto neutro com o código', () => {
    const text = failure('codigo-novo');
    expect(text.title).toBe('Falha na sincronização');
    expect(text.sentence).toBe(
      'A sincronização falhou com um código que o painel não reconhece (codigo-novo).',
    );
    for (const code of SYNC_FAILURE_CODES) {
      expect(text.sentence).not.toBe(failure(code, EMAIL).sentence);
    }
  });
});

describe('ignoredFileReason — arquivo ignorado', () => {
  const cases: Array<[string, string | null, string]> = [
    [
      'shortcut-not-followed',
      null,
      'É um atalho, e a sincronização não segue atalhos. Coloque o arquivo em si na pasta.',
    ],
    [
      'subfolder-not-synced',
      null,
      'É uma subpasta. Só os arquivos da raiz da pasta entram na base.',
    ],
    [
      'unsupported-type',
      'application/vnd.ms-excel',
      'Tipo não suportado (application/vnd.ms-excel). Só Google Docs e arquivos .md entram na base.',
    ],
    [
      'download-blocked',
      null,
      'O download está bloqueado para leitores. Permita o download para leitores neste arquivo, no Drive.',
    ],
    [
      'file-not-found',
      null,
      'O arquivo não foi encontrado na hora do download. Se ele continuar na pasta, a próxima sincronização tenta de novo.',
    ],
    [
      'provider-error',
      'missing-md5-checksum',
      'O Google recusou a leitura deste arquivo (missing-md5-checksum). A próxima sincronização tenta de novo.',
    ],
    [
      'provider-unavailable',
      null,
      'O Google não respondeu ao exportar este arquivo. A próxima sincronização tenta de novo.',
    ],
    [
      'access-denied',
      EMAIL,
      'A conta de serviço não tem acesso a este arquivo. Compartilhe o arquivo com a conta como Leitor.',
    ],
    ['too-large', '2097152', 'Maior que o limite de 1 MiB depois da exportação (2097152 bytes).'],
    [
      'unsupported-source-type',
      null,
      'O servidor não aceitou o formato enviado pela sincronização. Não depende do arquivo: é falha da integração.',
    ],
    [
      'null-character',
      null,
      'O conteúdo exportado tem um caractere nulo, que o servidor não aceita. Corrija o arquivo no Drive.',
    ],
    [
      'empty-content',
      null,
      'O arquivo está vazio depois da exportação. Ele entra na base quando tiver conteúdo.',
    ],
  ];

  it.each(cases)('%s', (code, detail, expected) => {
    expect(ignoredFileReason({ code, detail })).toBe(expected);
  });

  it('a tabela cobre exatamente os códigos conhecidos', () => {
    expect([...IGNORED_FILE_CODES].sort()).toEqual(cases.map(([code]) => code).sort());
  });

  it('unsupported-type sem detalhe não mostra parênteses vazios', () => {
    expect(ignoredFileReason({ code: 'unsupported-type', detail: null })).toBe(
      'Tipo não suportado. Só Google Docs e arquivos .md entram na base.',
    );
  });

  it('código desconhecido cai no texto neutro com o código', () => {
    const text = ignoredFileReason({ code: 'codigo-novo', detail: null });
    expect(text).toBe('Motivo que o painel não reconhece (codigo-novo).');
    for (const [code, detail, expected] of cases) {
      expect(text).not.toBe(expected);
      expect(ignoredFileReason({ code, detail })).not.toBe(text);
    }
  });
});

describe('nenhum texto manda excluir', () => {
  it('nenhum texto de falha da base, de arquivo ignorado nem do pedido contém exclu, remov ou apag', () => {
    const texts: string[] = [];
    for (const code of [...SYNC_FAILURE_CODES, 'codigo-novo']) {
      texts.push(allText(failure(code, EMAIL, true)), allText(failure(code, EMAIL, false)));
    }
    for (const code of [...IGNORED_FILE_CODES, 'codigo-novo']) {
      texts.push(
        ignoredFileReason({ code, detail: 'x' }),
        ignoredFileReason({ code, detail: null }),
      );
    }
    for (const code of [
      'knowledge-base-not-found',
      'sync-not-configured',
      'sync-api-unavailable',
      'sync-api-error',
    ]) {
      texts.push(
        connectorErrorMessage(
          new ConnectorsError('http', 'x', { status: 503, code }),
          'sync-request',
        ),
      );
    }
    texts.push(connectorErrorMessage(new ConnectorsError('network', 'x'), 'sync-request'));

    expect(texts.length).toBeGreaterThan(40);
    for (const text of texts) {
      expect(text).not.toMatch(FORBIDDEN);
    }
  });
});
