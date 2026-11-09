import { AlertTriangle, X } from "lucide-react";
import { Badge } from "../../../components/common";
import { money, percent, type Evaluation } from "../../../shared/services/api";
export function Detail({
  item: t,
  onClose,
  onOpenAlerts,
}: {
  item: Evaluation;
  onClose: () => void;
  onOpenAlerts: () => void;
}) {
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
        {t.decision !== "ALLOW" && (
          <div className="notice">
            <p>
              Nhận việc và ghi kết luận tại cảnh báo riêng. Khi chưa đủ bằng
              chứng, có thể chờ hoặc đóng Unknown.
            </p>
            <button className="button" onClick={onOpenAlerts}>
              Mở cảnh báo của giao dịch
            </button>
          </div>
        )}
        {t.reviews.length > 0 && (
          <h3>Kết luận cũ — chưa xác minh theo quy trình Alert</h3>
        )}
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
