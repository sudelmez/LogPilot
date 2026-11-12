export type KnowledgeSource = 'ChatFeedback' | 'IncidentResolution' | 'Manual';

/** İnsanların doğruladığı geçmiş vaka: sorun, kök neden ve neyin düzelttiği. */
export interface KnowledgeCase {
  id: string;
  title: string;
  errorCodes: string[];
  services: string[];
  symptom?: string;
  rootCause?: string;
  resolution: string;
  source: KnowledgeSource;
  sourceId?: string;
  createdAt: string;
  updatedAt: string;
  timesSuggested: number;
  lastSuggestedAt?: string;
}

export interface KnowledgeCaseInput {
  title: string;
  resolution: string;
  rootCause?: string;
  symptom?: string;
  errorCodes: string[];
  services: string[];
}
