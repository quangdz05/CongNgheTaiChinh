import { riskGroups, type RiskFinding } from "../../../shared/services/api";

export function RiskFindings({ findings }: { findings: RiskFinding[] }) {
  const groups = [...new Set(findings.map(f => f.group))];
  return <div className="risk-findings">
    {groups.map(group => <section className="risk-group" key={group}>
      <h3>{riskGroups[group] ?? group}</h3>
      {findings.filter(f => f.group === group).map(f => <div className="match" key={f.code}>
        <div><strong>{f.title}</strong><p>{f.evidence}</p>
          <small>{f.source === "model" ? "Mô hình AI" : f.source === "data" ? "Dữ liệu đầu vào" : "Luật nghiệp vụ"} · {f.action === "REVIEW" ? "Cần kiểm tra" : f.action === "OBSERVE" ? "Theo dõi" : "Thông tin"}</small>
          {f.validationStatus && <p className="helper">Cấu hình thử nghiệm, chưa kiểm định cho vận hành thực tế. Dữ liệu nhập hoặc mô phỏng chưa được xác minh độc lập.</p>}
          {!!f.references?.length && <details className="risk-references"><summary>Nguồn tham khảo và phạm vi áp dụng</summary>{f.references.map(r => <p key={r.id}><a href={r.url} target="_blank" rel="noreferrer">{r.title}</a><br />{r.scope}</p>)}</details>}
        </div>
      </div>)}
    </section>)}
    {!findings.some(f => f.action === "REVIEW") && <p className="helper">Chưa có dấu hiệu yêu cầu kiểm tra theo chính sách này. Không có nghĩa đã xác nhận giao dịch an toàn.</p>}
  </div>;
}
