import { useEffect, useState } from "react";
import { Badge, Empty } from "../../../components/common";
import {
  money,
  api,
  percent,
  type TransactionSummary,
} from "../../../shared/services/api";
import { compareTransaction } from "../_services/services";
import { HybridModelSelector, PersonalDatasets, RealDataModel } from "./ai-workspace";
export function AiPanel({
  status: initialStatus,
  metrics: initialMetrics,
  rows,
}: {
  status: string;
  metrics: any;
  rows: TransactionSummary[];
}) {
  const [tab, setTab] = useState("real");
  const [modelRevision, setModelRevision] = useState(0);
  const [currentModel, setCurrentModel] = useState<{ status: string; metrics: any } | null>(null);
  const status = currentModel?.status ?? initialStatus;
  const metrics = currentModel ? currentModel.metrics : initialMetrics;
  useEffect(() => {
    if (tab !== "native") return;
    const controller = new AbortController();
    setCurrentModel({ status: "Loading", metrics: null });
    Promise.all([
      api<{ status: string }>("/ai/status", { signal: controller.signal }),
      api<any>("/ai/metrics", { signal: controller.signal }),
    ]).then(([s, m]) => { if (!controller.signal.aborted) setCurrentModel({ status: s.status, metrics: m }); })
      .catch(() => { if (!controller.signal.aborted) setCurrentModel({ status: "Unavailable", metrics: null }); });
    return () => controller.abort();
  }, [tab, modelRevision]);
  const [id, setId] = useState(""),
    [comparison, setComparison] = useState<any>(null),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false);
  return <>
    <HybridModelSelector onApplied={() => setModelRevision(r => r + 1)} />
    <div className="workspace-tabs" role="group" aria-label="Mô hình và dữ liệu AI">
      {[["real", "Dữ liệu thực ULB"], ["datasets", "Bộ dữ liệu của tôi"], ["native", "AI giao dịch VND"]].map(([key, label]) => <button key={key} className={tab === key ? "active" : ""} aria-pressed={tab === key} onClick={() => setTab(key)}>{label}</button>)}
    </div>
    {tab === "real" ? <RealDataModel /> : tab === "datasets" ? <PersonalDatasets /> : (
    <div className="ai-layout">
      <section className="panel">
        <div className="panel-heading">
          <div>
            <h2>Mô hình giao dịch VND</h2>
            <p>Mô hình riêng dùng 10 đặc trưng nghiệp vụ, không phải mô hình ULB.</p>
          </div>
          <Badge value={status === "Ready" ? "READY" : "OFFLINE"} />
        </div>
        {metrics ? (
          <>
            <div className="model-meta">
              <strong>{metrics.modelVersion}</strong>
              <span>{metrics.datasetSource}</span>
              <span>{metrics.trainedAt}</span>
            </div>
            {metrics.datasetSource === "synthetic" && (
              <div className="notice">
                Dữ liệu mô phỏng. Các số liệu không đại diện cho hiệu quả phát
                hiện gian lận thực tế.
              </div>
            )}
            <div className="metric-grid">
              {["precision", "recall", "f1", "prAuc", "rocAuc"].map((k) => (
                <div key={k}>
                  <small>{k}</small>
                  <strong>{metrics.testMetrics?.[k]?.toFixed(3) ?? "—"}</strong>
                </div>
              ))}
            </div>
            <h3>Confusion matrix trên tập test</h3>
            <div className="matrix">
              {["TN", "FP", "FN", "TP"].map((k, i) => (
                <div key={k}>
                  <small>{k}</small>
                  <strong>
                    {metrics.testMetrics?.confusionMatrix?.flat()[i] ?? "—"}
                  </strong>
                </div>
              ))}
            </div>
            <p className="helper">
              Train / validation / test: {metrics.split?.train} /{" "}
              {metrics.split?.validation} / {metrics.split?.test}. Ngưỡng được
              chọn bằng validation. Quyết định Hybrid còn áp dụng chính sách C#.
            </p>
          </>
        ) : (
          <Empty
            text="Chưa có mô hình sẵn sàng"
            detail="Huấn luyện bằng ai-service/train.py, sau đó khởi động AI API. Chế độ Rules Only vẫn hoạt động."
          />
        )}
      </section>
      <section className="panel compare-panel">
        <div className="panel-heading">
          <div>
            <h2>So sánh trên cùng giao dịch</h2>
            <p>Dùng đặc trưng đã lưu, không thêm dữ liệu vào lịch sử.</p>
          </div>
        </div>
        <select
          aria-label="Giao dịch để so sánh"
          value={id}
          disabled={busy}
          onChange={(e) => {
            setId(e.target.value);
            setComparison(null);
          }}
        >
          <option value="">Chọn giao dịch</option>
          {rows.map((t) => (
            <option value={t.id} key={t.id}>
              {t.userId} · {money(t.amount)}
            </option>
          ))}
        </select>
        <button
          className="button primary wide"
          disabled={!id || busy}
          onClick={async () => {
            setBusy(true);
            setError("");
            try {
              setComparison(await compareTransaction(id));
            } catch (e) {
              setError((e as Error).message);
            } finally {
              setBusy(false);
            }
          }}
        >
          {busy ? "Đang so sánh…" : "So sánh luật và AI"}
        </button>
        {error && <div className="error">{error}</div>}
        {comparison && (
          <div className="compare-result">
            <p>
              Luật: <strong>{comparison.analysisStrategy === "RiskSignals" ? "Theo dấu hiệu đã lưu" : percent(comparison.ruleScore)}</strong> ·{" "}
              {comparison.ruleDecision}
            </p>
            <p>
              ML:{" "}
              <strong>
                {comparison.mlScore == null
                  ? "Không khả dụng"
                  : percent(comparison.mlScore)}
              </strong>
            </p>
            <p>
              Hybrid:{" "}
              <strong>
                {comparison.analysisStrategy === "RiskSignals" ? "Luật hoặc AI yêu cầu kiểm tra" : comparison.hybridScore == null
                  ? "Chưa đánh giá"
                  : percent(comparison.hybridScore)}
              </strong>{" "}
              · {comparison.hybridDecision ?? "—"}
            </p>
            <small>{comparison.note}</small>
          </div>
        )}
      </section>
    </div>
  )}</>;
}
