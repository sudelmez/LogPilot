import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, qs } from './client';
import type {
  AnalyticsSummary, ErrorCodeCount, LogEntry, PagedResult, ScenarioExecution, ScenarioSettings,
  ScenarioSummary, ServiceErrorShare, ServiceHealth, TrendPoint,
} from './types';
import { rangeParams } from '../lib/ranges';
import type { RangeKey } from '../lib/ranges';

const REFRESH_MS = 15_000;

// ---------- analytics ----------
export const useSummary = (range: RangeKey) =>
  useQuery({
    queryKey: ['summary', range],
    queryFn: () => api<AnalyticsSummary>(`/api/analytics/summary${qs(rangeParams(range))}`),
    refetchInterval: REFRESH_MS,
  });

export const useTrend = (range: RangeKey) =>
  useQuery({
    queryKey: ['trend', range],
    queryFn: () => api<TrendPoint[]>(`/api/analytics/error-trend${qs(rangeParams(range))}`),
    refetchInterval: REFRESH_MS,
  });

export const useErrorsByService = (range: RangeKey) =>
  useQuery({
    queryKey: ['errors-by-service', range],
    queryFn: () => api<ServiceErrorShare[]>(`/api/analytics/errors-by-service${qs(rangeParams(range))}`),
    refetchInterval: REFRESH_MS,
  });

export const useTopErrorCodes = (range: RangeKey, limit = 6) =>
  useQuery({
    queryKey: ['top-error-codes', range, limit],
    queryFn: () => api<ErrorCodeCount[]>(`/api/analytics/top-error-codes${qs({ ...rangeParams(range), limit })}`),
    refetchInterval: REFRESH_MS,
  });

export const useServiceHealth = (range: RangeKey) =>
  useQuery({
    queryKey: ['service-health', range],
    queryFn: () => api<ServiceHealth[]>(`/api/analytics/service-health${qs(rangeParams(range))}`),
    refetchInterval: REFRESH_MS,
  });

// ---------- logs ----------
export const useRecentErrors = (limit = 8) =>
  useQuery({
    queryKey: ['recent-errors', limit],
    queryFn: () => api<LogEntry[]>(`/api/logs/recent-errors${qs({ limit })}`),
    refetchInterval: REFRESH_MS,
  });

export interface LogFilters {
  level: string;
  application: string;
  service: string;
  userCode: string;
  errorCode: string;
  correlationId: string;
  transactionId: string;
  range: RangeKey | 'all';
  page: number;
  pageSize: number;
  ascending: boolean;
}

export const useLogs = (filters: LogFilters) =>
  useQuery({
    queryKey: ['logs', filters],
    queryFn: () => {
      const time = filters.range === 'all' ? {} : rangeParams(filters.range);
      return api<PagedResult<LogEntry>>(
        `/api/logs${qs({
          ...time,
          level: filters.level,
          application: filters.application,
          service: filters.service,
          userCode: filters.userCode,
          errorCode: filters.errorCode,
          correlationId: filters.correlationId,
          transactionId: filters.transactionId,
          page: filters.page,
          pageSize: filters.pageSize,
          ascending: filters.ascending,
        })}`,
      );
    },
    placeholderData: keepPreviousData,
  });

export const useLog = (id: string | null) =>
  useQuery({
    queryKey: ['log', id],
    queryFn: () => api<LogEntry>(`/api/logs/${id}`),
    enabled: id !== null,
  });

// ---------- scenarios ----------
export const useScenarios = () =>
  useQuery({ queryKey: ['scenarios'], queryFn: () => api<ScenarioSummary[]>('/api/scenarios') });

export const useExecutions = (scenarioId: string, page: number, pageSize: number) =>
  useQuery({
    queryKey: ['executions', scenarioId, page, pageSize],
    queryFn: () =>
      api<PagedResult<ScenarioExecution>>(`/api/scenarios/executions${qs({ scenarioId, page, pageSize })}`),
    placeholderData: keepPreviousData,
    // Bekleyen/çalışan kayıt varken sık, yoksa seyrek yenile.
    refetchInterval: (query) =>
      query.state.data?.items.some((e) => e.status === 'Pending' || e.status === 'Running') ? 2_000 : 10_000,
  });

export const useTriggerScenario = () => {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api<ScenarioExecution>(`/api/scenarios/${id}/trigger`, { method: 'POST' }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['executions'] }),
  });
};

export const useUpdateScenarioSettings = () => {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: { id: string; settings: ScenarioSettings }) =>
      api<ScenarioSummary>(`/api/scenarios/${input.id}/settings`, {
        method: 'PUT',
        body: JSON.stringify(input.settings),
      }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['scenarios'] }),
  });
};
