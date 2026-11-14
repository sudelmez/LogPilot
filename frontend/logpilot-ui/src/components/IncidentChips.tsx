import type { IncidentSeverity, IncidentStatus } from '../api/incidentTypes';
import { StatusDot } from './ui';
import type { Tone } from './ui';

const severityTone: Record<IncidentSeverity, Tone> = {
  Low: 'neutral', Medium: 'info', High: 'warning', Critical: 'error',
};

export function IncidentSeverityChip({ severity }: { severity: IncidentSeverity }) {
  return <StatusDot tone={severityTone[severity]} label={severity} strong={severity === 'Critical'} />;
}

const statusTone: Record<IncidentStatus, Tone> = {
  Open: 'error', Investigating: 'warning', Resolved: 'success',
};

export function IncidentStatusChip({ status }: { status: IncidentStatus }) {
  return <StatusDot tone={statusTone[status]} label={status} />;
}
