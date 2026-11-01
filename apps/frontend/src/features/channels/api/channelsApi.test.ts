import { afterEach, describe, vi } from 'vitest';
import { listChannels } from './channelsApi';
import { sessionFetchContract } from '../../../test/sessionContract';

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

// O cliente do apps/inbox: o segundo 401 encerra a sessão, como no apps/api (spec
// `panel-issuer-session`, "Segundo 401 no apps/inbox encerra a sessão").
describe('channelsApi: sessão pelo sessionFetch', () => {
  sessionFetchContract(() => listChannels());
});
