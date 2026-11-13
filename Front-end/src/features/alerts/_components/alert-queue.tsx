import { useCallback, useEffect, useRef, useState } from "react";
import { RiskFindings } from "../../transactions/_components/risk-findings";
import {
  ApiError,
  api,
  money,
  percent,
  type Evaluation,
} from "../../../shared/services/api";
import {
  alertCommand,
  getAlert,
  resolutionLabels,
  stateLabels,
  type AlertDetail,
  type AlertEvent,
  type AlertPage,
  type AlertStats,
  type AlertTimeline,
  type AlertUser,
} from "../_services/services";

const displayDate = (date: string) =>
  new Date(date).toLocaleString("vi-VN", { timeZoneName: "short" });

function EvidenceList({ json }: { json: string }) {
  const records = JSON.parse(json) as {
    sourceType: string;
    reference: string;
    description: string;
    capturedAt: string;
  }[];
  const sources: Record<string, string> = {
    InternalRecord: "Hồ sơ nội bộ",
    ManualInvestigation: "Điều tra thủ công",
    Other: "Khác",
  };
  return (
    <ul>
      {records.map((e, i) => (
        <li key={i}>
          <strong>
            {sources[e.sourceType] ?? e.sourceType}: {e.reference}
          </strong>
          <p>{e.description}</p>
          <small>{displayDate(e.capturedAt)}</small>
        </li>
      ))}
    </ul>
  );
}

function EventChanges({ entry }: { entry: AlertEvent }) {
  const before = (JSON.parse(entry.beforeJson) ?? {}) as Record<
    string,
    unknown
  >;
  const after = JSON.parse(entry.afterJson) as Record<string, unknown>;
  const fields: Record<string, string> = {
    status: "Trạng thái",
    ownerUserId: "Người phụ trách",
    priority: "Ưu tiên",
    resolution: "Kết luận",
    dueAt: "Hạn SLA",
    waitingUntil: "Hẹn tiếp tục",
    closureNotes: "Căn cứ đóng",
    escalationReason: "Lý do chuyển cấp",
    handlingEpisode: "Vòng xử lý",
  };
  const format = (key: string, value: unknown) => {
    if (value == null) return "—";
    if (key === "status") return stateLabels[String(value)] ?? String(value);
    if (key === "resolution")
      return resolutionLabels[String(value)] ?? String(value);
    if (key === "ownerUserId") return `Tài khoản #${value}`;
    if (key === "dueAt" || key === "waitingUntil")
      return displayDate(String(value));
    return String(value);
  };
  return (
    <dl className="alert-changes">
      {Object.entries(fields)
        .filter(([key]) => before[key] !== after[key])
        .map(([key, label]) => (
          <div key={key}>
            <dt>{label}</dt>
            <dd>
              {format(key, before[key])} → {format(key, after[key])}
            </dd>
          </div>
        ))}
    </dl>
  );
}
const actionLabels: Record<string, string> = {
  claim: "Nhận việc",
  start: "Bắt đầu điều tra",
  wait: "Chờ thông tin",
  resume: "Tiếp tục",
  assign: "Phân công / giao lại",
  release: "Giải phóng",
  priority: "Đổi ưu tiên",
  escalate: "Chuyển cấp",
  close: "Đóng cảnh báo",
  reopen: "Mở lại",
  notes: "Thêm ghi chú",
};
const eventLabels: Record<string, string> = {
  Created: "Tạo cảnh báo",
  ImportedLegacy: "Nhập dữ liệu cũ",
  Claimed: "Nhận việc",
  Started: "Bắt đầu điều tra",
  WaitingEntered: "Chờ thông tin",
  Resumed: "Tiếp tục",
  Reassigned: "Phân công",
  Released: "Giải phóng",
  PriorityChanged: "Đổi ưu tiên",
  Escalated: "Chuyển cấp",
  Closed: "Đóng",
  Reopened: "Mở lại",
  NoteAdded: "Ghi chú",
};

export function AlertQueue({
  initialSearch = "",
  revision,
  stats,
  onChanged,
}: {
  initialSearch?: string;
  revision: number;
  stats: AlertStats | null;
  onChanged: () => Promise<void>;
}) {
  const [status, setStatus] = useState("");
  const [priority, setPriority] = useState("");
  const [owner, setOwner] = useState("");
  const [overdue, setOverdue] = useState(false);
  const [search, setSearch] = useState(initialSearch);
  const [page, setPage] = useState(1);
  const [data, setData] = useState<AlertPage | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const [error, setError] = useState("");
  const [lastSync, setLastSync] = useState(0);
  const [now, setNow] = useState(Date.now());
  const [stale, setStale] = useState(true);
  const [refresh, setRefresh] = useState(0);
  const generation = useRef(0);
  const requestRef = useRef<AbortController | null>(null);
  useEffect(() => {
    setSearch(initialSearch);
    setPage(1);
  }, [initialSearch]);
  const reload = useCallback(() => setRefresh((n) => n + 1), []);
  useEffect(() => {
    let active = true;
    const load = async () => {
      requestRef.current?.abort();
      const controller = new AbortController();
      requestRef.current = controller;
      const current = ++generation.current;
      const params = new URLSearchParams({ page: String(page), size: "20" });
      if (status) params.set("status", status);
      if (priority) params.set("priority", priority);
      if (owner) params.set("owner", owner);
      if (overdue) params.set("overdue", "true");
      if (search) params.set("search", search);
      try {
        const result = await api<AlertPage>("/alerts?" + params, {
          signal: controller.signal,
        });
        if (active && current === generation.current) {
          setData(result);
          setLastSync(Date.now());
          setStale(false);
          setError("");
        }
      } catch (e) {
        if (active && !controller.signal.aborted) {
          setStale(true);
          setError((e as Error).message);
        }
      }
    };
    setData(null);
    setStale(true);
    const debounce = setTimeout(() => void load(), 200);
    const timer = setInterval(() => {
      if (!document.hidden) void load();
    }, 10000);
    const focus = () => {
      if (!document.hidden) void load();
    };
    const offline = () => setStale(true);
    window.addEventListener("focus", focus);
    window.addEventListener("online", focus);
    window.addEventListener("offline", offline);
    document.addEventListener("visibilitychange", focus);
    return () => {
      active = false;
      clearTimeout(debounce);
      clearInterval(timer);
      requestRef.current?.abort();
      window.removeEventListener("focus", focus);
      window.removeEventListener("online", focus);
      window.removeEventListener("offline", offline);
      document.removeEventListener("visibilitychange", focus);
    };
  }, [status, priority, owner, overdue, search, page, revision, refresh]);
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, []);
  const filterChanged = () => setPage(1);
  const effectiveNow = data
    ? new Date(data.serverNow).getTime() + Math.max(0, now - lastSync)
    : now;
  return (
    <section className="panel alert-queue">
      <div className="panel-heading">
        <div>
          <h2>Công việc điều tra</h2>
          <p>
            {stats?.openAlerts ?? "—"} đang mở · {stats?.overdue ?? "—"} quá SLA
            · {stats?.states.Escalated ?? "—"} chuyển cấp
          </p>
          <p>
            Đã đóng chưa đủ bằng chứng: {stats?.closedUnknown ?? "—"} · Mâu
            thuẫn: {stats?.closedDisputed ?? "—"}
          </p>
        </div>
        <button className="button" onClick={reload}>
          Tải lại
        </button>
      </div>
      <p className="alert-sync" role="status">
        Cập nhật định kỳ 10 giây ·{" "}
        {lastSync
          ? `Đồng bộ: ${displayDate(new Date(lastSync).toISOString())}`
          : "Chưa đồng bộ"}
        {(stale || now - lastSync > 30000) && (
          <strong> · Dữ liệu có thể đã cũ</strong>
        )}
      </p>
      <div className="toolbar alert-filters">
        <input
          aria-label="Tìm cảnh báo"
          maxLength={100}
          placeholder="Mã cảnh báo / giao dịch / khách hàng / merchant"
          value={search}
          onChange={(e) => {
            setSearch(e.target.value);
            filterChanged();
          }}
        />
        <select
          aria-label="Trạng thái cảnh báo"
          value={status}
          onChange={(e) => {
            setStatus(e.target.value);
            filterChanged();
          }}
        >
          <option value="">Đang mở</option>
          <option value="All">Tất cả trạng thái</option>
          {Object.entries(stateLabels).map(([k, v]) => (
            <option key={k} value={k}>
              {v}
            </option>
          ))}
        </select>
        <select
          aria-label="Mức ưu tiên"
          value={priority}
          onChange={(e) => {
            setPriority(e.target.value);
            filterChanged();
          }}
        >
          <option value="">Mọi ưu tiên</option>
          {["P1", "P2", "P3"].map((p) => (
            <option key={p}>{p}</option>
          ))}
        </select>
        <select
          aria-label="Người phụ trách"
          value={owner}
          onChange={(e) => {
            setOwner(e.target.value);
            filterChanged();
          }}
        >
          <option value="">Cả workspace</option>
          <option value="mine">Việc của tôi</option>
          <option value="unassigned">Chưa nhận</option>
        </select>
        <label>
          <input
            type="checkbox"
            checked={overdue}
            onChange={(e) => {
              setOverdue(e.target.checked);
              filterChanged();
            }}
          />
          Quá SLA
        </label>
      </div>
      {error && (
        <div className="error" role="alert">
          {error}
        </div>
      )}
      <div className="table-scroll">
        <table>
          <thead>
            <tr>
              <th>CẢNH BÁO / GIAO DỊCH</th>
              <th>ƯU TIÊN</th>
              <th>TRẠNG THÁI</th>
              <th>PHỤ TRÁCH</th>
              <th>HẠN SLA</th>
              <th>KẾT LUẬN</th>
            </tr>
          </thead>
          <tbody>
            {data?.items.map((a) => (
              <tr key={a.id}>
                <td>
                  <button
                    className="alert-link"
                    onClick={() => setSelected(a.id)}
                  >
                    {a.id.slice(0, 8)} · {a.userId}
                  </button>
                  <small>
                    {a.transactionId.slice(0, 8)} · {a.merchant} ·{" "}
                    {money(a.amount)} · {a.decision}
                  </small>
                </td>
                <td>
                  <span
                    className={`badge ${a.priority === "P1" ? "block" : "review"}`}
                  >
                    {a.priority}
                  </span>
                </td>
                <td>{stateLabels[a.status]}</td>
                <td>{a.ownerName ?? "Chưa nhận"}</td>
                <td>
                  {displayDate(a.dueAt)}
                  {a.status !== "Closed" &&
                    new Date(a.dueAt).getTime() <= effectiveNow && (
                      <strong className="text-amber">Quá SLA</strong>
                    )}
                </td>
                <td>
                  {a.status === "Closed"
                    ? resolutionLabels[a.resolution]
                    : "Chưa kết luận"}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {data && !data.items.length && (
        <div className="empty">Không có cảnh báo phù hợp.</div>
      )}
      {!data && !error && <div className="empty">Đang tải cảnh báo…</div>}
      <div className="pagination">
        <span>
          {data?.total ?? "—"} cảnh báo · Trang {page}
        </span>
        <div>
          <button
            className="button"
            disabled={page <= 1}
            onClick={() => setPage((p) => p - 1)}
          >
            Trước
          </button>
          <button
            className="button"
            disabled={!data || page * 20 >= data.total}
            onClick={() => setPage((p) => p + 1)}
          >
            Sau
          </button>
        </div>
      </div>
      {selected && (
        <AlertInspector
          key={selected}
          id={selected}
          onClose={() => setSelected(null)}
          onChanged={async () => {
            reload();
            await onChanged();
          }}
        />
      )}
    </section>
  );
}

function AlertInspector({
  id,
  onClose,
  onChanged,
}: {
  id: string;
  onClose: () => void;
  onChanged: () => Promise<void>;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [detail, setDetail] = useState<AlertDetail | null>(null);
  const [evaluation, setEvaluation] = useState<Evaluation | null>(null);
  const [users, setUsers] = useState<AlertUser[]>([]);
  const [events, setEvents] = useState<AlertEvent[]>([]);
  const [timelinePage, setTimelinePage] = useState(1);
  const [timelineTotal, setTimelineTotal] = useState(0);
  const [action, setAction] = useState("");
  const [reason, setReason] = useState("");
  const [owner, setOwner] = useState("");
  const [priority, setPriority] = useState("P1");
  const [until, setUntil] = useState("");
  const [waitingType, setWaitingType] = useState("ExternalInformation");
  const [closure, setClosure] = useState("InsufficientEvidence");
  const [evidenceRef, setEvidenceRef] = useState("");
  const [evidenceSource, setEvidenceSource] = useState("InternalRecord");
  const [evidenceDescription, setEvidenceDescription] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [syncError, setSyncError] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [lastSync, setLastSync] = useState(0);
  const [now, setNow] = useState(Date.now());
  const [stale, setStale] = useState(true);
  // Preserve requestId and exact payload for a deliberate retry after an uncertain network result.
  const pending = useRef<{
    command: string;
    body: Record<string, unknown>;
  } | null>(null);
  const [uncertain, setUncertain] = useState(false);
  useEffect(() => {
    dialog.current?.showModal();
    return () => dialog.current?.close();
  }, []);
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, []);
  useEffect(() => {
    let active = true;
    let controller: AbortController | null = null;
    const load = async () => {
      controller?.abort();
      const c = new AbortController();
      controller = c;
      try {
        const result = await getAlert(id, c.signal);
        const [timeline, people, tx] = await Promise.all([
          api<AlertTimeline>(
            `/alerts/${id}/timeline?page=${timelinePage}&size=20`,
            { signal: c.signal },
          ),
          api<AlertUser[]>("/alerts/users", { signal: c.signal }),
          api<Evaluation>(`/transactions/${result.alert.transactionId}`, {
            signal: c.signal,
          }),
        ]);
        if (active && !c.signal.aborted) {
          setDetail((old) =>
            !old || result.alert.version >= old.alert.version ? result : old,
          );
          setUsers(people);
          setEvents(timeline.items);
          setTimelineTotal(timeline.total);
          setEvaluation(tx);
          setLastSync(Date.now());
          setStale(false);
          setSyncError("");
        }
      } catch (e) {
        if (active && !c.signal.aborted) {
          setStale(true);
          setSyncError((e as Error).message);
        }
      }
    };
    void load();
    const timer = setInterval(() => {
      if (!document.hidden) void load();
    }, 10000);
    const focus = () => {
      if (!document.hidden) void load();
    };
    const offline = () => setStale(true);
    window.addEventListener("focus", focus);
    window.addEventListener("online", focus);
    window.addEventListener("offline", offline);
    document.addEventListener("visibilitychange", focus);
    return () => {
      active = false;
      controller?.abort();
      clearInterval(timer);
      window.removeEventListener("focus", focus);
      window.removeEventListener("online", focus);
      window.removeEventListener("offline", offline);
      document.removeEventListener("visibilitychange", focus);
    };
  }, [id, timelinePage, refresh]);
  async function send(retry = false) {
    if (!detail) return;
    let operation = retry ? pending.current : null;
    if (!operation) {
      const body: Record<string, unknown> = {
        requestId: crypto.randomUUID(),
        expectedVersion: detail.alert.version,
      };
      if (
        [
          "assign",
          "release",
          "priority",
          "escalate",
          "reopen",
          "wait",
        ].includes(action)
      )
        body.reason = reason;
      if (["assign", "reopen"].includes(action))
        body.ownerUserId = Number(owner);
      if (action === "priority") body.priority = priority;
      if (action === "wait") {
        body.waitingType = waitingType;
        body.waitingUntil = until ? new Date(until).toISOString() : null;
      }
      if (["close", "notes"].includes(action)) body.notes = reason;
      if (action === "close") {
        body.closureReasonCode = closure;
        body.resolution = (
          {
            InsufficientEvidence: "Unknown",
            ConflictingEvidence: "Disputed",
            FraudConfirmed: "Fraud",
            LegitimateConfirmed: "Legitimate",
          } as Record<string, string>
        )[closure];
        if (evidenceRef || evidenceDescription)
          body.evidence = [
            {
              sourceType: evidenceSource,
              reference: evidenceRef,
              description: evidenceDescription,
              capturedAt: detail.serverNow,
            },
          ];
      }
      operation = { command: action, body };
      pending.current = operation;
    }
    setBusy(true);
    setError("");
    try {
      const result = await alertCommand(id, operation.command, operation.body);
      setDetail((old) =>
        old && result.alert.version >= old.alert.version
          ? { ...old, alert: result.alert, allowedActions: [] }
          : old,
      );
      pending.current = null;
      setUncertain(false);
      setAction("");
      setReason("");
      setEvidenceRef("");
      setEvidenceDescription("");
      setRefresh((n) => n + 1);
      await onChanged();
    } catch (e) {
      if (e instanceof ApiError && e.status < 500) {
        pending.current = null;
        setUncertain(false);
        if (e.status === 409) {
          setAction("");
          setRefresh((n) => n + 1);
          setError(
            "Cảnh báo đã thay đổi. Đã yêu cầu tải lại; hãy kiểm tra trước khi thao tác tiếp. Ghi chú nháp vẫn được giữ.",
          );
        } else setError(e.message);
      } else {
        setUncertain(true);
        setStale(true);
        setError(
          "Chưa xác định kết quả thao tác. Có thể thử lại chính lệnh cũ bằng nút bên dưới; không gửi quyết định mới.",
        );
      }
    } finally {
      setBusy(false);
    }
  }
  const a = detail?.alert;
  const staleNow = stale || now - lastSync > 30000;
  const needsReason =
    !!action && !["claim", "start", "resume"].includes(action);
  const confirmed =
    closure === "FraudConfirmed" || closure === "LegitimateConfirmed";
  return (
    <dialog
      ref={dialog}
      className="modal detail alert-dialog"
      aria-labelledby="alert-title"
      onCancel={(e) => {
        if (busy || uncertain) e.preventDefault();
        else onClose();
      }}
    >
      <div className="modal-heading">
        <div>
          <h2 id="alert-title">Xử lý cảnh báo</h2>
          <p>{id}</p>
        </div>
        <button
          disabled={busy || uncertain}
          onClick={onClose}
          aria-label="Đóng cửa sổ"
        >
          ×
        </button>
      </div>
      {error && (
        <div className="error" role="alert">
          {error}
        </div>
      )}
      {syncError && (
        <div className="error" role="alert">
          Lỗi đồng bộ: {syncError}
        </div>
      )}
      {uncertain && (
        <div className="notice">
          Giữ nguyên requestId cho lần thử lại.
          <button
            className="button"
            disabled={busy}
            onClick={() => void send(true)}
          >
            Thử lại lệnh cũ
          </button>
          <button
            className="button"
            disabled={busy}
            onClick={() => {
              pending.current = null;
              setUncertain(false);
              setAction("");
              setRefresh((n) => n + 1);
            }}
          >
            Đối soát trạng thái, bỏ lệnh chờ
          </button>
        </div>
      )}
      {a && detail && (
        <>
          <p className="alert-sync" role="status">
            {lastSync
              ? `Đồng bộ: ${displayDate(new Date(lastSync).toISOString())}`
              : "Chưa đồng bộ"}
            {staleNow && <strong> · Dữ liệu có thể đã cũ</strong>}
          </p>
          <div className="detail-summary">
            <div>
              <h3>
                {stateLabels[a.status]} · {a.priority}
              </h3>
              <p>Phụ trách: {detail.ownerName ?? "Chưa nhận"}</p>
              <p>
                Vòng xử lý {a.handlingEpisode} · Phiên bản {a.version}
              </p>
            </div>
            <span className="badge">
              {a.status === "Closed"
                ? resolutionLabels[a.resolution]
                : "Chưa kết luận"}
            </span>
          </div>
          <p className="helper">
            Hạn đóng: {displayDate(a.dueAt)} · SLA liên tục 24/7
            <br />
            {a.status === "Closed"
              ? `Đóng: ${displayDate(a.closedAt!)} · ${a.closedWithinSla ? "Trong SLA" : "Trễ SLA"}`
              : new Date(a.dueAt).getTime() <=
                  new Date(detail.serverNow).getTime() +
                    Math.max(0, now - lastSync)
                ? "Đã quá SLA"
                : "Chưa quá SLA"}
            <br />
            Nguồn: {a.sourceType} · Ưu tiên: {a.priorityReason}
          </p>
          {a.waitingUntil && (
            <p>
              Chờ đến {displayDate(a.waitingUntil)}: {a.waitingReason}
            </p>
          )}
          {a.escalationReason && <p>Chuyển cấp: {a.escalationReason}</p>}
          {a.closureNotes && <p>Lý do đóng: {a.closureNotes}</p>}
          {a.status === "Closed" && a.evidenceJson !== "[]" && (
            <details>
              <summary>Bằng chứng tham chiếu khi đóng</summary>
              <EvidenceList json={a.evidenceJson} />
            </details>
          )}
          {evaluation && (
            <details>
              <summary>
                Giao dịch và đánh giá gốc: {a.transactionId.slice(0, 8)}
              </summary>
              <p className="helper">
                {evaluation.userId} · {evaluation.merchant} ·{" "}
                {money(evaluation.amount)} · {evaluation.decision} ·{" "}
                {evaluation.analysisStrategy === "RiskSignals" ? "Phân nhóm dấu hiệu" : percent(evaluation.finalScore ?? 0)}
                <br />
                Quyết định gốc được giữ nguyên. BLOCK chưa phải chặn tiền thật.
              </p>
              {evaluation.analysisStrategy === "RiskSignals" && <RiskFindings findings={evaluation.riskFindings} />}
              {evaluation.matches.map((m) => (
                <div className="match" key={m.code}>
                  <div>
                    <strong>{m.title}</strong>
                    <p>{m.evidence}</p>
                  </div>
                </div>
              ))}
              {evaluation.reviews.length > 0 && (
                <p className="notice">
                  Có review cũ, chưa xác minh theo quy trình Alert; không tự
                  dùng làm kết luận.
                </p>
              )}
            </details>
          )}
          {a.status !== "Closed" || detail.allowedActions.includes("reopen") ? (
            <form
              className="alert-command"
              onSubmit={(e) => {
                e.preventDefault();
                void send();
              }}
            >
              <label>
                Thao tác
                <select
                  aria-label="Thao tác"
                  value={action}
                  disabled={busy || uncertain}
                  onChange={(e) => setAction(e.target.value)}
                >
                  <option value="">Chọn thao tác…</option>
                  {detail.allowedActions.map((c) => (
                    <option key={c} value={c}>
                      {actionLabels[c]}
                    </option>
                  ))}
                </select>
              </label>
              {needsReason && (
                <label>
                  {action === "close"
                    ? "Căn cứ / lý do đóng"
                    : "Lý do / ghi chú"}
                  <textarea
                    required
                    minLength={5}
                    maxLength={1000}
                    value={reason}
                    onChange={(e) => setReason(e.target.value)}
                    placeholder="Tối thiểu 5 ký tự; không nhập mật khẩu hoặc dữ liệu cá nhân thô"
                  />
                </label>
              )}
              {["assign", "reopen"].includes(action) && (
                <label>
                  Người phụ trách
                  <select
                    required
                    value={owner}
                    onChange={(e) => setOwner(e.target.value)}
                  >
                    <option value="">Chọn tài khoản</option>
                    {users.map((u) => (
                      <option key={u.id} value={u.id}>
                        {u.username} ({u.role})
                      </option>
                    ))}
                  </select>
                </label>
              )}
              {action === "priority" && (
                <label>
                  Ưu tiên mới
                  <select
                    value={priority}
                    onChange={(e) => setPriority(e.target.value)}
                  >
                    {["P1", "P2", "P3"].map((p) => (
                      <option key={p}>{p}</option>
                    ))}
                  </select>
                </label>
              )}
              {action === "wait" && (
                <>
                  <label>
                    Loại chờ
                    <select
                      value={waitingType}
                      onChange={(e) => setWaitingType(e.target.value)}
                    >
                      <option value="ExternalInformation">
                        Thông tin bổ sung
                      </option>
                      <option value="InternalReview">Đánh giá nội bộ</option>
                      <option value="Other">Khác</option>
                    </select>
                  </label>
                  <label>
                    Hẹn tiếp tục (giờ máy hiện tại, tối đa 7 ngày)
                    <input
                      type="datetime-local"
                      required
                      value={until}
                      onChange={(e) => setUntil(e.target.value)}
                    />
                  </label>
                  <p className="helper">
                    Waiting vẫn tính SLA. Hết hẹn hoặc SLA sẽ chuyển cấp.
                  </p>
                </>
              )}
              {action === "close" && (
                <>
                  <label>
                    Kết luận và lý do
                    <select
                      value={closure}
                      onChange={(e) => setClosure(e.target.value)}
                    >
                      <option value="InsufficientEvidence">
                        Chưa đủ bằng chứng — Unknown
                      </option>
                      <option value="ConflictingEvidence">
                        Bằng chứng mâu thuẫn — Disputed
                      </option>
                      <option value="FraudConfirmed">
                        Có căn cứ gian lận — Fraud
                      </option>
                      <option value="LegitimateConfirmed">
                        Có căn cứ hợp lệ — Legitimate
                      </option>
                    </select>
                  </label>
                  <p className="helper">
                    Không bị ép chọn hợp lệ khi chưa rõ. Kết luận xử lý không tự
                    chuyển sang huấn luyện AI.
                  </p>
                  {confirmed && (
                    <fieldset>
                      <legend>Bằng chứng tham chiếu bắt buộc</legend>
                      <label>
                        Nguồn
                        <select
                          value={evidenceSource}
                          onChange={(e) => setEvidenceSource(e.target.value)}
                        >
                          <option value="InternalRecord">Hồ sơ nội bộ</option>
                          <option value="ManualInvestigation">
                            Điều tra thủ công
                          </option>
                          <option value="Other">Khác</option>
                        </select>
                      </label>
                      <label>
                        Mã tham chiếu
                        <input
                          required
                          maxLength={300}
                          value={evidenceRef}
                          onChange={(e) => setEvidenceRef(e.target.value)}
                        />
                      </label>
                      <label>
                        Mô tả căn cứ
                        <textarea
                          required
                          minLength={5}
                          maxLength={1000}
                          value={evidenceDescription}
                          onChange={(e) =>
                            setEvidenceDescription(e.target.value)
                          }
                        />
                      </label>
                    </fieldset>
                  )}
                </>
              )}
              <button
                className="button primary"
                disabled={
                  !action ||
                  !detail.allowedActions.includes(action) ||
                  busy ||
                  uncertain ||
                  staleNow ||
                  (needsReason && reason.trim().length < 5)
                }
              >
                {" "}
                {busy
                  ? "Đang lưu…"
                  : (actionLabels[action] ?? "Thực hiện")}{" "}
              </button>
            </form>
          ) : null}
          <h3>Lịch sử xử lý</h3>
          {events.map((e) => (
            <article className="review-entry" key={e.id}>
              <strong>
                {eventLabels[e.type] ?? e.type} · v{e.version}
              </strong>
              <p>{e.reason}</p>
              <small>
                {e.actorUserId ? `Tài khoản #${e.actorUserId}` : "Hệ thống"} ·{" "}
                {displayDate(e.at)}
              </small>
              <details>
                <summary>Trước / sau thao tác</summary>
                <EventChanges entry={e} />
              </details>
            </article>
          ))}
          <div className="pagination">
            <span>
              {timelineTotal} sự kiện · Trang {timelinePage}
            </span>
            <div>
              <button
                className="button"
                disabled={timelinePage === 1}
                onClick={() => setTimelinePage((p) => p - 1)}
              >
                Trước
              </button>
              <button
                className="button"
                disabled={timelinePage * 20 >= timelineTotal}
                onClick={() => setTimelinePage((p) => p + 1)}
              >
                Sau
              </button>
            </div>
          </div>
        </>
      )}
    </dialog>
  );
}
