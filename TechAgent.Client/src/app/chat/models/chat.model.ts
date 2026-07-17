
export interface ChatHistoryEntry {
  role: string;
  message: string;
  createdAt: string;
  wasRefused: boolean;
}

export interface SessionSummary {
  sessionId: string;
  title: string;
  lastActivity: string;
}
