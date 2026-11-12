export type IncidentStatus = 'Open' | 'Investigating' | 'Resolved';
export type IncidentSeverity = 'Low' | 'Medium' | 'High' | 'Critical';

export interface Incident {
  id: string;
  title: string;
  errorCode: string;
  severity: IncidentSeverity;
  status: IncidentStatus;
  affectedServices: string[];
  affectedUserCodes: string[];
  relatedLogIds: string[];
  logCount: number;
  firstSeenAt: string;
  lastSeenAt: string;
  createdAt: string;
  updatedAt: string;
  resolvedAt?: string;
  resolutionNote?: string;
  aiSummary?: string;
  summaryGeneratedAt?: string;
}
