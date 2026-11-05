import { useEffect, useState } from "react";
import { type Policy } from "../../../shared/services/api";
import { savePolicy } from "../_services/services";
export function Rules({
  policy,
  canEdit,
  onSaved,
}: {
  policy: Policy;
  canEdit: boolean;
  onSaved: (p: Policy) => void;
}) {
  const [p, setP] = useState(policy),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false);
  useEffect(() => setP(policy), [policy]);
  const fields: [keyof Policy, string][] = [
    ["highAmount", "Giá trị cao VND"],
    ["criticalAmount", "Giá trị nghiêm trọng VND"],
    ["velocityCount", "Số giao dịch trước đó trong 1 giờ"],
    ["sharedDeviceUsers", "Số người dùng chung thiết bị"],
    ["reviewThreshold", "Ngưỡng REVIEW"],
    ["blockThreshold", "Ngưỡng BLOCK"],
    ["mlWeight", "Trọng số ML trong Hybrid"],
  ];
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
        <div className="form-grid">
          {fields.map(([key, label]) => (
            <label key={key}>
              {label}
              <input
                type="number"
                step={
                  key.toLowerCase().includes("threshold") || key === "mlWeight"
                    ? 0.01
                    : 1
                }
                value={Number(p[key])}
                disabled={!canEdit}
                onChange={(e) => setP({ ...p, [key]: Number(e.target.value) })}
              />
            </label>
          ))}
        </div>
        <div className="rule-toggles">
          {(
            [
              ["amountEnabled", "Luật hạn mức"],
              ["velocityEnabled", "Luật tần suất"],
              ["deviceEnabled", "Luật thiết bị và IP"],
              ["unusualEnabled", "Luật lệch lịch sử"],
            ] as const
          ).map(([key, label]) => (
            <label key={key}>
              <input
                type="checkbox"
                checked={p[key]}
                disabled={!canEdit}
                onChange={(e) => setP({ ...p, [key]: e.target.checked })}
              />
              {label}
            </label>
          ))}
        </div>
        <p className="helper">
          Hai luật ban đêm và merchant luôn được áp dụng. Trọng số là cấu hình
          thử nghiệm; AI không ghi đè luật nghiêm trọng hoặc luật đạt ngưỡng
          BLOCK.
        </p>
        {error && <div className="error">{error}</div>}
        {canEdit && (
          <button className="button primary" disabled={busy}>
            Lưu phiên bản mới
          </button>
        )}
      </form>
    </section>
  );
}
