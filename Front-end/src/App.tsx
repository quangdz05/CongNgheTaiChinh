import {
  Activity,
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
import { useCallback, useEffect, useState } from "react";
import { Badge, Empty } from "./components/common";
import { AiPanel } from "./features/ai/_components/ai-panel";
import { Login } from "./features/auth/_components/login-form";
import { Rules } from "./features/rules/_components/rules-form";
import { Detail } from "./features/transactions/_components/transaction-detail";
import { TransactionForm } from "./features/transactions/_components/transaction-form";
import {
  api,
  money,
  percent,
  post,
  type Evaluation,
  type Policy,
  type Stats,
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
  const [user, setUser] = useState<User | null>(null),
    [ready, setReady] = useState(false),
    [page, setPage] = useState<Page>("overview"),
    [mode, setMode] = useState("RulesOnly"),
    [stats, setStats] = useState<Stats | null>(null),
    [rows, setRows] = useState<Evaluation[]>([]),
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
  const notify = (msg: string) => {
    setToast(msg);
    setTimeout(() => setToast(""), 5000);
  };
  useEffect(() => {
    const logout = () => setUser(null);
    window.addEventListener("fs-logout", logout);
    if (sessionStorage.getItem("fs-token"))
      api<User>("/auth/me")
        .then(setUser)
        .catch(() => {})
        .finally(() => setReady(true));
    else setReady(true);
    return () => window.removeEventListener("fs-logout", logout);
  }, []);
  const load = useCallback(async () => {
    if (!user) return;
    setError("");
    try {
      const [s, t, p] = await Promise.all([
        api<Stats>("/dashboard"),
        api<{ items: Evaluation[]; total: number }>(
          `/transactions?page=${pageNo}&size=12&search=${encodeURIComponent(search)}&decision=${page === "alerts" && !filter ? "ALERTS" : filter}`,
        ),
        api<Policy>("/rules"),
      ]);
      setStats(s);
      setRows(t.items);
      setTotal(t.total);
      setPolicy(p);
    } catch (e) {
      setError((e as Error).message);
    }
  }, [user, pageNo, search, filter, page]);
  useEffect(() => {
    const id = setTimeout(() => {
      void load();
    }, 250);
    return () => clearTimeout(id);
  }, [load]);
  useEffect(() => {
    if (!user) return;
    api<{ status: string }>("/ai/status")
      .then((x) => setAiStatus(x.status))
      .catch(() => setAiStatus("Unavailable"));
    if (page === "ai")
      api("/ai/metrics")
        .then(setMetrics)
        .catch(() => setMetrics(null));
    if (page === "audit" && user.role === "Admin")
      api<any[]>("/audit")
        .then(setAudit)
        .catch((e) => setError(e.message));
  }, [user, page, busy]);
  async function demo() {
    setBusy(true);
    setError("");
    try {
      const r = await post<{ count: number }>("/demo", { mode });
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
  if (!ready)
    return (
      <div className="splash">
        <LoaderCircle className="spin" /> Đang kết nối FraudShield…
      </div>
    );
  if (!user) return <Login onLogin={setUser} />;
  const visibleRows =
    page === "alerts" ? rows.filter((r) => r.decision !== "ALLOW") : rows;
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
                {key === "alerts" && !!stats?.pending && <b>{stats.pending}</b>}
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
              sessionStorage.removeItem("fs-token");
              setUser(null);
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
              <button className="button" onClick={demo} disabled={busy}>
                {busy ? (
                  <LoaderCircle size={16} className="spin" />
                ) : (
                  <Play size={16} />
                )}
                Tạo bộ demo
              </button>
              <button
                className="button"
                onClick={() => void load()}
                aria-label="Tải lại"
              >
                <RefreshCw size={16} />
              </button>
              <button
                className="button primary"
                onClick={() => setFormOpen(true)}
              >
                <Plus size={17} /> Phân tích giao dịch
              </button>
            </div>
          </div>
          <div className="mode-bar">
            <div>
              <span className="mode-icon">
                <Activity size={17} />
              </span>
              <span>
                <strong>
                  {mode === "RulesOnly" ? "Chế độ giữa kỳ" : "Chế độ cuối kỳ"}
                </strong>
                <small>
                  {mode === "RulesOnly"
                    ? "Chỉ dùng luật nghiệp vụ · Không gọi AI"
                    : "Luật + AI API · Khi AI lỗi chuyển kiểm tra"}
                </small>
              </span>
            </div>
            <select
              aria-label="Chế độ phân tích"
              value={mode}
              onChange={(e) => setMode(e.target.value)}
            >
              <option value="RulesOnly">Rules Only</option>
              <option value="Hybrid">Hybrid + AI</option>
            </select>
          </div>
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
                    label: "Chờ kiểm tra",
                    value: stats?.pending ?? 0,
                    Icon: Clock3,
                    meta: "Cảnh báo chưa có đánh giá",
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
                    Độ trễ chưa bao gồm thời gian lưu DB và mạng. Nhãn gian lận
                    chỉ ghi nhận sau kiểm tra.
                  </p>
                  <button
                    className="button wide"
                    onClick={demo}
                    disabled={busy}
                  >
                    {busy ? (
                      <LoaderCircle size={16} className="spin" />
                    ) : (
                      <Play size={16} />
                    )}{" "}
                    Tạo bộ demo 18 giao dịch
                  </button>
                </section>
              </div>
            </>
          )}
          {(page === "overview" ||
            page === "transactions" ||
            page === "alerts") && (
            <section className="panel transaction-panel">
              <div className="panel-heading">
                <div>
                  <h2>
                    {page === "alerts"
                      ? "Danh sách cảnh báo"
                      : "Giao dịch gần đây"}
                  </h2>
                  <p>Chọn một giao dịch để xem căn cứ và kết quả kiểm tra.</p>
                </div>
                <button className="button" onClick={exportCsv}>
                  <Download size={15} /> Xuất CSV
                </button>
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
                      <th>ĐIỂM RỦI RO</th>
                      <th>QUYẾT ĐỊNH</th>
                      <th>THỜI GIAN</th>
                      <th />
                    </tr>
                  </thead>
                  <tbody>
                    {visibleRows.map((t) => (
                      <tr
                        key={t.id}
                        onClick={() => setSelected(t)}
                        tabIndex={0}
                        onKeyDown={(e) => e.key === "Enter" && setSelected(t)}
                      >
                        <td>
                          <strong>{t.userId}</strong>
                          <small>
                            {t.merchant} · {t.id.slice(0, 8)}
                          </small>
                        </td>
                        <td className="amount">{money(t.amount)}</td>
                        <td>
                          <span className="score">
                            <span className="score-track">
                              <i
                                style={{
                                  width: percent(t.finalScore),
                                  background:
                                    t.decision === "BLOCK"
                                      ? "#db6464"
                                      : t.decision === "REVIEW"
                                        ? "#d7a03d"
                                        : "#36a88a",
                                }}
                              />
                            </span>
                            {percent(t.finalScore)}
                          </span>
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
              {!visibleRows.length && (
                <Empty
                  text="Chưa có giao dịch phù hợp"
                  detail={
                    page === "alerts"
                      ? "Không có cảnh báo trong trang đang xem. Lọc REVIEW hoặc BLOCK để tìm chính xác."
                      : "Bắt đầu bằng một giao dịch hoặc bộ dữ liệu mô phỏng."
                  }
                />
              )}
              <div className="pagination">
                <span>
                  {total} giao dịch phù hợp · Trang {pageNo}
                  {page === "alerts" ? " · Ẩn ALLOW trong trang" : ""}
                </span>
                <div>
                  <button
                    className="button"
                    disabled={pageNo === 1}
                    onClick={() => setPageNo((n) => n - 1)}
                  >
                    Trước
                  </button>
                  <button
                    className="button"
                    disabled={pageNo * 12 >= total}
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
                  detail="Bộ demo có tình huống nhiều tài khoản dùng chung thiết bị."
                />
              )}
            </section>
          )}
          {page === "ai" && (
            <AiPanel status={aiStatus} metrics={metrics} rows={rows} />
          )}
          {page === "audit" && (
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
          mode={mode}
          onClose={() => setFormOpen(false)}
          onSuccess={(t) => {
            setFormOpen(false);
            setSelected(t);
            void load();
          }}
        />
      )}
      {selected && (
        <Detail
          item={selected}
          onClose={() => setSelected(null)}
          onRefresh={async () => {
            setSelected(await api<Evaluation>("/transactions/" + selected.id));
            await load();
          }}
        />
      )}
    </div>
  );
}
