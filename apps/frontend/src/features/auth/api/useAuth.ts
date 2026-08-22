import { useMutation } from '@tanstack/react-query';
import { login } from './authApi';
import type { LoginInput } from '../types/auth';

export function useLoginMutation() {
  return useMutation({
    mutationFn: (input: LoginInput) => login(input),
  });
}
