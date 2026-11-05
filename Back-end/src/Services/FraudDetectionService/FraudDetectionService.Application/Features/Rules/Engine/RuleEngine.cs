using FraudDetectionService.Domain.Repositories;
using System.Diagnostics;
using System.Text.Json;
using FraudDetectionService.Domain;
namespace FraudDetectionService.Application;

public static class RuleEngine
{
    public static List<RuleMatch> Evaluate(FeatureVector f, RulePolicy p)
    {
        var r = new List<RuleMatch>();
        if (p.AmountEnabled && f.Amount >= (double)p.CriticalAmount)
            r.Add(new("AMOUNT_CRITICAL", "Vượt hạn mức nghiêm trọng", 1, $"{f.Amount:N0} VND ≥ {p.CriticalAmount:N0} VND", true));
        else if (p.AmountEnabled && f.Amount >= (double)p.HighAmount)
            r.Add(new("AMOUNT_HIGH", "Giá trị giao dịch cao", .7, $"{f.Amount:N0} VND ≥ {p.HighAmount:N0} VND"));
        if (p.VelocityEnabled && f.TransactionCount1h >= p.VelocityCount)
            r.Add(new("VELOCITY", "Tần suất bất thường", .85, $"{f.TransactionCount1h} giao dịch trước đó trong một giờ"));
        if (p.UnusualEnabled && f.TransactionCount24h >= 3 && f.AmountZScore >= 2.5)
            r.Add(new("AMOUNT_Z", "Lệch lịch sử chi tiêu", f.AmountZScore >= 4 ? .9 : .6, $"z-score = {f.AmountZScore:F2}"));
        if (p.DeviceEnabled && f.SharedDeviceUsers >= p.SharedDeviceUsers)
            r.Add(new("SHARED_DEVICE", "Thiết bị dùng chung", .9, $"{f.SharedDeviceUsers} người dùng đã sử dụng thiết bị trong 24 giờ"));
        if (p.DeviceEnabled && f.SharedIpUsers >= 5)
            r.Add(new("SHARED_IP", "IP dùng chung", .55, $"{f.SharedIpUsers} người dùng trong 24 giờ; có thể là mạng dùng chung hợp lệ"));
        if (p.DeviceEnabled && f.TransactionCount24h >= 3 && f.IsNewDevice == 1 && f.IsNewLocation == 1)
            r.Add(new("NEW_CONTEXT", "Thiết bị và quốc gia mới", .8, "Thay đổi đồng thời trên tài khoản đã có lịch sử"));
        if (f.HourOfDay < 6 && f.Amount >= 50_000_000)
            r.Add(new("NIGHT_VALUE", "Giá trị cao ban đêm", .7, "00:00–05:59 UTC và từ 50 triệu VND"));
        if (f.MerchantRisk >= .8)
            r.Add(new("MERCHANT", "Nhóm merchant rủi ro theo cấu hình", .65, "Giả định demo, không phải kết luận gian lận"));
        return r;
    }
}
