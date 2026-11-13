import { LoaderCircle, ScanLine, X } from "lucide-react";
import { useRef, useState } from "react";
import { type Evaluation } from "../../../shared/services/api";
import { transactionConstraints } from "../_schema/schema";
import { analyzeTransaction } from "../_services/services";
export function TransactionForm({
  mode,
  onClose,
  onSuccess,
}: {
  mode: string;
  onClose: () => void;
  onSuccess: (t: Evaluation) => void;
}) {
  const [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [amount, setAmount] = useState(""),
    [analysisMode, setAnalysisMode] = useState(mode);
  const submission = useRef<{ body: string; id: string } | null>(null);
  return (
    <div className="overlay">
      <section className="modal" role="dialog" aria-modal="true" aria-labelledby="create-transaction-title">
        <div className="modal-heading">
          <div>
            <h2 id="create-transaction-title">Tạo giao dịch mới</h2>
            <p>
              Nhập thông tin riêng · {analysisMode === "RulesOnly"
                ? "Chỉ dùng luật nghiệp vụ"
                : "Kết hợp luật và AI"}{" "}
              · VND
            </p>
          </div>
          <button onClick={onClose} aria-label="Đóng" disabled={busy}>
            <X />
          </button>
        </div>
        <form
          onSubmit={async (e) => {
            e.preventDefault();
            const f = new FormData(e.currentTarget);
            setBusy(true);
            setError("");
            const request = {
              userId: f.get("userId"),
              amount: Number(amount),
              currency: "VND",
              merchant: f.get("merchant"),
              category: f.get("category"),
              country: f.get("country"),
              deviceId: f.get("device"),
              ipAddress: f.get("ip"),
              mode: analysisMode,
              ...(f.get("timestamp") ? { timestamp: new Date(String(f.get("timestamp"))).toISOString() } : {}),
            };
            const body = JSON.stringify(request);
            if (submission.current?.body !== body)
              submission.current = { body, id: crypto.randomUUID() };
            try {
              onSuccess(
                await analyzeTransaction({
                  ...request,
                  id: submission.current.id,
                }),
              );
            } catch (e) {
              setError((e as Error).message);
            } finally {
              setBusy(false);
            }
          }}
        >
          <div className="form-grid">
            <label>
              Người dùng
              <input
                name="userId"
                placeholder="Nhập mã người dùng"
                maxLength={80}
                required
              />
            </label>
            <label>
              Số tiền VND
              <input
                type="number"
                value={amount}
                onChange={(e) => setAmount(e.target.value)}
                placeholder="Nhập số tiền"
                step="1"
                min={transactionConstraints.amount.min}
                max={transactionConstraints.amount.max}
                required
              />
            </label>
            <label>
              Merchant
              <input
                name="merchant"
                placeholder="Nhập tên đơn vị nhận tiền"
                required
                maxLength={80}
              />
            </label>
            <label>
              Nhóm merchant
              <select name="category">
                {[
                  "grocery",
                  "electronics",
                  "travel",
                  "jewelry",
                  "crypto",
                  "gambling",
                  "other",
                ].map((c) => (
                  <option key={c}>{c}</option>
                ))}
              </select>
            </label>
            <label>
              Quốc gia
              <input
                name="country"
                defaultValue="VN"
                pattern={transactionConstraints.countryPattern}
                required
              />
            </label>
            <label>
              Thiết bị
              <input
                name="device"
                placeholder="Nhập mã thiết bị"
                required
                maxLength={100}
              />
            </label>
            <label>
              Địa chỉ IP
              <input
                name="ip"
                placeholder="Ví dụ: 192.0.2.10"
                required
                maxLength={45}
              />
            </label>
            <label>
              Thời gian giao dịch
              <input name="timestamp" type="datetime-local" step="1" />
              <small>
                Để trống để lấy thời gian hiện tại; nhập theo giờ trên máy của bạn.
                Có thể nhập đến giây; các giao dịch cùng thời điểm không được tính là chuỗi.
              </small>
            </label>
            <label>
              Cách phân tích
              <select value={analysisMode} onChange={e => setAnalysisMode(e.target.value)}>
                <option value="RulesOnly">Chỉ dùng luật</option>
                <option value="Hybrid">Luật kết hợp AI giao dịch VND</option>
              </select>
            </label>
          </div>
          {error && <div className="error" role="alert">{error}</div>}
          <p className="helper">
            Khi lưu, giao dịch được phân tích và thêm vào danh sách. Kết quả REVIEW hoặc BLOCK sẽ tạo cảnh báo.{" "}
            Không nhập thông tin tài khoản hoặc thẻ thật. Thiết bị và IP được
            HMAC trước khi lưu. Các trường thiết bị, IP và quốc gia là dữ liệu bạn cung cấp để thử nghiệm, chưa được hệ thống xác minh là thông tin truy cập thật.
          </p>
          <button className="button primary wide" disabled={busy}>
            {busy ? (
              <LoaderCircle className="spin" size={17} />
            ) : (
              <ScanLine size={17} />
            )}{" "}
            {busy ? "Đang tạo giao dịch…" : "Tạo và phân tích giao dịch"}
          </button>
        </form>
      </section>
    </div>
  );
}
