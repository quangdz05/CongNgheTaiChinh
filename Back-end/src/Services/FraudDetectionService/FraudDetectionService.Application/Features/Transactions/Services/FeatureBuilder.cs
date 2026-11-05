using FraudDetectionService.Domain.Repositories;
using System.Diagnostics;
using System.Text.Json;
using FraudDetectionService.Domain;
namespace FraudDetectionService.Application;

public static class FeatureBuilder
{
    public static FeatureVector Build(Transaction tx, IReadOnlyList<Transaction> history)
    {
        var prior = history.Where(h => h.Timestamp < tx.Timestamp && h.Timestamp >= tx.Timestamp.AddHours(-24)).ToList();
        var user = prior.Where(h => h.UserId == tx.UserId).ToList();
        var amounts = user.Select(h => (double)h.Amount).ToList();
        var mean = amounts.Count == 0 ? (double)tx.Amount : amounts.Average();
        var std = amounts.Count < 2 ? 1 : Math.Sqrt(amounts.Average(a => Math.Pow(a - mean, 2)));
        return new((double)tx.Amount, user.Count(h => h.Timestamp >= tx.Timestamp.AddHours(-1)), user.Count,
            Math.Clamp(((double)tx.Amount - mean) / Math.Max(std, 1), -10, 10),
            user.Any(h => h.DeviceHash == tx.DeviceHash) ? 0 : 1,
            user.Any(h => h.Country == tx.Country) ? 0 : 1,
            prior.Where(h => h.DeviceHash == tx.DeviceHash).Select(h => h.UserId).Distinct().Count(),
            prior.Where(h => h.IpHash == tx.IpHash).Select(h => h.UserId).Distinct().Count(),
            tx.Timestamp.UtcDateTime.Hour, tx.Category switch
            {
                "gambling" => 1,
                "crypto" => .9,
                "jewelry" => .75,
                "electronics" => .65,
                "travel" => .55,
                _ => .1
            });
    }
}
