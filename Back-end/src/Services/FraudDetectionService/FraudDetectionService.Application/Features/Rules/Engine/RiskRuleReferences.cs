using FraudDetectionService.Domain;

namespace FraudDetectionService.Application;

public static class RiskRuleReferences
{
    public static readonly RiskReference Monitoring = new("FFIEC_MONITORING", "FFIEC Suspicious Activity Reporting",
        "https://bsaaml.ffiec.gov/manual/AssessingComplianceWithBSARegulatoryRequirements/04",
        "Ngưỡng phải phù hợp hồ sơ khách hàng và được kiểm định. Không chứng minh cửa sổ 24 giờ, 20 mẫu hoặc thuật toán so đỉnh lịch sử của dự án.");
    public static readonly RiskReference Velocity = new("FEDNOW_VELOCITY", "Federal Reserve Account activity thresholds",
        "https://www.frbservices.org/resources/resource-centers/fraud-mitigation-resource-center/frfs-fraud-mitigation-products-services/",
        "Tham khảo kiểm soát tần suất theo nhóm khách hàng. Không quy định 3 giao dịch trong 60 giây.");
    public static readonly RiskReference Statistics = new("NIST_OUTLIERS", "NIST Detection of Outliers",
        "https://itl.nist.gov/div898/handbook/eda/section3/eda35h.htm",
        "Tham khảo thống kê ngoại lệ. Không xác nhận z-score 2,5 là ngưỡng gian lận; modified z-score 3,5 dùng công thức khác.");
    public static readonly RiskReference Access = new("FFIEC_ACCESS", "FFIEC Authentication and Access 2021",
        "https://www.federalreserve.gov/frrs/guidance/authentication-and-access-to-financial-institution-services-and-systems-interagency-guidance.htm",
        "Kết hợp thiết bị, vị trí và hành vi. IP hoặc vị trí riêng lẻ không đủ tin cậy; điều kiện đồng thời mới của dự án chưa được kiểm định.");
    public static readonly RiskReference Linkage = new("FINCEN_LINKAGE", "FinCEN FIN-2020-A005 dấu hiệu số 6",
        "https://www.fincen.gov/system/files/advisory/2020-07-30/FinCEN%20Advisory%20Covid%20Cybercrime%20508%20FINAL.pdf",
        "Khuyến cáo trong bối cảnh COVID-19 về nhiều tài khoản có vẻ không liên quan dùng chung thiết bị/IP. Không quy định ngưỡng 3 hoặc 5 người.");
    public static readonly RiskReference Threshold = new("SKLEARN_THRESHOLD", "scikit-learn Tuning the decision threshold",
        "https://scikit-learn.org/stable/modules/classification_threshold.html",
        "Chọn ngưỡng theo mục tiêu trên validation độc lập. Không xác nhận một giá trị chung cho mọi mô hình hoặc nghiệp vụ.");
    public static readonly RiskReference Vietnam = new("VN_AML_2022", "Luật 14/2022/QH15 Điều 28 và 29",
        "https://datafiles.chinhphu.vn/cpp/files/vbpq/2023/4/luat14.pdf",
        "Tham khảo dấu hiệu AML trong bản luật 2022. Dự án không triển khai đầy đủ điều kiện pháp lý và không kết luận rửa tiền.");
    public static IReadOnlyList<RiskReference> All => [Monitoring, Velocity, Statistics, Access, Linkage, Threshold, Vietnam];
    public static IReadOnlyList<RiskReference> For(string code) => code switch
    {
        "RAPID_SEQUENCE" => [Velocity, Monitoring],
        "AMOUNT_DEVIATION" => [Statistics, Monitoring],
        "ACCESS_CHANGE" => [Access],
        "SHARED_DEVICE" or "SHARED_IP" => [Linkage, Access],
        "MODEL_SIGNAL" => [Threshold],
        _ => [Monitoring]
    };
    public static RiskFinding Annotate(RiskFinding finding) => finding with { References = For(finding.Code) };
}
