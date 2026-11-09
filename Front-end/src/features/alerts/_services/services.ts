import { api, post } from "../../../shared/services/api";

export const stateLabels: Record<string, string> = {
  New: "Mới",
  Assigned: "Đã nhận",
  InProgress: "Đang điều tra",
  Waiting: "Đang chờ",
  Escalated: "Đã chuyển cấp",
  Closed: "Đã đóng",
};
export const resolutionLabels: Record<string, string> = {
  Unknown: "Chưa đủ bằng chứng",
  Fraud: "Gian lận",
  Legitimate: "Hợp lệ",
  Disputed: "Bằng chứng mâu thuẫn",
};
export type AlertRow = {
  id: string;
  transactionId: string;
  status: string;
  priority: string;
  ownerUserId: number | null;
  ownerName: string | null;
  resolution: string;
  createdAt: string;
  dueAt: string;
  version: number;
  userId: string;
  merchant: string;
  amount: number;
  currency: string;
  decision: string;
};
export type Alert = {
  id: string;
  transactionId: string;
  status: string;
  ownerUserId: number | null;
  priority: string;
  priorityReason: string;
  sourceType: string;
  version: number;
  resolution: string;
  dueAt: string;
  slaStartedAt: string;
  createdAt: string;
  closedAt: string | null;
  closedWithinSla: boolean | null;
  closureReasonCode: string | null;
  closureNotes: string | null;
  waitingUntil: string | null;
  waitingReason: string | null;
  escalationReason: string | null;
  handlingEpisode: number;
  evidenceJson: string;
};
export type AlertDetail = {
  alert: Alert;
  ownerName: string | null;
  allowedActions: string[];
  serverNow: string;
};
export type AlertEvent = {
  id: string;
  type: string;
  actorUserId: number | null;
  at: string;
  version: number;
  reason: string;
  beforeJson: string;
  afterJson: string;
};
export type AlertTimeline = {
  items: AlertEvent[];
  total: number;
  page: number;
  size: number;
};
export type AlertPage = {
  items: AlertRow[];
  total: number;
  page: number;
  size: number;
  serverNow: string;
};
export type AlertStats = {
  openAlerts: number;
  overdue: number;
  closedUnknown: number;
  closedDisputed: number;
  states: Record<string, number>;
  asOf: string;
};
export type AlertUser = { id: number; username: string; role: string };
export const getAlert = (id: string, signal: AbortSignal) =>
  api<AlertDetail>(`/alerts/${id}`, { signal });
export const alertCommand = (
  id: string,
  command: string,
  body: Record<string, unknown>,
) =>
  post<{ alert: Alert; appliedVersion: number; replayed: boolean }>(
    `/alerts/${id}/${command}`,
    body,
  );
