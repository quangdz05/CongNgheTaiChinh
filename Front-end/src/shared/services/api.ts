export type Match = {
  code: string;
  title: string;
  score: number;
  evidence: string;
  critical: boolean;
};
export type Review = {
  id: string;
  isFraud: boolean;
  notes: string;
  reviewer: string;
  reviewedAt: string;
};
export type Evaluation = {
  id: string;
  userId: string;
  amount: number;
  currency: string;
  merchant: string;
  category: string;
  country: string;
  deviceHash: string;
  ipHash: string;
  timestamp: string;
  mode: string;
  ruleScore: number;
  mlScore: number | null;
  finalScore: number;
  decision: string;
  aiStatus: string;
  modelVersion: string | null;
  ruleVersion: number;
  latencyMs: number;
  matches: Match[];
  features: Record<string, number>;
  reviews: Review[];
};
export type Policy = {
  id: number;
  version: number;
  highAmount: number;
  criticalAmount: number;
  velocityCount: number;
  sharedDeviceUsers: number;
  reviewThreshold: number;
  blockThreshold: number;
  mlWeight: number;
  amountEnabled: boolean;
  velocityEnabled: boolean;
  deviceEnabled: boolean;
  unusualEnabled: boolean;
  updatedBy: string;
};
export type Stats = {
  total: number;
  sampled: number;
  amount: number;
  allow: number;
  review: number;
  block: number;
  pending: number;
  confirmedFraud: number;
  averageLatencyMs: number;
  daily: { date: string; total: number; flagged: number }[];
  rings: { device: string; users: string[]; transactions: number }[];
};
export type TransactionSummary = Pick<
  Evaluation,
  | "id"
  | "userId"
  | "amount"
  | "currency"
  | "merchant"
  | "timestamp"
  | "finalScore"
  | "decision"
>;
let sessionController = new AbortController();
export class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
    public code?: string,
  ) {
    super(message);
  }
}
export function resetApiSession() {
  sessionController.abort();
  sessionController = new AbortController();
}
export async function api<T>(
  path: string,
  options: RequestInit = {},
): Promise<T> {
  const token = sessionStorage.getItem("fs-token");
  const signal = options.signal
    ? AbortSignal.any([sessionController.signal, options.signal])
    : sessionController.signal;
  const res = await fetch("/api" + path, {
    ...options,
    signal,
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: "Bearer " + token } : {}),
      ...options.headers,
    },
  });
  if (
    res.status === 401 &&
    path !== "/auth/login" &&
    token === sessionStorage.getItem("fs-token")
  ) {
    sessionStorage.removeItem("fs-token");
    window.dispatchEvent(new Event("fs-logout"));
  }
  if (!res.ok) {
    const data = await res.json().catch(() => ({}));
    throw new ApiError(
      data.error || data.title || `Yêu cầu thất bại (${res.status})`,
      res.status,
      data.code,
    );
  }
  return res.json();
}
export const post = <T>(path: string, body: unknown) =>
  api<T>(path, { method: "POST", body: JSON.stringify(body) });
export const money = (v: number) =>
  new Intl.NumberFormat("vi-VN", {
    style: "currency",
    currency: "VND",
    maximumFractionDigits: 0,
  }).format(v);
export const percent = (v: number) => Math.round(v * 100) + "%";
