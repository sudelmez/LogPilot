export type Severity = 'Debug' | 'Information' | 'Warning' | 'Error' | 'Critical';

export interface LogEntry {
  id: string;
  timestamp: string;
  level: Severity;
  application: string;
  service: string;
  environment: string;
  userCode?: string;
  userId?: string;
  correlationId?: string;
  transactionId?: string;
  operation?: string;
  outcome?: 'Success' | 'Failure';
  errorCode?: string;
  message: string;
  exceptionType?: string;
  durationMs?: number;
  metadata: Record<string, string>;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface RequestStats {
  completed: number;
  failed: number;
  failureRate: number;
}

export interface AnalyticsSummary {
  from: string;
  to: string;
  totalLogs: number;
  errorCount: number;
  warningCount: number;
  logErrorRatio: number;
  requests: RequestStats;
}

export interface TrendPoint {
  bucketStart: string;
  total: number;
  errors: number;
  warnings: number;
}

export interface ServiceErrorShare {
  service: string;
  errors: number;
  totalLogs: number;
  logErrorRatio: number;
  shareOfAllErrors: number;
}

export interface ErrorCodeCount {
  errorCode: string;
  count: number;
  distinctUsers: number;
  lastSeen: string;
}

export type HealthStatus = 'Unknown' | 'Healthy' | 'Degraded' | 'Unhealthy';

export interface ServiceHealth {
  service: string;
  totalLogs: number;
  errors: number;
  warnings: number;
  logErrorRatio: number;
  avgDurationMs?: number;
  maxDurationMs?: number;
  lastErrorAt?: string;
  status: HealthStatus;
}

export interface ScenarioSettings {
  enabled: boolean;
  intervalSeconds: number;
  probability: number;
  weight: number;
}

export interface ScenarioSummary {
  id: string;
  name: string;
  description: string;
  kind: 'Normal' | 'Incident';
  settings: ScenarioSettings;
  stepCount: number;
}

export type ExecutionStatus = 'Pending' | 'Running' | 'Completed' | 'Failed';

export interface ScenarioExecution {
  id: string;
  scenarioId: string;
  triggeredBy: 'Schedule' | 'Manual';
  status: ExecutionStatus;
  requestedAt: string;
  startedAt?: string;
  finishedAt?: string;
  seed?: number;
  generatedLogCount: number;
  correlationIds: string[];
  error?: string;
}
