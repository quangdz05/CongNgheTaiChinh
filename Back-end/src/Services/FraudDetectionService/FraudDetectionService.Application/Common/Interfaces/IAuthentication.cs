using FraudDetectionService.Domain;
namespace FraudDetectionService.Application;

public interface IPasswordVerifier
{
    bool Verify(string password, string? encoded);
}
public interface ITokenIssuer
{
    string Create(AppUser user);
}
