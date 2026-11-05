using FraudDetectionService.Domain.Repositories;
namespace FraudDetectionService.Application.Features.Auth.Commands;

public record LoginCommand(string Username, string Password);
public record LoginResult(string Token, string Username, string Role, int ExpiresIn = 3600);
public sealed class LoginCommandHandler(IFraudRepository repository, IPasswordVerifier passwords, ITokenIssuer tokens)
{
    public async Task<LoginResult?> Handle(LoginCommand command, CancellationToken ct)
    {
        var user = await repository.UserAsync(command.Username.Trim().ToLowerInvariant(), ct);
        var valid = passwords.Verify(command.Password, user?.PasswordHash);
        return user == null || !valid ? null : new(tokens.Create(user), user.Username, user.Role);
    }
}
