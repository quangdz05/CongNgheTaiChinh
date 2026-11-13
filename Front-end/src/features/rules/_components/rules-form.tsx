import { useEffect, useState } from "react";
import { api, type Policy, type RiskReference } from "../../../shared/services/api";
import { savePolicy } from "../_services/services";
export function Rules({
  policy,
  canEdit,
  onSaved,
  modelMetrics,
}: {
  policy: Policy;
  canEdit: boolean;
  onSaved: (p: Policy) => void;
  modelMetrics?: { modelVersion?: string; datasetSource?: string; validationMetrics?: { threshold?: number } } | null;
}) {
  const [p, setP] = useState(policy),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false);
  const [references, setReferences] = useState<RiskReference[]>([]);
  const [referenceError, setReferenceError] = useState("");
  const [localModelMetrics, setLocalModelMetrics] = useState<typeof modelMetrics>(null);
  const activeModelMetrics = modelMetrics ?? localModelMetrics;
  useEffect(() => {
    let active = true;
    if (!modelMetrics) api<NonNullable<typeof modelMetrics>>("/ai/metrics").then(r => { if (active) setLocalModelMetrics(r); }).catch(() => { if (active) setLocalModelMetrics(null); });
    return () => { active = false; };
  }, [modelMetrics]);
  useEffect(() => {
    let active = true;
    api<{references: RiskReference[]}>("/rules/references").then(r => { if (active) setReferences(r.references); }).catch(() => { if (active) setReferenceError("Chưa tải được nguồn tham khảo. Hãy tải lại trang."); });
    return () => { active = false; };
  }, []);
  useEffect(() => setP(policy), [policy]);
  const signalMode = p.analysisStrategy === "RiskSignals";
  const signalFields: [keyof Policy, string, string][] = [
    ["burstWindowSeconds", "Cửa sổ chuỗi giao dịch (giây)", "Đếm giao dịch cùng người dùng, gồm giao dịch hiện tại. Lịch sử tham chiếu tối đa 24 giờ."],
    ["burstCount", "Số giao dịch trong chuỗi cần kiểm tra", "Nếu đủ lịch sử, chỉ gắn cờ khi vượt cả ngưỡng này và mức cao nhất của các cửa sổ trước chuỗi. Nếu thiếu lịch sử, ghi rõ chưa kết luận bất thường."],
    ["minimumHistory", "Số giao dịch tham chiếu tối thiểu", "Dùng để đánh giá độ lệch giá trị và tần suất cá nhân. Thiếu lịch sử sẽ có nhãn thông tin riêng, không tự kết luận rủi ro."],
    ["amountDeviationThreshold", "Độ lệch giá trị tối thiểu (z-score)", "So với trung bình và độ lệch chuẩn của giao dịch trước đó trong 24 giờ. Nhạy với lịch sử ít biến động; cần kiểm định bằng dữ liệu có nhãn."],
    ["highAmount", "Hạn mức kiểm tra nghiệp vụ (VND)", "Chỉ dùng khi bật kiểm tra hạn mức. Do đơn vị vận hành xác định, không phải ngưỡng gian lận phổ quát."],
    ["sharedDeviceUsers", "Số người dùng chung thiết bị", "Số người dùng phân biệt trong 24 giờ, gồm người dùng hiện tại. Đạt ngưỡng sẽ yêu cầu xác minh liên kết tài khoản."],
    ["sharedIpUsers", "Số người dùng chung IP để theo dõi", "IP chung có thể do NAT hoặc Wi-Fi công cộng. Dấu hiệu này riêng lẻ không tạo yêu cầu kiểm tra."],
    ["reviewThreshold", "Ngưỡng điểm AI yêu cầu kiểm tra", "Áp dụng trong Hybrid. AI giao dịch VND hiện học dữ liệu mô phỏng; ngưỡng cấu hình này chưa được kiểm định cho dữ liệu thực tế."],
  ];
  const legacyFields: [keyof Policy, string][] = [
    ["highAmount", "Giá trị cao VND"],
    ["criticalAmount", "Giá trị nghiêm trọng VND"],
    ["velocityCount", "Số giao dịch trước đó trong 1 giờ"],
    ["sharedDeviceUsers", "Số người dùng chung thiết bị"],
    ["reviewThreshold", "Ngưỡng REVIEW"],
    ["blockThreshold", "Ngưỡng BLOCK"],
    ["mlWeight", "Trọng số ML trong Hybrid"],
  ];
  const fields: [keyof Policy, string, string?][] = signalMode ? signalFields : legacyFields;
  return (
    <section className="panel rules-panel">
      <div className="panel-heading">
        <div>
          <h2>Cấu hình chính sách</h2>
          <p>Phiên bản {p.version} · Thay đổi chỉ áp dụng cho giao dịch mới.</p>
        </div>
        <span className="badge allow">{canEdit ? "Admin" : "Chỉ xem"}</span>
      </div>
      <form
        onSubmit={async (e) => {
          e.preventDefault();
          setBusy(true);
          setError("");
          try {
            onSaved(await savePolicy(p));
          } catch (e) {
            setError((e as Error).message);
          } finally {
            setBusy(false);
          }
        }}
      >
        <label>Phương pháp đánh giá
          <select value={p.analysisStrategy} disabled={!canEdit || busy} onChange={e => setP({ ...p, analysisStrategy: e.target.value })}>
            <option value="RiskSignals">Phân nhóm dấu hiệu và bằng chứng</option>
            <option value="LegacyScore" disabled>Chấm điểm cũ chỉ để đọc lịch sử</option>
          </select>
        </label>
        {signalMode && <div className="notice"><p>Một giao dịch có thể thuộc nhiều nhóm dấu hiệu. Có dấu hiệu yêu cầu kiểm tra từ luật HOẶC AI → REVIEW và tạo cảnh báo. Không tự động BLOCK theo số tiền, giờ giao dịch hay loại merchant; không trộn điểm luật với AI.</p><p>Các giá trị khởi tạo là tham số thử nghiệm. Nguồn tham khảo hỗ trợ nhóm dấu hiệu, không chứng minh các ngưỡng đang nhập. Cần kiểm định báo nhầm và bỏ sót trên dữ liệu có nhãn theo từng nhóm khách hàng. ALLOW nghĩa là chưa có cờ kiểm tra theo chính sách hiện tại.</p><p>Thiết bị, IP và quốc gia đang do người nhập cung cấp hoặc sinh mô phỏng. Quốc gia giao dịch không phải vị trí đăng nhập đã xác minh; dự án chưa thu thập dấu vân tay thiết bị, GPS hoặc quan hệ chủ tài khoản.</p></div>}
        {!signalMode && <p className="notice">Chính sách cũ đang được hiển thị để đối chiếu. Muốn lưu phiên bản mới, chọn Phân nhóm dấu hiệu và bằng chứng. Kết quả lịch sử được giữ nguyên.</p>}
        {signalMode && activeModelMetrics?.validationMetrics?.threshold != null && <div className="notice"><p>Ngưỡng validation của mô hình VND {activeModelMetrics.modelVersion}: <strong>{activeModelMetrics.validationMetrics.threshold.toFixed(3)}</strong>. Nguồn dữ liệu: {activeModelMetrics.datasetSource === "synthetic" ? "mô phỏng" : activeModelMetrics.datasetSource}. Đây là kết quả chọn ngưỡng cho mô hình này, chưa xác nhận hiệu quả vận hành hoặc hiệu quả của toàn bộ Hybrid.</p>{canEdit && <button type="button" className="button" disabled={busy} onClick={() => setP({ ...p, reviewThreshold: Number(activeModelMetrics.validationMetrics!.threshold!.toFixed(3)) })}>Dùng ngưỡng validation của mô hình</button>}</div>}
        <div className="form-grid">
          {fields.map(([key, label, description]) => (
            <label key={key}>
              {label}
              <input
                type="number"
                step={
                  key.toLowerCase().includes("threshold") || key === "mlWeight"
                    ? 0.001
                    : 1
                }
                value={Number(p[key])}
                disabled={!canEdit || busy}
                required
                onChange={(e) => setP({ ...p, [key]: Number(e.target.value) })}
              />
              {description && <small>{description}</small>}
            </label>
          ))}
        </div>
        <div className="rule-toggles">
          {(
            [
              ["amountEnabled", "Kiểm tra hạn mức nghiệp vụ"],
              ["velocityEnabled", "Phát hiện chuỗi giao dịch"],
              ["deviceEnabled", "Kiểm tra truy cập và liên kết tài khoản"],
              ["unusualEnabled", "So sánh giá trị với lịch sử cá nhân"],
            ] as const
          ).map(([key, label]) => (
            <label key={key}>
              <input
                type="checkbox"
                checked={p[key]}
                disabled={!canEdit || busy}
                onChange={(e) => setP({ ...p, [key]: e.target.checked })}
              />
              {label}
            </label>
          ))}
        </div>
        <p className="helper">
          {signalMode ? "Dấu hiệu, nguồn tham khảo và phiên bản chính sách được lưu cùng giao dịch. Lịch sử tối đa 24 giờ chưa đại diện hành vi dài hạn. So đỉnh lịch sử có thể bỏ sót hành vi xấu đã lặp lại; z-score nhạy với lịch sử ít biến động. Các luật không tự xác định loại gian lận." : "Kết quả cũ có luật giờ UTC và điểm merchant giả định. Phương pháp mới bỏ hai luật này khỏi quyết định nghiệp vụ. Các đặc trưng của mô hình AI giữ nguyên schema để tương thích artifact đã train."}
        </p>
        {error && <div className="error">{error}</div>}
        {canEdit && (
          <button className="button primary" disabled={busy || !signalMode}>
            Lưu phiên bản mới
          </button>
        )}
      </form>
      <details className="risk-references"><summary>Nguồn tham khảo và đối chiếu luật</summary>
        <p className="helper">Rà soát ngày 12/11/2026. Nguồn quốc tế dùng để tham khảo phương pháp, không phải quy định bắt buộc ở Việt Nam. Bản luật AML Việt Nam không phải chứng nhận hiệu quả phát hiện gian lận của dự án.</p>
        {references.map(r => <p key={r.id}><a href={r.url} target="_blank" rel="noreferrer">{r.title}</a><br />{r.scope}</p>)}
        {referenceError && <p role="alert">{referenceError}</p>}
      </details>
    </section>
  );
}
