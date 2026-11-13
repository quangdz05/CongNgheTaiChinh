import { useCallback, useEffect, useState } from "react";
import { api, money, post } from "../../../shared/services/api";

const base = "/ai/workspace";
type Report = {
  modelVersion: string; datasetSource: string; trainedAt: string;
  split: { train: number; validation: number; test: number; method: string };
  testMetrics: { precision: number; recall: number; f1: number; prAuc: number; rocAuc: number; threshold: number; confusionMatrix: number[][] };
  originalRows?: number; duplicatesRemoved?: number;
};
type Dataset = {
  id: string; name: string; status: string; rowCount: number; labelledCount: number;
  fraudCount: number; containsSynthetic: boolean; error: string | null; metrics: Report | null;
  preview?: Row[];
};
type Row = { userId: string; amount: number; currency: string; merchant: string; category: string; country: string; deviceId: string; ipAddress: string; timestamp: string; label: number | null; origin?: string };
type Sample = { id: number; features: Record<string, number>; label: number };
const statusLabels: Record<string, string> = { draft: "Bản nháp", training: "Đang huấn luyện", ready: "Đã huấn luyện", failed: "Chưa huấn luyện được" };

type ModelOption = { datasetId: string | null; name: string; modelVersion: string | null };
type HybridModel = { active: ModelOption; canManage: boolean; options: ModelOption[] };

export function HybridModelSelector({ onApplied }: { onApplied: () => void }) {
  const [state, setState] = useState<HybridModel | null>(null);
  const [selected, setSelected] = useState("");
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const load = useCallback(async () => {
    const result = await api<HybridModel>(base + "/hybrid-model");
    setState(result);
    setSelected(result.active.datasetId ?? "");
  }, []);
  useEffect(() => { void load().catch((e: Error) => setError(e.message)); }, [load]);
  const options = state?.options ?? [];
  const selectedActive = state?.active.datasetId && !options.some(o => o.datasetId === state.active.datasetId);
  return <section className="panel workspace-panel" style={{ marginBottom: 18 }}>
    <h2>Mô hình áp dụng cho Hybrid</h2>
    {state && <p role="status">Đang áp dụng: <strong>{state.active.name}</strong>{state.active.modelVersion && <> · {state.active.modelVersion}</>}</p>}
    <p className="helper">Áp dụng cho các giao dịch Hybrid mới của toàn hệ thống và chức năng so sánh. Kết quả giao dịch cũ giữ nguyên. RulesOnly không dùng AI. Ngưỡng quyết định vẫn theo Luật nghiệp vụ.</p>
    {error && <div className="error" role="alert">{error}</div>}
    {message && <p className="workspace-result" role="status">{message}</p>}
    {state?.canManage ? <>
      <label>Chọn mô hình đã huấn luyện<select value={selected} disabled={busy} onChange={e => { setSelected(e.target.value); setMessage(""); }}>
        <option value="">Mô hình VND mặc định</option>
        {selectedActive && <option value={state.active.datasetId!}>{state.active.name} · Bản đang áp dụng</option>}
        {options.map(o => <option key={o.datasetId} value={o.datasetId!}>{o.name} · {o.modelVersion}</option>)}
      </select></label>
      <p className="helper">Danh sách gồm các bộ dữ liệu của bạn đã train xong. Mô hình ULB dùng đặc trưng khác nên không thể áp dụng. Dữ liệu mô phỏng hoặc nhãn nhập tay chưa chứng minh hiệu quả thực tế.</p>
      <div className="workspace-actions">
        <button className="button primary" disabled={busy || !!(selectedActive && selected === state.active.datasetId)} onClick={async () => {
          setBusy(true); setError(""); setMessage("");
          try {
            const result = await post<HybridModel>(base + "/hybrid-model", { datasetId: selected || null });
            setState(result); setSelected(result.active.datasetId ?? "");
            onApplied();
            setMessage("Đã áp dụng mô hình cho Hybrid. Hệ thống giữ bản sao này đến khi Admin chọn lại.");
          } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
        }}>Áp dụng cho Hybrid toàn hệ thống</button>
        <button className="button" disabled={busy} onClick={async () => {
          setBusy(true); setError("");
          try { await load(); } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
        }}>Làm mới danh sách</button>
      </div>
    </> : state ? <p className="helper">Chỉ Admin được đổi mô hình áp dụng cho Hybrid.</p> : <p>Đang tải mô hình…</p>}
  </section>;
}

function Metrics({ report }: { report: Report }) {
  return <div className="workspace-report">
    <p><strong>{report.modelVersion}</strong></p>
    <p className="helper">{report.datasetSource} · {new Date(report.trainedAt).toLocaleString("vi-VN")}</p>
    <div className="metric-grid">
      {([['precision', 'Precision'], ['recall', 'Recall'], ['f1', 'F1'], ['prAuc', 'AP'], ['rocAuc', 'ROC AUC']] as const).map(([key, label]) => <div key={key}><small>{label}</small><strong>{report.testMetrics[key].toFixed(3)}</strong></div>)}
    </div>
    <p>Train / validation / test: <strong>{report.split.train.toLocaleString()} / {report.split.validation.toLocaleString()} / {report.split.test.toLocaleString()}</strong></p>
    <p className="helper">Chia theo thời gian; ngưỡng {report.testMetrics.threshold.toFixed(3)} chọn trên validation. AP là average precision. Điểm AI chưa được hiệu chuẩn thành xác suất thực tế.</p>
    <div className="matrix">{["TN", "FP", "FN", "TP"].map((key, i) => <div key={key}><small>{key}</small><strong>{report.testMetrics.confusionMatrix.flat()[i]}</strong></div>)}</div>
  </div>;
}

export function RealDataModel() {
  const [report, setReport] = useState<Report | null>(null);
  const [samples, setSamples] = useState<Sample[]>([]);
  const [selected, setSelected] = useState("");
  const [input, setInput] = useState("");
  const [result, setResult] = useState<{ score: number; prediction: number; threshold: number } | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    Promise.all([api<Report>(base + "/ulb", { signal: controller.signal }), api<Sample[]>(base + "/ulb/samples", { signal: controller.signal })])
      .then(([r, s]) => { setReport(r); setSamples(s); })
      .catch((e: Error) => { if (!controller.signal.aborted) setError(e.message); });
    return () => controller.abort();
  }, []);
  return <section className="panel workspace-panel">
    <h2>Mô hình từ dữ liệu thực ULB</h2>
    <p>Giao dịch thẻ châu Âu đã ẩn danh, có nhãn hợp lệ và gian lận. Mô hình này dùng Time, Amount và V1–V28.</p>
    <p className="workspace-note">Các cột PCA không có trên form giao dịch VND. Chấm dữ liệu ULB tại đây; giao dịch VND tiếp tục dùng mô hình riêng ở tab bên cạnh.</p>
    {error && <div className="error" role="alert">{error}</div>}
    {!report && !error && <p role="status">Đang tải mô hình…</p>}
    {report && <>
      <p>{report.originalRows?.toLocaleString()} giao dịch gốc · Đã loại {report.duplicatesRemoved?.toLocaleString()} dòng trùng hoàn toàn trước khi chia tập.</p>
      <Metrics report={report} />
      <h3>Chấm thử giao dịch ULB</h3>
      <p className="helper">20 mẫu minh họa lấy từ test, có cả hai lớp; không đại diện tỷ lệ gian lận của toàn bộ dữ liệu. Nhãn không được gửi vào mô hình.</p>
      <label>Mẫu từ tập test
        <select value={selected} onChange={(e) => { const sample = samples.find(s => String(s.id) === e.target.value); setSelected(e.target.value); setInput(sample ? JSON.stringify(sample.features, null, 2) : ""); setResult(null); }}>
          <option value="">Chọn mẫu để chấm hoặc nhập JSON bên dưới</option>
          {samples.map((s, i) => <option key={s.id} value={s.id}>Mẫu {i + 1} · Amount {s.features.Amount} · Time {s.features.Time}</option>)}
        </select>
      </label>
      <label>Đặc trưng giao dịch dưới dạng JSON
        <textarea rows={7} value={input} placeholder={'{"Time": 0, "V1": 0, …, "V28": 0, "Amount": 100}'} onChange={e => { setInput(e.target.value); setSelected(""); setResult(null); }} />
      </label>
      <button className="button primary" disabled={busy || !input.trim()} onClick={async () => {
        setBusy(true); setError(""); setResult(null);
        try { setResult(await post(base + "/ulb/predict", { features: JSON.parse(input) })); }
        catch (e) { setError(e instanceof SyntaxError ? "JSON chưa đúng định dạng. Hãy chọn một mẫu hoặc kiểm tra nội dung." : (e as Error).message); }
        finally { setBusy(false); }
      }}>{busy ? "Đang chấm…" : "Chấm bằng mô hình dữ liệu thực"}</button>
      {result && <div className="workspace-result" role="status">
        <p>Điểm rủi ro: <strong>{result.score.toFixed(4)}</strong> · Ngưỡng: {result.threshold.toFixed(3)}</p>
        <p>Dự đoán: <strong>{result.prediction === 1 ? "Gian lận" : "Hợp lệ"}</strong>{selected && <> · Nhãn dữ liệu: <strong>{samples.find(s => String(s.id) === selected)?.label === 1 ? "Gian lận" : "Hợp lệ"}</strong></>}</p>
        <p className="helper">Đây là phân loại của mô hình ULB, không tạo giao dịch hoặc cảnh báo trong hệ thống VND.</p>
      </div>}
    </>}
  </section>;
}

const emptyRow = (): Row => ({ userId: "", amount: 100000, currency: "VND", merchant: "", category: "grocery", country: "VN", deviceId: "", ipAddress: "", timestamp: new Date().toISOString(), label: null });

export function PersonalDatasets() {
  const [datasets, setDatasets] = useState<Dataset[]>([]);
  const [id, setId] = useState("");
  const [detail, setDetail] = useState<Dataset | null>(null);
  const [name, setName] = useState("");
  const [samples, setSamples] = useState(1000);
  const [seed, setSeed] = useState(42);
  const [row, setRow] = useState<Row>(emptyRow);
  const [personalPrediction, setPersonalPrediction] = useState<{ score: number; prediction: number; threshold: number } | null>(null);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const list = useCallback(async () => setDatasets(await api<Dataset[]>(base + "/datasets")), []);
  useEffect(() => { list().catch((e: Error) => setError(e.message)); }, [list]);
  useEffect(() => {
    if (!id) { setDetail(null); return; }
    let active = true;
    const controller = new AbortController();
    const load = async () => {
      try { const d = await api<Dataset>(`${base}/datasets/${id}`, { signal: controller.signal }); if (active) setDetail(d); }
      catch (e) { if (active && !controller.signal.aborted) setError((e as Error).message); }
    };
    setDetail(null); setPersonalPrediction(null); void load();
    const timer = setInterval(() => { if (!document.hidden) void load(); }, 3000);
    return () => { active = false; controller.abort(); clearInterval(timer); };
  }, [id]);
  const act = async (task: () => Promise<void>) => {
    setBusy(true); setError(""); setMessage("");
    try { await task(); } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  };
  const command = async (cmd: string, body: unknown, success: string) => {
    setPersonalPrediction(null);
    await post(`${base}/datasets/${id}/${cmd}`, body);
    setDetail(await api<Dataset>(`${base}/datasets/${id}`)); await list(); setMessage(success);
  };
  const disabled = busy || detail?.status === "training";
  return <section className="panel workspace-panel">
    <h2>Bộ dữ liệu của tôi</h2>
    <p>Tạo bộ dữ liệu riêng bằng cách nhập giao dịch có nhãn hoặc sinh dữ liệu mô phỏng. Dữ liệu này tách khỏi lịch sử giao dịch trên website.</p>
    <p className="workspace-note">Dữ liệu tự sinh luôn được ghi là mô phỏng. Nhãn người dùng nhập chưa được xác minh độc lập. Train xong, Admin có thể làm mới danh sách và chọn áp dụng cho Hybrid ở phía trên. Train lại không tự đổi bản đang áp dụng.</p>
    {error && <div className="error" role="alert">{error}</div>}
    {message && <p className="workspace-result" role="status">{message}</p>}
    <form className="workspace-create" onSubmit={e => { e.preventDefault(); void act(async () => { const d = await post<Dataset>(base + "/datasets", { name }); await list(); setId(d.id); setName(""); setMessage("Đã tạo bộ dữ liệu. Thêm giao dịch hoặc sinh dữ liệu bên dưới."); }); }}>
      <label>Tên bộ dữ liệu<input required maxLength={100} value={name} onChange={e => setName(e.target.value)} placeholder="Ví dụ: Thử nghiệm giao dịch tháng 11" /></label>
      <button className="button primary" disabled={busy || !name.trim()}>Tạo bộ dữ liệu</button>
    </form>
    <label>Chọn bộ dữ liệu<select value={id} disabled={busy} onChange={e => { setId(e.target.value); setError(""); setMessage(""); }}>
      <option value="">Chọn bộ dữ liệu của bạn</option>{datasets.map(d => <option key={d.id} value={d.id}>{d.name}</option>)}
    </select></label>
    {!datasets.length && <p className="helper">Bạn chưa tạo bộ dữ liệu nào. Đặt tên và nhấn Tạo bộ dữ liệu để bắt đầu.</p>}
    {detail && <>
      <h3>{detail.name}</h3>
      <p><strong>{statusLabels[detail.status] || detail.status}</strong> · {detail.rowCount} giao dịch · {detail.labelledCount} có nhãn · {detail.fraudCount} nhãn gian lận</p>
      {detail.containsSynthetic && <p className="helper">Bộ này có dữ liệu mô phỏng. Kết quả không chứng minh hiệu quả phát hiện gian lận thực tế.</p>}
      {detail.error && <div className="error" role="alert">{detail.error}</div>}
      <form className="workspace-generate" onSubmit={e => { e.preventDefault(); void act(() => command("generate", { samples, seed }, "Đã sinh giao dịch mô phỏng cùng nhãn. Có thể tải CSV hoặc huấn luyện.")); }}>
        <h3>Sinh dữ liệu mô phỏng</h3>
        <div className="form-grid">
          <label>Số giao dịch từ 1000 đến 10000<input type="number" min={1000} max={10000} required value={samples} disabled={disabled} onChange={e => setSamples(Number(e.target.value))} /></label>
          <label>Seed từ 0 đến 2147483647<input type="number" min={0} max={2147483647} required value={seed} disabled={disabled} onChange={e => setSeed(Number(e.target.value))} /></label>
        </div>
        <p className="helper">Cùng seed cho cùng mẫu mô phỏng. Mỗi bộ được sinh một lần; có thể tiếp tục thêm giao dịch thủ công. Tối đa 10.000 giao dịch mỗi bộ.</p>
        <button className="button" disabled={disabled}>Sinh giao dịch vào bộ này</button>
      </form>
      <form onSubmit={e => { e.preventDefault(); void act(async () => { await command("rows", row, "Đã thêm giao dịch vào bộ dữ liệu."); setRow(r => ({ ...r, timestamp: new Date().toISOString() })); }); }}>
        <h3>Nhập một giao dịch</h3>
        <div className="form-grid">
          {([['userId', 'Mã người dùng'], ['merchant', 'Merchant'], ['deviceId', 'Mã thiết bị'], ['ipAddress', 'Địa chỉ IP'], ['country', 'Quốc gia hai chữ hoa'], ['timestamp', 'Thời gian ISO 8601 có múi giờ']] as const).map(([key, label]) => <label key={key}>{label}<input required maxLength={key === 'timestamp' ? 50 : key === 'country' ? 2 : key === 'userId' ? 80 : key === 'ipAddress' ? 60 : 120} value={row[key]} disabled={disabled} placeholder={key === 'ipAddress' ? '192.0.2.1' : undefined} onChange={e => setRow({ ...row, [key]: e.target.value })} /></label>)}
          <label>Số tiền VND<input required type="number" min={1} max={1e11} value={row.amount} disabled={disabled} onChange={e => setRow({ ...row, amount: Number(e.target.value) })} /></label>
          <label>Nhóm giao dịch<select value={row.category} disabled={disabled} onChange={e => setRow({ ...row, category: e.target.value })}>{['grocery', 'electronics', 'travel', 'jewelry', 'crypto', 'gambling', 'other'].map(c => <option key={c}>{c}</option>)}</select></label>
          <label>Nhãn kết luận<select value={row.label == null ? '' : row.label} disabled={disabled} onChange={e => setRow({ ...row, label: e.target.value === '' ? null : Number(e.target.value) })}><option value="">Chưa xác định</option><option value="0">Hợp lệ</option><option value="1">Gian lận</option></select></label>
        </div>
        <p className="helper">Chỉ gán nhãn khi có căn cứ. Mẫu Chưa xác định vẫn được lưu nhưng không dùng để train.</p>
        <button className="button" disabled={disabled}>Thêm giao dịch</button>
        {detail.status === 'ready' && <button className="button" type="button" disabled={disabled} onClick={() => void act(async () => {
          setPersonalPrediction(null);
          setPersonalPrediction(await post(`${base}/datasets/${id}/predict`, row));
        })}>Chấm thử với mô hình của bộ này</button>}
        {personalPrediction && <p className="workspace-result" role="status">Điểm {personalPrediction.score.toFixed(4)} · Ngưỡng {personalPrediction.threshold.toFixed(3)} · Dự đoán {personalPrediction.prediction === 1 ? 'gian lận' : 'hợp lệ'}. Chấm thử không lưu thêm giao dịch.</p>}
      </form>
      <div className="workspace-actions">
        <button className="button primary" disabled={disabled || detail.labelledCount < 500} onClick={() => void act(() => command("train", {}, "Đã bắt đầu huấn luyện nền. Kết quả sẽ tự cập nhật tại đây."))}>{detail.status === 'training' ? 'Đang huấn luyện…' : 'Huấn luyện bộ dữ liệu này'}</button>
        <button className="button" disabled={busy || detail.rowCount === 0} onClick={() => void act(async () => {
          const response = await fetch(`/api${base}/datasets/${id}/csv`, { headers: { Authorization: `Bearer ${sessionStorage.getItem('fs-token')}` } });
          if (!response.ok) throw new Error('Không thể tải CSV. Hãy kiểm tra phiên đăng nhập.');
          const url = URL.createObjectURL(await response.blob()); const a = document.createElement('a'); a.href = url; a.download = 'fraudshield-dataset.csv'; a.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
        })}>Tải bộ dữ liệu CSV</button>
        <button className="button" disabled={disabled} onClick={() => {
          if (window.confirm(`Xóa bộ dữ liệu "${detail.name}" và mô hình riêng của bộ này?`)) void act(async () => {
            await api(`${base}/datasets/${id}`, { method: 'DELETE' }); setId(''); await list(); setMessage('Đã xóa bộ dữ liệu.');
          });
        }}>Xóa bộ dữ liệu</button>
      </div>
      <p className="helper">Cần ít nhất 500 mẫu có nhãn, đủ cả hai lớp trong từng tập train, validation và test theo thời gian. Có thể rời tab rồi quay lại xem tiến độ.</p>
      {detail.metrics && <Metrics report={detail.metrics} />}
      {!!detail.preview?.length && <>
        <h3>20 giao dịch được thêm gần nhất</h3>
        <div className="table-scroll"><table><thead><tr><th>Người dùng</th><th>Số tiền</th><th>Thời gian</th><th>Nhãn</th><th>Nguồn</th></tr></thead><tbody>{detail.preview.map((r, i) => <tr key={i}><td>{r.userId}</td><td>{money(r.amount)}</td><td>{new Date(r.timestamp).toLocaleString('vi-VN')}</td><td>{r.label == null ? 'Chưa xác định' : r.label === 1 ? 'Gian lận' : 'Hợp lệ'}</td><td>{r.origin === 'synthetic' ? 'Mô phỏng' : 'Nhập tay'}</td></tr>)}</tbody></table></div>
      </>}
    </>}
  </section>;
}
