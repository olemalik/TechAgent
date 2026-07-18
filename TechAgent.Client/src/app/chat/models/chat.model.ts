
export interface ChatHistoryEntry {
  id: number;
  role: string;
  message: string;
  createdAt: string;
  wasRefused: boolean;
  feedbackScore: number | null;
  attachmentName?: string | null;
  attachmentUrl?: string | null;
  attachmentContentType?: string | null;
}

export interface SessionSummary {
  sessionId: string;
  title: string;
  lastActivity: string;
}
