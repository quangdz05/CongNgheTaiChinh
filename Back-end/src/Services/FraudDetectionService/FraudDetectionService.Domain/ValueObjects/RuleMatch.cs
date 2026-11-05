namespace FraudDetectionService.Domain;

public record RuleMatch(string Code, string Title, double Score, string Evidence, bool Critical = false);
