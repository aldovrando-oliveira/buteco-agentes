import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, getSystemInsights } from './insightsApi';
import { systemInsightsFixture } from '../test/systemInsightsFixture';
import { setToken, getToken } from '../../../auth/token';

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  } as Response;
}

describe('insightsApi', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
    sessionStorage.clear();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('monta a URL com os DOIS limites obrigatórios', async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse(systemInsightsFixture()));

    await getSystemInsights('2026-09-15T03:00:00.000Z', '2026-09-25T02:59:59.000Z');

    const [url] = vi.mocked(fetch).mock.calls[0];
    // A rota responde 400 sem qualquer um dos dois — não existe período
    // implícito (`InsightsPeriod.MissingBoundMessage`).
    expect(String(url)).toContain('/insights/system?');
    expect(String(url)).toContain('from=2026-09-15T03%3A00%3A00.000Z');
    expect(String(url)).toContain('to=2026-09-25T02%3A59%3A59.000Z');
  });

  it('anexa o token quando existe', async () => {
    setToken('token-de-teste');
    vi.mocked(fetch).mockResolvedValue(jsonResponse(systemInsightsFixture()));

    await getSystemInsights('a', 'b');

    const [, init] = vi.mocked(fetch).mock.calls[0];
    expect((init?.headers as Record<string, string>).Authorization).toBe(
      'Bearer token-de-teste',
    );
  });

  it('401 limpa o token', async () => {
    setToken('token-que-expirou');
    vi.mocked(fetch).mockResolvedValue(jsonResponse({ title: 'Não autorizado' }, 401));

    await expect(getSystemInsights('a', 'b')).rejects.toThrow(ApiError);

    expect(getToken()).toBeNull();
  });

  it('o ValidationProblem da janela chega como ApiError com o corpo preservado', async () => {
    const problem = {
      title: 'Os dois limites do período são obrigatórios.',
      status: 400,
      errors: { from: ['O limite inicial é obrigatório.'] },
    };
    vi.mocked(fetch).mockResolvedValue(jsonResponse(problem, 400));

    // O corpo importa: é o que o cabeçalho da página mostra ao lado do
    // travessão, e sem ele a razão da falha vira "Erro 400" genérico.
    await expect(getSystemInsights('a', 'b')).rejects.toMatchObject({
      name: 'ApiError',
      status: 400,
      message: 'Os dois limites do período são obrigatórios.',
      problem,
    });
  });

  it('devolve o corpo desserializado sem tocar em nenhum campo', async () => {
    const body = systemInsightsFixture();
    vi.mocked(fetch).mockResolvedValue(jsonResponse(body));

    const result = await getSystemInsights('a', 'b');

    // O cliente NÃO normaliza, não preenche default e não aplica `?? 0`: o nulo
    // que a rota mandou é o nulo que o componente recebe. É a primeira das três
    // linhas de defesa da gramática.
    expect(result.tokens.conversation.inputTokens).toBeNull();
    expect(result.temporal.peakWeekday).toBeNull();
    expect(result).toEqual(body);
  });
});
