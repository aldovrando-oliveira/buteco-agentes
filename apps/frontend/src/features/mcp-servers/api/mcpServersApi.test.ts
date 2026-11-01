import { afterEach, describe, vi } from 'vitest';
import { listMcpServers } from './mcpServersApi';
import { sessionFetchContract } from '../../../test/sessionContract';

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('mcpServersApi: sessão pelo sessionFetch', () => {
  sessionFetchContract(() => listMcpServers());
});
