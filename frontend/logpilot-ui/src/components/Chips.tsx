import { Chip } from '@mui/material';
import type { ChipProps } from '@mui/material';
import type { ExecutionStatus, HealthStatus, Severity } from '../api/types';

const levelColor: Record<Severity, ChipProps['color']> = {
  Debug: 'default',
  Information: 'info',
  Warning: 'warning',
  Error: 'error',
  Critical: 'error',
};

export function SeverityChip({ level }: { level: Severity }) {
  return (
    <Chip
      size="small"
      label={level === 'Information' ? 'Info' : level}
      color={levelColor[level]}
      variant={level === 'Critical' ? 'filled' : 'outlined'}
    />
  );
}

const healthColor: Record<HealthStatus, ChipProps['color']> = {
  Unknown: 'default',
  Healthy: 'success',
  Degraded: 'warning',
  Unhealthy: 'error',
};

export function HealthChip({ status }: { status: HealthStatus }) {
  return <Chip size="small" label={status} color={healthColor[status]} />;
}

const executionColor: Record<ExecutionStatus, ChipProps['color']> = {
  Pending: 'default',
  Running: 'info',
  Completed: 'success',
  Failed: 'error',
};

export function ExecutionChip({ status }: { status: ExecutionStatus }) {
  return <Chip size="small" label={status} color={executionColor[status]} variant="outlined" />;
}
