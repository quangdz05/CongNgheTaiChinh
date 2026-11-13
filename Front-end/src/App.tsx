import {
  AlertTriangle,
  ArrowLeftRight,
  ArrowUpRight,
  Ban,
  BrainCircuit,
  CheckCircle2,
  ChevronRight,
  Clock3,
  Download,
  LayoutDashboard,
  LoaderCircle,
  LogOut,
  Network,
  Play,
  Plus,
  RefreshCw,
  ScanLine,
  ScrollText,
  Search,
  ShieldCheck,
  SlidersHorizontal,
  X,
} from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { Badge, Empty } from "./components/common";
import { AiPanel } from "./features/ai/_components/ai-panel";
import { AlertQueue } from "./features/alerts/_components/alert-queue";
import { type AlertStats } from "./features/alerts/_services/services";
import { Login } from "./features/auth/_components/login-form";
import { Rules } from "./features/rules/_components/rules-form";
import { Detail } from "./features/transactions/_components/transaction-detail";
import { TransactionForm } from "./features/transactions/_components/transaction-form";
import {
  api,
  money,
  percent,
  post,
  resetApiSession,
  type Evaluation,
  type Policy,
  type Stats,
  type TransactionSummary,
} from "./shared/services/api";
const pages = [
  ["overview", "Tổng quan", LayoutDashboard],
  ["transactions", "Giao dịch", ArrowLeftRight],
  ["alerts", "Hàng đợi cảnh báo", ScanLine],
  ["rules", "Luật nghiệp vụ", SlidersHorizontal],
  ["ai", "Mô hình AI", BrainCircuit],
  ["network", "Liên kết thiết bị", Network],
  ["audit", "Nhật ký hệ thống", ScrollText],
] as const;
type Page = (typeof pages)[number][0];
type User = { username: string; role: string };
export default function App() {
  const [user, setUser] = useState<User | null>(null);
  const [ready, setReady] = useState(false);
  const logout = useCallback(() => {
    sessionStorage.removeItem("fs-token");
    resetApiSession();
    setUser(null);
  }, []);
  useEffect(() => {
    let active = true;
    window.addEventListener("fs-logout", logout);
    if (sessionStorage.getItem("fs-token"))
      api<User>("/auth/me")
        .then((u) => {
          if (active) setUser(u);
        })
        .catch(() => {})
        .finally(() => {
          if (active) setReady(true);
        });
    else setReady(true);
    return () => {
      active = false;
      window.removeEventListener("fs-logout", logout);
    };
  }, [logout]);
  if (!ready)
    return (
      <div className="splash">
        <LoaderCircle className="spin" /> Đang kết nối FraudShield…
      </div>
    );
  if (!user)
    return (
      <Login
        onLogin={(u) => {
          resetApiSession();
          setUser(u);
        }}
      />
    );
  return <Workspace key={user.username} user={user} onLogout={logout} />;
}
function Workspace({ user, onLogout }: { user: User; onLogout: () => void }) {
  const [page, setPage] = useState<Page>("overview"),
    [stats, setStats] = useState<Stats | null>(null),
    [rows, setRows] = useState<TransactionSummary[]>([]),
    [total, setTotal] = useState(0),
    [pageNo, setPageNo] = useState(1),
    [search, setSearch] = useState(""),
    [filter, setFilter] = useState(""),
    [policy, setPolicy] = useState<Policy | null>(null),
    [aiStatus, setAiStatus] = useState("Đang kiểm tra"),
    [metrics, setMetrics] = useState<any>(null),
    [audit, setAudit] = useState<any[]>([]),
    [selected, setSelected] = useState<Evaluation | null>(null),
    [formOpen, setFormOpen] = useState(false),
    [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [toast, setToast] = useState("");
  const [revision, setRevision] = useState(0);
  const [transactionsLoading, setTransactionsLoading] = useState(true);
  const [alertStats, setAlertStats] = useState<AlertStats | null>(null);
  useEffect(() => {
    let active = true;
    let controller: AbortController | null = null;
    const refresh = async () => {
      controller?.abort();
      controller = new AbortController();
      const request = controller;
      try {
        const result = await api<AlertStats>("/alerts/stats", {
          signal: request.signal,
        });
        if (active && !request.signal.aborted) setAlertStats(result);
      } catch {
        /* Queue shows its own sync errors; keep last known counters. */
      }
    };
    void refresh();
    const timer = setInterval(() => {
      if (!document.hidden) void refresh();
    }, 10000);
    const focus = () => {
      if (!document.hidden) void refresh();
    };
    window.addEventListener("focus", focus);
    window.addEventListener("online", focus);
    document.addEventListener("visibilitychange", focus);
    return () => {
      active = false;
      controller?.abort();
      clearInterval(timer);
      window.removeEventListener("focus", focus);
      window.removeEventListener("online", focus);
      document.removeEventListener("visibilitychange", focus);
    };
  }, [revision]);
  const detailController = useRef<AbortController | null>(null);
  const toastTimer = useRef<ReturnType<typeof setTimeout>>();
  useEffect(
    () => () => {
      detailController.current?.abort();
      clearTimeout(toastTimer.current);
    },
    [],
  );
  const notify = (msg: string) => {
    setToast(msg);
    clearTimeout(toastTimer.current);
    toastTimer.current = setTimeout(() => setToast(""), 5000);
  };
  const load = useCallback(async () => {
    setRevision((n) => n + 1);
  }, []);
  useEffect(() => {
    if (!["overview", "transactions", "ai"].includes(page)) return;
    const controller = new AbortController();
    setTransactionsLoading(true);
    setRows([]);
    const id = setTimeout(() => {
      setError("");
      api<{ items: TransactionSummary[]; total: number }>(
        `/transactions?page=${pageNo}&size=12&search=${encodeURIComponent(search)}&decision=${filter}`,
        { signal: controller.signal },
      )
        .then((t) => {
          if (!controller.signal.aborted) {
            setRows(t.items);
            setTotal(t.total);
          }
        })
        .catch((e) => {
          if (!controller.signal.aborted) setError(e.message);
        })
        .finally(() => {
          if (!controller.signal.aborted) setTransactionsLoading(false);
        });
    }, 250);
    return () => {
      clearTimeout(id);
      controller.abort();
    };
  }, [pageNo, search, filter, page, revision]);
  useEffect(() => {
    const controller = new AbortController();
    const options = { signal: controller.signal };
    const reportError = (e: Error) => {
      if (!controller.signal.aborted) setError(e.message);
    };
    if (["overview", "network"].includes(page))
      api<Stats>("/dashboard", options)
        .then((s) => {
          if (!controller.signal.aborted) setStats(s);
        })
        .catch(reportError);
    if (["overview", "rules"].includes(page))
      api<Policy>("/rules", options)
        .then((p) => {
          if (!controller.signal.aborted) setPolicy(p);
        })
        .catch(reportError);
    api<{ status: string }>("/ai/status", options)
      .then((x) => {
        if (!controller.signal.aborted) setAiStatus(x.status);
      })
      .catch(() => {
        if (!controller.signal.aborted) setAiStatus("Unavailable");
      });
    if (page === "ai")
      api("/ai/metrics", options)
        .then((m) => {
          if (!controller.signal.aborted) setMetrics(m);
        })
        .catch(() => {
          if (!controller.signal.aborted) setMetrics(null);
        });
    if (page === "audit" && user.role === "Admin")
      api<any[]>("/audit", options)
        .then((a) => {
          if (!controller.signal.aborted) setAudit(a);
        })
        .catch(reportError);
    return () => controller.abort();
  }, [user.role, page, revision]);
  async function openDetail(id: string) {
    detailController.current?.abort();
    const controller = new AbortController();
    detailController.current = controller;
    try {
      const item = await api<Evaluation>("/transactions/" + id, {
        signal: controller.signal,
      });
      if (!controller.signal.aborted) setSelected(item);
    } catch (e) {
      if (!controller.signal.aborted) setError((e as Error).message);
    }
  }
  async function demo() {
    setBusy(true);
    setError("");
    try {
      const r = await post<{ count: number }>("/demo", { mode: "Hybrid" });
      notify(
        `Đã tạo ${r.count} giao dịch mô phỏng. Không có giao dịch tiền thật.`,
      );
      await load();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  async function exportCsv() {
    try {
      const r = await fetch("/api/transactions/export", {
        headers: {
          Authorization: "Bearer " + sessionStorage.getItem("fs-token"),
        },
      });
      if (!r.ok) throw new Error("Không thể xuất dữ liệu");
      const u = URL.createObjectURL(await r.blob());
      const a = document.createElement("a");
      a.href = u;
      a.download = "fraudshield-transactions.csv";
      a.click();
      URL.revokeObjectURL(u);
    } catch (e) {
      setError((e as Error).message);
    }
  }
  const visibleRows = rows;
  return (
    <div className="app">
      <aside>
        <a className="brand" href="#" onClick={(e) => e.preventDefault()}>
          <span className="brand-mark">
            <ShieldCheck size={26} />
          </span>
          <span>
            fraudshield<small>RISK OPERATIONS</small>
          </span>
        </a>
        <div className="workspace">
          <span className="workspace-icon">FS</span>
          <div>
            FinTech Workspace<small>Môi trường thử nghiệm</small>
          </div>
          <span className="dot" />
        </div>
        <div className="nav-label">KHÔNG GIAN LÀM VIỆC</div>
        <nav>
          {pages
            .filter((p) => p[0] !== "audit" || user.role === "Admin")
            .map(([key, label, Icon]) => (
              <button
                key={key}
                className={page === key ? "active" : ""}
                onClick={() => {
                  setPage(key);
                  setPageNo(1);
                  setFilter("");
                  setSearch("");
                }}
              >
                <Icon size={18} />
                {label}
                {key === "alerts" && !!alertStats?.openAlerts && (
                  <b>{alertStats.openAlerts}</b>
                )}
              </button>
            ))}
        </nav>
        <div className="sidebar-bottom">
          <div className="secure-note">
            <ShieldCheck size={18} />
            <div>
              Prototype học phần<small>Dữ liệu mô phỏng · VND</small>
            </div>
          </div>
          <button
            className="user"
            onClick={() => {
              onLogout();
            }}
          >
            <span className="avatar">
              {user.username.slice(0, 2).toUpperCase()}
            </span>
            <span>
              {user.username}
              <small>{user.role}</small>
            </span>
            <LogOut size={17} />
          </button>
        </div>
      </aside>
      <main>
        <header>
          <div className="breadcrumb">
            Workspace <ChevronRight size={13} />
            <strong>{pages.find((p) => p[0] === page)?.[1]}</strong>
          </div>
          <div className="header-right">
            <span className="environment">
              <i /> Local workspace
            </span>
            <span className="avatar small">
              {user.username[0].toUpperCase()}
            </span>
          </div>
        </header>
        <div className="content">
          <div className="page-heading">
            <div>
              <div className="eyebrow">FRAUDSHIELD INTELLIGENCE</div>
              <h1>{pages.find((p) => p[0] === page)?.[1]}</h1>
              <p>
                {page === "overview"
                  ? "Quan sát giao dịch, giải thích rủi ro và đưa ra quyết định."
                  : page === "ai"
                    ? "Mô hình độc lập qua API. Kết quả gắn với dữ liệu và phiên bản."
                    : "Theo dõi dữ liệu và kiểm soát quyết định trong cùng một không gian."}
              </p>
            </div>
            <div className="actions">
              {page === "overview" && (
                <>
                  <button
                    className="button"
                    onClick={() => setPage("transactions")}
                  >
                    Xem giao dịch
                  </button>
                  <button className="button" onClick={() => setPage("alerts")}>
                    Xem cảnh báo
                  </button>
                </>
              )}
              {["overview", "transactions", "network", "audit"].includes(
                page,
              ) && (
                <button
                  className="button"
                  onClick={() => void load()}
                  aria-label="Tải lại"
                >
                  <RefreshCw size={16} />
                </button>
              )}
              {page === "transactions" && (
                <button
                  className="button primary"
                  onClick={() => setFormOpen(true)}
                >
                  <Plus size={17} /> Tạo giao dịch
                </button>
              )}
            </div>
          </div>
          {page === "transactions" && (
            <details className="panel demo-panel">
              <summary className="demo-summary">
                <span className="demo-icon"><Play size={19} aria-hidden="true" /></span>
                <span className="demo-heading">
                  <strong>Dữ liệu mô phỏng</strong>
                  <span>Chuẩn bị các tình huống mẫu để khám phá hệ thống</span>
                </span>
                <span className="demo-count">18 giao dịch</span>
                <ChevronRight className="demo-chevron" size={18} aria-hidden="true" />
              </summary>
              <div className="demo-body">
                <div className="demo-description">
                  <span className="demo-label">LUẬT KẾT HỢP AI · HYBRID</span>
                  <p>Gồm giao dịch thông thường, giá trị cao, chuỗi liên tiếp và nhiều tài khoản dùng chung thiết bị.</p>
                  <small>Mỗi lần tạo thêm 18 giao dịch. Kết quả REVIEW hoặc BLOCK sẽ tạo cảnh báo.</small>
                </div>
                <button className="button primary demo-create" onClick={demo} disabled={busy}>
                  {busy ? (
                    <LoaderCircle size={16} className="spin" />
                  ) : (
                    <Play size={16} />
                  )}
                  {busy ? "Đang tạo bộ demo…" : "Tạo bộ demo 18 giao dịch"}
                </button>
              </div>
            </details>
          )}
          {error && (
            <div className="error" role="alert">
              <AlertTriangle size={18} />
              {error}
              <button onClick={() => setError("")} aria-label="Đóng lỗi">
                <X size={16} />
              </button>
            </div>
          )}
          {page === "overview" && (
            <>
              <div className="stats-grid">
                {[
                  {
                    label: "Giao dịch đã phân tích",
                    value: stats?.total ?? 0,
                    Icon: ArrowLeftRight,
                    meta: "Theo lịch sử lưu trữ",
                    tone: "blue",
                  },
                  {
                    label: "Đề xuất cho phép",
                    value: stats?.allow ?? 0,
                    Icon: CheckCircle2,
                    meta: "ALLOW · Theo chính sách",
                    tone: "green",
                  },
                  {
                    label: "Cảnh báo đang mở",
                    value: alertStats?.openAlerts ?? 0,
                    Icon: Clock3,
                    meta: `${alertStats?.overdue ?? 0} quá thời gian xử lí · gồm Waiting / Escalated`,
                    tone: "amber",
                  },
                  {
                    label: "Đề xuất chặn",
                    value: stats?.block ?? 0,
                    Icon: Ban,
                    meta: "BLOCK ≠ gian lận xác nhận",
                    tone: "red",
                  },
                ].map((x) => (
                  <section className="stat-card" key={x.label}>
                    <div>
                      <span>{x.label}</span>
                      <span className={"stat-icon " + x.tone}>
                        <x.Icon size={18} />
                      </span>
                    </div>
                    <strong>{x.value.toLocaleString("vi-VN")}</strong>
                    <small>{x.meta}</small>
                  </section>
                ))}
              </div>
              <div className="overview-grid">
                <section className="panel">
                  <div className="panel-heading">
                    <div>
                      <h2>Hoạt động giao dịch</h2>
                      <p>
                        Phân bố theo ngày UTC · Tối đa 10.000 bản ghi gần nhất
                      </p>
                    </div>
                    <span className="subtle">
                      {stats?.daily.length ?? 0} ngày
                    </span>
                  </div>
                  {stats?.daily.length ? (
                    <div className="chart">
                      {stats.daily.slice(-10).map((d) => (
                        <div className="chart-column" key={d.date}>
                          <div className="chart-value">{d.total}</div>
                          <div
                            className="bar-stack"
                            style={{
                              height: `${Math.max(6, (d.total / Math.max(...stats.daily.map((x) => x.total))) * 155)}px`,
                            }}
                          >
                            <div
                              style={{
                                height: `${(d.flagged / d.total) * 100}%`,
                              }}
                            />
                          </div>
                          <span>{d.date.slice(5)}</span>
                        </div>
                      ))}
                    </div>
                  ) : (
                    <Empty
                      text="Chưa có hoạt động"
                      detail="Tạo dữ liệu mô phỏng hoặc gửi giao dịch đầu tiên."
                    />
                  )}
                  <div className="legend">
                    <span>
                      <i />
                      Tổng giao dịch
                    </span>
                    <span>
                      <i />
                      Có cảnh báo
                    </span>
                  </div>
                </section>
                <section className="panel system-panel">
                  <div className="panel-heading">
                    <div>
                      <h2>Trạng thái hệ thống</h2>
                      <p>Các thành phần đang phục vụ</p>
                    </div>
                    <ShieldCheck size={21} />
                  </div>
                  <div className="system-row">
                    <span>ASP.NET Core API</span>
                    <span className="status-dot">Đã kết nối</span>
                  </div>
                  <div className="system-row">
                    <span>Bộ luật nghiệp vụ</span>
                    <strong>v{policy?.version ?? 1}</strong>
                  </div>
                  <div className="system-row">
                    <span>AI inference service</span>
                    <strong
                      className={
                        aiStatus === "Ready" ? "text-green" : "text-amber"
                      }
                    >
                      {aiStatus}
                    </strong>
                  </div>
                  <div className="system-row">
                    <span>Độ trễ lõi trung bình</span>
                    <strong>
                      {stats?.averageLatencyMs.toFixed(1) ?? "0"} ms
                    </strong>
                  </div>
                  <p className="helper">
                    Độ trễ gồm chờ xử lý, chưa gồm lưu DB và mạng. Nhãn gian lận
                    chỉ ghi nhận sau kiểm tra.
                  </p>
                </section>
              </div>
            </>
          )}
          {page === "alerts" && (
            <AlertQueue
              initialSearch={search}
              revision={revision}
              stats={alertStats}
              onChanged={load}
            />
          )}
          {(page === "overview" || page === "transactions") && (
            <section className="panel transaction-panel">
              <div className="panel-heading">
                <div>
                  <h2>Giao dịch gần đây</h2>
                  <p>Chọn một giao dịch để xem căn cứ và kết quả kiểm tra.</p>
                </div>
                {page === "transactions" && (
                  <button className="button" onClick={exportCsv}>
                    <Download size={15} /> Xuất CSV
                  </button>
                )}
              </div>
              <div className="toolbar">
                <label className="search">
                  <Search size={17} />
                  <input
                    aria-label="Tìm giao dịch"
                    placeholder="Tìm người dùng hoặc merchant…"
                    value={search}
                    onChange={(e) => {
                      setSearch(e.target.value);
                      setPageNo(1);
                    }}
                  />
                </label>
                <select
                  aria-label="Lọc quyết định"
                  value={filter}
                  onChange={(e) => {
                    setFilter(e.target.value);
                    setPageNo(1);
                  }}
                >
                  <option value="">Tất cả quyết định</option>
                  <option value="ALLOW">ALLOW</option>
                  <option value="REVIEW">REVIEW</option>
                  <option value="BLOCK">BLOCK</option>
                </select>
              </div>
              <div className="table-scroll">
                <table>
                  <thead>
                    <tr>
                      <th>GIAO DỊCH / NGƯỜI DÙNG</th>
                      <th>SỐ TIỀN</th>
                      <th>DẤU HIỆU / ĐÁNH GIÁ</th>
                      <th>QUYẾT ĐỊNH</th>
                      <th>THỜI GIAN</th>
                      <th />
                    </tr>
                  </thead>
                  <tbody>
                    {visibleRows.map((t) => (
                      <tr
                        key={t.id}
                        onClick={() => void openDetail(t.id)}
                        tabIndex={0}
                        onKeyDown={(e) =>
                          e.key === "Enter" && void openDetail(t.id)
                        }
                      >
                        <td>
                          <strong>{t.userId}</strong>
                          <small>
                            {t.merchant} · {t.id.slice(0, 8)}
                          </small>
                        </td>
                        <td className="amount">{money(t.amount)}</td>
                        <td>
                          {t.analysisStrategy === "RiskSignals" ? (
                            <div className="risk-tags">
                              {[
                                ...new Set(
                                  (t.riskFindings ?? [])
                                    .filter((f) => f.action === "REVIEW")
                                    .map((f) => f.group),
                                ),
                              ].map((g) => (
                                <span className="badge review" key={g}>
                                  {{
                                    frequency: "Tần suất",
                                    value: "Giá trị",
                                    context: "Truy cập",
                                    linkage: "Liên kết tài khoản",
                                    model: "Tín hiệu AI",
                                    data_quality: "Thiếu đánh giá AI",
                                  }[g] ?? g}
                                </span>
                              ))}
                              {!(t.riskFindings ?? []).some(
                                (f) => f.action === "REVIEW",
                              ) && <small>Chưa có cờ kiểm tra</small>}
                              {(t.riskFindings ?? []).some(
                                (f) => f.code === "LIMITED_HISTORY",
                              ) && <small>Chưa đủ lịch sử</small>}
                            </div>
                          ) : (
                            <>
                              <small>Chấm điểm cũ</small>
                              <span className="score">
                                <span className="score-track">
                                  <i
                                    style={{
                                      width: percent(t.finalScore ?? 0),
                                      background:
                                        t.decision === "BLOCK"
                                          ? "#db6464"
                                          : t.decision === "REVIEW"
                                            ? "#d7a03d"
                                            : "#36a88a",
                                    }}
                                  />
                                </span>
                                {percent(t.finalScore ?? 0)}
                              </span>
                            </>
                          )}
                        </td>
                        <td>
                          <Badge value={t.decision} />
                        </td>
                        <td className="muted">
                          {new Date(t.timestamp).toLocaleString("vi-VN")}
                        </td>
                        <td>
                          <ArrowUpRight size={17} />
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              {transactionsLoading && (
                <div className="empty" role="status">
                  <LoaderCircle className="spin" size={26} />
                  <strong>Đang tải giao dịch…</strong>
                </div>
              )}
              {!transactionsLoading && !visibleRows.length && (
                <>
                  <Empty
                    text="Chưa có giao dịch phù hợp"
                    detail={
                      page === "transactions"
                        ? "Thử đổi bộ lọc hoặc nhập giao dịch bằng nút Tạo giao dịch."
                        : "Mở trang Giao dịch để tra cứu hoặc tạo giao dịch mới."
                    }
                  />
                  {page === "overview" && (
                    <div style={{ textAlign: "center", paddingBottom: 24 }}>
                      <button
                        className="button"
                        onClick={() => setPage("transactions")}
                      >
                        Xem giao dịch
                      </button>
                    </div>
                  )}
                </>
              )}
              <div className="pagination">
                <span>
                  {total} giao dịch phù hợp · Trang {pageNo}
                </span>
                <div>
                  <button
                    className="button"
                    disabled={transactionsLoading || pageNo === 1}
                    onClick={() => setPageNo((n) => n - 1)}
                  >
                    Trước
                  </button>
                  <button
                    className="button"
                    disabled={transactionsLoading || pageNo * 12 >= total}
                    onClick={() => setPageNo((n) => n + 1)}
                  >
                    Sau
                  </button>
                </div>
              </div>
            </section>
          )}
          {page === "rules" && policy && (
            <Rules
              modelMetrics={metrics}
              policy={policy}
              canEdit={user.role === "Admin"}
              onSaved={(p) => {
                setPolicy(p);
                notify("Đã lưu phiên bản luật " + p.version);
              }}
            />
          )}
          {page === "network" && (
            <section className="panel">
              <div className="panel-heading">
                <div>
                  <h2>Thiết bị có liên kết</h2>
                  <p>
                    Quan hệ trong tối đa 10.000 giao dịch gần nhất. Cùng thiết
                    bị là tín hiệu cần xác minh.
                  </p>
                </div>
              </div>
              {stats?.rings.length ? (
                stats.rings.map((r) => (
                  <div className="ring" key={r.device}>
                    <Network size={30} />
                    <div>
                      <strong>Device {r.device}…</strong>
                      <p>{r.transactions} giao dịch</p>
                      <div className="user-chips">
                        {r.users.map((u) => (
                          <span key={u}>{u}</span>
                        ))}
                      </div>
                    </div>
                  </div>
                ))
              ) : (
                <Empty
                  text="Chưa có nhóm liên kết"
                  detail="Chưa có dữ liệu liên kết để hiển thị. Có thể chuẩn bị dữ liệu mô phỏng tại trang Giao dịch."
                />
              )}
            </section>
          )}
          {page === "ai" && (
            <AiPanel status={aiStatus} metrics={metrics} rows={rows} />
          )}
          {page === "audit" && user.role === "Admin" && (
            <section className="panel">
              <div className="panel-heading">
                <h2>Lịch sử thay đổi và kiểm tra</h2>
              </div>
              {audit.map((a) => (
                <div className="audit-row" key={a.id}>
                  <ScrollText size={18} />
                  <div>
                    <strong>{a.action}</strong>
                    <p>
                      {a.actor} · {new Date(a.at).toLocaleString("vi-VN")}
                    </p>
                    <code>{a.details}</code>
                  </div>
                </div>
              ))}
              {!audit.length && (
                <Empty
                  text="Chưa có sự kiện"
                  detail="Thay đổi luật và đánh giá giao dịch được ghi tại đây."
                />
              )}
            </section>
          )}
          <footer>
            FraudShield · Clean Architecture / React / AI API
            <span>Dữ liệu mô phỏng · Quyết định hỗ trợ kiểm soát</span>
          </footer>
        </div>
      </main>
      {toast && (
        <div className="toast" role="status">
          <CheckCircle2 size={18} />
          {toast}
        </div>
      )}
      {formOpen && (
        <TransactionForm
          mode="RulesOnly"
          onClose={() => setFormOpen(false)}
          onSuccess={(t) => {
            setFormOpen(false);
            setPage("transactions");
            setPageNo(1);
            setSearch("");
            setFilter("");
            setSelected(t);
            notify("Đã tạo và lưu giao dịch của bạn.");
            void load();
          }}
        />
      )}
      {selected && (
        <Detail
          item={selected}
          onClose={() => setSelected(null)}
          onOpenAlerts={() => {
            setSearch(selected.id);
            setPage("alerts");
            setSelected(null);
          }}
        />
      )}
    </div>
  );
}
