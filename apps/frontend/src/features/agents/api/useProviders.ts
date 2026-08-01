import { useQuery } from '@tanstack/react-query';
import { listProviders } from './providersApi';

export function useProvidersQuery() {
  return useQuery({
    queryKey: ['providers'],
    queryFn: listProviders,
    staleTime: Infinity,
  });
}
