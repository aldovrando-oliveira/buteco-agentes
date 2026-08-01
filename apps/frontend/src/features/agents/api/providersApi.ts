import type { ProviderCatalogEntry } from '../types/agent';
import { request } from './agentsApi';

export function listProviders(): Promise<ProviderCatalogEntry[]> {
  return request<ProviderCatalogEntry[]>('/providers');
}
