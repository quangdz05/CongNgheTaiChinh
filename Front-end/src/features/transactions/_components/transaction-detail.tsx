import { AlertTriangle, Ban, Check, X } from "lucide-react";
import { useState } from "react";
import { Badge } from "../../../components/common";
import { money, percent, type Evaluation } from "../../../shared/services/api";
import { submitReview } from "../_services/services";
export function Detail({
  item: t,
  onClose,
  onRefresh,
}: {
  item: Evaluation;
  onClose: () => void;
  onRefresh: () => Promise<void>;
}) {
  const [notes, setNotes] = useState(""),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false);
  async function review(isFraud: boolean) {
    setBusy(true);
    setError("");
    try {
      await submitReview(t.id, isFraud, notes);
      setNotes("");
      await onRefresh();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <div className="overlay">
      <section className="modal detail">
        <div className="modal-heading">
          <div>
            <h2>Chi tiết giao dịch</h2>
            <p>{t.id}</p>
          </div>
          <button onClick={onClose} aria-label="Đóng">
            <X />
          </button>
        </div>
        <div className="detail-summary">
          <div>
            <small>
              {t.userId} · {t.merchant}
            </small>
            <h2>{money(t.amount)}</h2>
          </div>
          <Badge value={t.decision} />
        </div>
        <div className="detail-scores">
          {[
            ["Điểm luật", percent(t.ruleScore)],
            [
              "Điểm ML",
              t.mlScore == null ? "Chưa đánh giá" : percent(t.mlScore),
            ],
            ["Điểm cuối", percent(t.finalScore)],
          ].map(([k, v]) => (
            <div key={k}>
              <small>{k}</small>
              <strong>{v}</strong>
            </div>
          ))}
        </div>
        <p className="helper">
          {t.mode} · AI: {t.aiStatus} · Luật v{t.ruleVersion}{" "}
          {t.modelVersion ? "· " + t.modelVersion : ""}
          <br />
          Điểm rủi ro không phải xác suất gian lận đã hiệu chuẩn.
        </p>
        <h3>Căn cứ nghiệp vụ</h3>
        {t.matches.length ? (
          t.matches.map((m) => (
            <div className="match" key={m.code}>
              <AlertTriangle size={17} />
              <div>
                <strong>{m.title}</strong>
                <p>{m.evidence}</p>
              </div>
              <span>{percent(m.score)}</span>
            </div>
          ))
        ) : (
          <p className="helper">
            Không có luật kích hoạt. Điều này không đảm bảo giao dịch an toàn
            tuyệt đối.
          </p>
        )}
        <details>
          <summary>Đặc trưng đã dùng để đánh giá</summary>
          <div className="feature-grid">
            {Object.entries(t.features).map(([k, v]) => (
              <div key={k}>
                <span>{k}</span>
                <strong>{Number(v).toFixed(2)}</strong>
              </div>
            ))}
          </div>
        </details>
        <h3>Kết luận kiểm tra</h3>
        <textarea
          aria-label="Ghi chú kiểm tra"
          placeholder="Ghi căn cứ kiểm tra, tối thiểu 5 ký tự…"
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
          maxLength={1000}
        />
        {error && <div className="error">{error}</div>}
        <div className="actions review-actions">
          <button
            className="button"
            disabled={busy || notes.trim().length < 5}
            onClick={() => review(false)}
          >
            <Check size={16} /> Giao dịch hợp lệ
          </button>
          <button
            className="button danger"
            disabled={busy || notes.trim().length < 5}
            onClick={() => review(true)}
          >
            <Ban size={16} /> Xác nhận gian lận
          </button>
        </div>
        {t.reviews.map((r) => (
          <div className="review-entry" key={r.id}>
            <strong>
              {r.isFraud ? "Gian lận được ghi nhận" : "Giao dịch hợp lệ"}
            </strong>
            <p>{r.notes}</p>
            <small>
              {r.reviewer} · {new Date(r.reviewedAt).toLocaleString("vi-VN")}
            </small>
          </div>
        ))}
      </section>
    </div>
  );
}
