using FraudDetectionService.Domain;
using System.Globalization;

namespace FraudDetectionService.Application;

public static class RiskSignalAnalyzer
{
    public static List<RiskFinding> Analyze(Transaction tx, IEnumerable<Transaction> history, FeatureVector f, RulePolicy p)
    {
        var findings = new List<RiskFinding>();
        void Add(string code, string group, string title, string evidence, string action = "REVIEW") =>
            findings.Add(new(code, group, title, evidence, "rule", action));
        var prior = history.Where(t => t.Timestamp < tx.Timestamp && t.Timestamp >= tx.Timestamp.AddHours(-24)).ToList();
        var own = prior.Where(t => t.UserId == tx.UserId).OrderBy(t => t.Timestamp).ToList();
        var mature = own.Count >= p.MinimumHistory;
        if (!mature)
            findings.Add(new("LIMITED_HISTORY", "data_quality", "Chưa đủ lịch sử cá nhân",
                $"Có {own.Count}/{p.MinimumHistory} giao dịch trước đó trong 24 giờ. Chưa đánh giá độ lệch giá trị theo lịch sử cá nhân; vẫn kiểm tra các dấu hiệu khác.", "data", "INFORMATION"));

        if (p.AmountEnabled && tx.Amount >= p.HighAmount)
            Add("BUSINESS_LIMIT", "value", "Vượt hạn mức kiểm tra nghiệp vụ",
                $"Giá trị {tx.Amount:N0} VND ≥ hạn mức do quản trị viên đặt {p.HighAmount:N0} VND. Đây là hạn mức vận hành, chưa phải bằng chứng gian lận.");
        if (p.UnusualEnabled && mature && f.AmountZScore >= p.AmountDeviationThreshold)
            Add("AMOUNT_DEVIATION", "value", "Giá trị lệch lịch sử cá nhân",
                $"z-score = {f.AmountZScore.ToString("F2", CultureInfo.InvariantCulture)} ≥ {p.AmountDeviationThreshold}; tham chiếu {own.Count} giao dịch trước đó trong 24 giờ. Cần kiểm tra mục đích giao dịch.");

        if (p.VelocityEnabled)
        {
            var start = tx.Timestamp.AddSeconds(-p.BurstWindowSeconds);
            var count = own.Count(t => t.Timestamp >= start) + 1;
            var baseline = own.Where(t => t.Timestamp < start).ToList();
            var peak = PeakCount(baseline, p.BurstWindowSeconds);
            var enoughBaseline = baseline.Count >= p.MinimumHistory;
            if (count >= p.BurstCount && (!enoughBaseline || count > peak))
                Add("RAPID_SEQUENCE", "frequency", "Chuỗi giao dịch liên tiếp",
                    $"{count} giao dịch trong {p.BurstWindowSeconds} giây (gồm giao dịch này), ngưỡng cấu hình {p.BurstCount}. " +
                    (enoughBaseline ? $"Cao hơn mức cao nhất {peak} giao dịch/cửa sổ trong phần lịch sử 24 giờ trước chuỗi hiện tại."
                    : $"Chỉ có {baseline.Count}/{p.MinimumHistory} giao dịch tham chiếu trước chuỗi; chưa đủ cơ sở kết luận tần suất bất thường."));
        }
        if (p.DeviceEnabled)
        {
            if (own.Count > 0 && f.IsNewDevice == 1 && f.IsNewLocation == 1)
                Add("ACCESS_CHANGE", "context", "Đổi thiết bị và quốc gia",
                    $"Cả thiết bị và quốc gia {tx.Country} chưa xuất hiện trong {own.Count} giao dịch trước đó của người dùng trong 24 giờ; cần xác minh thay đổi truy cập.");
            var deviceUsers = prior.Where(t => t.DeviceHash == tx.DeviceHash).Select(t => t.UserId).Append(tx.UserId).Distinct().Count();
            var ipUsers = prior.Where(t => t.IpHash == tx.IpHash).Select(t => t.UserId).Append(tx.UserId).Distinct().Count();
            if (deviceUsers >= p.SharedDeviceUsers)
                Add("SHARED_DEVICE", "linkage", "Nhiều người dùng chung thiết bị",
                    $"{deviceUsers} người dùng phân biệt dùng cùng mã thiết bị trong 24 giờ (gồm người dùng này), ngưỡng {p.SharedDeviceUsers}. Cần xác minh quan hệ giữa các tài khoản.");
            if (ipUsers >= p.SharedIpUsers)
                Add("SHARED_IP", "linkage", "Nhiều người dùng chung IP",
                    $"{ipUsers} người dùng phân biệt dùng cùng IP trong 24 giờ, ngưỡng {p.SharedIpUsers}. IP chung có thể do mạng công cộng hoặc NAT; dấu hiệu này riêng lẻ chỉ để theo dõi.", "OBSERVE");
        }
        return findings.Select(RiskRuleReferences.Annotate).ToList();
    }

    public static List<RiskFinding> WithModel(IEnumerable<RiskFinding> rules, AiPrediction? prediction, string mode, RulePolicy p)
    {
        var findings = rules.Where(f => f.Source != "model").ToList();
        if (mode != "Hybrid") return findings;
        if (prediction == null)
            findings.Add(new("AI_UNAVAILABLE", "data_quality", "Chưa nhận được đánh giá AI", "Đã chọn Hybrid nhưng AI không khả dụng. Chuyển kiểm tra thủ công.", "model", "REVIEW"));
        else if (prediction.Score >= p.ReviewThreshold)
            findings.Add(new("MODEL_SIGNAL", "model", "Điểm mô hình vượt ngưỡng kiểm tra",
                $"Điểm AI {prediction.Score:F4} ≥ ngưỡng cấu hình {p.ReviewThreshold:F4}. Mô hình {prediction.ModelVersion} chỉ phân loại nhị phân, không xác định loại gian lận. Điểm chưa được hiệu chuẩn cho vận hành thực tế.", "model", "REVIEW"));
        return findings.Select(f => f.References == null ? RiskRuleReferences.Annotate(f) : f).ToList();
    }

    public static string Decision(IEnumerable<RiskFinding> findings) => findings.Any(f => f.Action == "REVIEW") ? "REVIEW" : "ALLOW";

    private static int PeakCount(List<Transaction> rows, int seconds)
    {
        var peak = 0;
        for (int left = 0, right = 0; right < rows.Count; right++)
        {
            while (rows[left].Timestamp < rows[right].Timestamp.AddSeconds(-seconds)) left++;
            peak = Math.Max(peak, right - left + 1);
        }
        return peak;
    }
}
