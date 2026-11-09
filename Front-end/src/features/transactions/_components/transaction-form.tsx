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
    [amount, setAmount] = useState(150000);
  const submission = useRef<{ body: string; id: string } | null>(null);
  return (
    <div className="overlay">
      <section className="modal">
        <div className="modal-heading">
          <div>
            <h2>Phân tích giao dịch</h2>
            <p>
              {mode === "RulesOnly"
                ? "Chỉ dùng luật nghiệp vụ"
                : "Kết hợp luật và AI"}{" "}
              · VND
            </p>
          </div>
          <button onClick={onClose} aria-label="Đóng">
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
              amount,
              currency: "VND",
              merchant: f.get("merchant"),
              category: f.get("category"),
              country: f.get("country"),
              deviceId: f.get("device"),
              ipAddress: f.get("ip"),
              mode,
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
          <div className="presets">
            {[
              ["Thông thường", 150000],
              ["Giá trị cao", 150000000],
              ["Nghiêm trọng", 300000000],
            ].map(([label, value]) => (
              <button
                className="button"
                type="button"
                key={label}
                onClick={() => setAmount(Number(value))}
              >
                {label}
              </button>
            ))}
          </div>
          <div className="form-grid">
            <label>
              Người dùng
              <input
                name="userId"
                defaultValue="USER-DEMO-001"
                maxLength={80}
                required
              />
            </label>
            <label>
              Số tiền VND
              <input
                type="number"
                value={amount}
                onChange={(e) => setAmount(Number(e.target.value))}
                min={transactionConstraints.amount.min}
                max={transactionConstraints.amount.max}
                required
              />
            </label>
            <label>
              Merchant
              <input
                name="merchant"
                defaultValue="FinTech Store"
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
                defaultValue="DEVICE-DEMO-001"
                required
                maxLength={100}
              />
            </label>
            <label>
              Địa chỉ IP
              <input
                name="ip"
                defaultValue="192.0.2.10"
                required
                maxLength={45}
              />
            </label>
          </div>
          {error && <div className="error">{error}</div>}
          <p className="helper">
            Không nhập thông tin tài khoản hoặc thẻ thật. Thiết bị và IP được
            HMAC trước khi lưu.
          </p>
          <button className="button primary wide" disabled={busy}>
            {busy ? (
              <LoaderCircle className="spin" size={17} />
            ) : (
              <ScanLine size={17} />
            )}{" "}
            Phân tích và lưu giao dịch
          </button>
        </form>
      </section>
    </div>
  );
}
