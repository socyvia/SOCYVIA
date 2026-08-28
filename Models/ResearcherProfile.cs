using System;

namespace SOCYVIA.Models;

public class ResearcherProfile
{
    public string Id { get; set; } =
        Guid.NewGuid().ToString();

    public string FullName { get; set; } =
        string.Empty;

    public string? PasswordHash { get; set; }

    public string? PasswordSalt { get; set; }

    public bool RememberMe { get; set; }

    public bool PrivacyAccepted { get; set; }

    public bool OnboardingCompleted { get; set; }

    public bool OnboardingSkipped { get; set; }

    public DateTime CreatedAt { get; set; } =
        DateTime.UtcNow;

    public DateTime LastAccessAt { get; set; } =
        DateTime.UtcNow;
}
