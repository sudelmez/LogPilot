import type { ExecutionStatus, HealthStatus, Severity } from '../api/types';
import { StatusDot } from './ui';
import type { Tone } from './ui';

const levelTone: Record<Severity, Tone> = {
  Debug: 'neutral', Information: 'info', Warning: 'warning', Error: 'error', Critical: 'error',
};

export function SeverityChip({ level }: { level: Severity }) {
  return <StatusDot tone={levelTone[level]} label={level === 'Information' ? 'Info' : level} strong={level === 'Critical'} />;
}

const healthTone: Record<HealthStatus, Tone> = {
  Unknown: 'neutral', Healthy: 'success', Degraded: 'warning', Unhealthy: 'error',
};

export function HealthChip({ status }: { status: HealthStatus }) {
  return <StatusDot tone={healthTone[status]} label={status} strong={status === 'Unhealthy'} />;
}

const executionTone: Record<ExecutionStatus, Tone> = {
  Pending: 'neutral', Running: 'info', Completed: 'success', Failed: 'error',
};

export function ExecutionChip({ status }: { status: ExecutionStatus }) {
  return <StatusDot tone={executionTone[status]} label={status} />;
}
