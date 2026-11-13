namespace FraudDetectionService.Domain;

// A finding is an observed signal, never a confirmed fraud subtype.
public sealed record RiskFinding(string Code, string Group, string Title, string Evidence, string Source, string Action,
    IReadOnlyList<RiskReference>? References = null, string ValidationStatus = "Experimental", string InputTrust = "UserProvidedDemo");

public sealed record RiskReference(string Id, string Title, string Url, string Scope);
