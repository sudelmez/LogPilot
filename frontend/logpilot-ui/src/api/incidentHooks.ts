import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, qs } from './client';
import type { Incident, IncidentStatus } from './incidentTypes';
import type { LogEntry, PagedResult } from './types';

export interface IncidentFilters {
  status: string;      // 'active' | '' | IncidentStatus
  severity: string;
  page: number;
  pageSize: number;
}

export const useIncidents = (f: IncidentFilters) =>
  useQuery({
    queryKey: ['incidents', f],
    queryFn: () =>
      api<PagedResult<Incident>>(
        `/api/incidents${qs({
          activeOnly: f.status === 'active' ? true : undefined,
          status: f.status === 'active' ? undefined : f.status,
          severity: f.severity,
          page: f.page,
          pageSize: f.pageSize,
        })}`,
      ),
    placeholderData: keepPreviousData,
    refetchInterval: 15_000,
  });

export const useActiveIncidents = () =>
  useQuery({
    queryKey: ['incidents', 'active-summary'],
    queryFn: () => api<PagedResult<Incident>>('/api/incidents?activeOnly=true&pageSize=50'),
    refetchInterval: 15_000,
  });

export const useIncident = (id: string | null) =>
  useQuery({
    queryKey: ['incident', id],
    queryFn: () => api<Incident>(`/api/incidents/${id}`),
    enabled: id !== null,
  });

export const useIncidentLogs = (id: string | null) =>
  useQuery({
    queryKey: ['incident-logs', id],
    queryFn: () => api<LogEntry[]>(`/api/incidents/${id}/logs`),
    enabled: id !== null,
  });

export const useUpdateIncidentStatus = () => {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: { id: string; status: IncidentStatus; resolutionNote?: string; rootCause?: string }) =>
      api<Incident>(`/api/incidents/${input.id}/status`, {
        method: 'PATCH',
        body: JSON.stringify({ status: input.status, resolutionNote: input.resolutionNote, rootCause: input.rootCause }),
      }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['incidents'] });
      void client.invalidateQueries({ queryKey: ['incident'] });
      void client.invalidateQueries({ queryKey: ['knowledge'] });
    },
  });
};

export const useRegenerateSummary = () => {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api<Incident>(`/api/incidents/${id}/summary`, { method: 'POST' }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['incident'] }),
  });
};
