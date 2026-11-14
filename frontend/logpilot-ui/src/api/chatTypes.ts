export type ConfidenceLevel = 'NotApplicable' | 'Insufficient' | 'Low' | 'Medium' | 'High';

export interface EvidenceItem {
  errorCode?: string;
  service?: string;
  count: number;
  firstSeen?: string;
  lastSeen?: string;
  logIds: string[];
  userCodes: string[];
  correlationIds: string[];
}

export interface AssistantAnswer {
  summary: string;
  probableCause?: string;
  confidence: ConfidenceLevel;
  confidenceScore: number;
  confidenceFactors: string[];
  evidence: EvidenceItem[];
  evidenceLogIds: string[];
  affectedServices: string[];
  suggestedActions: string[];
  dataLimitations: string[];
  clarifyingQuestion?: string;
  relatedCases: RelatedCase[];
  /** Doluysa soru görev alanı dışındaydı ve reddedildi. */
  outOfScope?: 'OffTopic' | 'Abusive' | 'NotPermitted';
}

/** Backend'in eklediği, insanların doğruladığı geçmiş vaka (ipucu; kanıt değil). */
export interface RelatedCase {
  id: string;
  title: string;
  resolution: string;
  rootCause?: string;
  errorCodes: string[];
  services: string[];
  createdAt: string;
}

export interface MessageFeedback {
  helpful: boolean;
  resolution?: string;
  knowledgeCaseId?: string;
  createdAt: string;
}

export interface ToolExecution {
  name: string;
  argumentsJson: string;
  durationMs: number;
  resultCount: number;
  truncated: boolean;
  flaggedContent: number;
  error?: string;
  /** Zincirleme araçlarda gezilen tablolar, sırasıyla (backend araç sonucundan alır). */
  path?: InvestigationHop[];
}

export interface InvestigationHop {
  source: string;
  lookedUpBy: string;
  found: boolean;
  result?: string | null;
}

export interface ChatMessage {
  id: string;
  sessionId: string;
  role: 'User' | 'Assistant';
  content: string;
  answer?: AssistantAnswer;
  toolExecutions: ToolExecution[];
  createdAt: string;
  provider?: string;
  feedback?: MessageFeedback;
}

export interface ChatSession {
  id: string;
  userName: string;
  title: string;
  createdAt: string;
  updatedAt: string;
}
