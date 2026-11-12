import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, qs } from './client';
import type { KnowledgeCase, KnowledgeCaseInput } from './knowledgeTypes';
import type { PagedResult } from './types';

export interface KnowledgeFilters {
  q: string;
  errorCode: string;
  page: number;
  pageSize: number;
}

export const useKnowledge = (f: KnowledgeFilters) =>
  useQuery({
    queryKey: ['knowledge', f],
    queryFn: () =>
      api<PagedResult<KnowledgeCase>>(`/api/knowledge${qs({ q: f.q, errorCode: f.errorCode, page: f.page, pageSize: f.pageSize })}`),
    placeholderData: keepPreviousData,
  });

export const useKnowledgeCase = (id: string | null) =>
  useQuery({ queryKey: ['knowledge', 'case', id], queryFn: () => api<KnowledgeCase>(`/api/knowledge/${id}`), enabled: id !== null });

export const useSaveKnowledgeCase = () => {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: { id?: string; data: KnowledgeCaseInput }) =>
      input.id
        ? api<KnowledgeCase>(`/api/knowledge/${input.id}`, { method: 'PUT', body: JSON.stringify(input.data) })
        : api<KnowledgeCase>('/api/knowledge', { method: 'POST', body: JSON.stringify(input.data) }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['knowledge'] }),
  });
};

export const useDeleteKnowledgeCase = () => {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api<void>(`/api/knowledge/${id}`, { method: 'DELETE' }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['knowledge'] }),
  });
};
