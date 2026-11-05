namespace FraudDetectionService.Domain;

public sealed class AppUser
{
    public int Id
    {
        get; set;
    }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Analyst";
}
