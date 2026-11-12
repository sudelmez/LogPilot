import { Chip } from '@mui/material';
import type { ChipProps } from '@mui/material';
import type { IncidentSeverity, IncidentStatus } from '../api/incidentTypes';

const severityColor: Record<IncidentSeverity, ChipProps['color']> = {
  Low: 'default', Medium: 'info', High: 'warning', Critical: 'error',
};

export function IncidentSeverityChip({ severity }: { severity: IncidentSeverity }) {
  return <Chip size="small" label={severity} color={severityColor[severity]} variant={severity === 'Critical' ? 'filled' : 'outlined'} />;
}

const statusColor: Record<IncidentStatus, ChipProps['color']> = {
  Open: 'error', Investigating: 'warning', Resolved: 'success',
};

export function IncidentStatusChip({ status }: { status: IncidentStatus }) {
  return <Chip size="small" label={status} color={statusColor[status]} />;
}
