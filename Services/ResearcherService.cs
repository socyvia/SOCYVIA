using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

public static class ResearcherService
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 210_000;


    // =========================================================
    // INITIALIZE
    // =========================================================

    public static void Initialize()
    {
        StorageService.Initialize();

        MigrateLegacyProfileIfNeeded();
    }


    // =========================================================
    // LEGACY MIGRATION
    // =========================================================

    private static void MigrateLegacyProfileIfNeeded()
    {
        if (!File.Exists(
                StorageService.LegacyResearcherProfileFile))
        {
            return;
        }


        try
        {
            var json =
                File.ReadAllText(
                    StorageService.LegacyResearcherProfileFile);


            var legacyProfile =
                JsonSerializer.Deserialize<ResearcherProfile>(
                    json);


            if (legacyProfile is null ||
                string.IsNullOrWhiteSpace(
                    legacyProfile.Id))
            {
                return;
            }


            StorageService.InitializeResearcherWorkspace(
                legacyProfile.Id);


            var destination =
                StorageService.GetResearcherProfileFile(
                    legacyProfile.Id);


            if (!File.Exists(
                    destination))
            {
                File.Copy(
                    StorageService.LegacyResearcherProfileFile,
                    destination,
                    overwrite: false);
            }


            if (legacyProfile.RememberMe)
            {
                SetActiveResearcher(
                    legacyProfile.Id);
            }
        }
        catch
        {
            // Never destroy legacy data if migration fails.
        }
    }


    // =========================================================
    // GET ALL PROFILES
    // =========================================================

    public static List<ResearcherProfile> GetProfiles()
    {
        Initialize();


        var profiles =
            new List<ResearcherProfile>();


        if (!Directory.Exists(
                StorageService.ResearchersFolder))
        {
            return profiles;
        }


        foreach (var directory in
                 Directory.GetDirectories(
                     StorageService.ResearchersFolder))
        {
            var profileFile =
                Path.Combine(
                    directory,
                    "profile.json");


            if (!File.Exists(
                    profileFile))
            {
                continue;
            }


            try
            {
                var json =
                    File.ReadAllText(
                        profileFile);


                var profile =
                    JsonSerializer.Deserialize<ResearcherProfile>(
                        json);


                if (profile is not null)
                {
                    profiles.Add(
                        profile);
                }
            }
            catch
            {
                // Ignore damaged profile.
            }
        }


        return profiles
            .OrderByDescending(
                profile =>
                    profile.LastAccessAt)
            .ToList();
    }


    // =========================================================
    // GET PROFILE
    // =========================================================

    public static ResearcherProfile? GetProfile(
        string researcherId)
    {
        Initialize();


        var file =
            StorageService.GetResearcherProfileFile(
                researcherId);


        if (!File.Exists(file))
        {
            return null;
        }


        try
        {
            var json =
                File.ReadAllText(
                    file);


            return JsonSerializer
                .Deserialize<ResearcherProfile>(
                    json);
        }
        catch
        {
            return null;
        }
    }


    // =========================================================
    // CREATE PROFILE
    // =========================================================

    public static ResearcherProfile CreateProfile(
        string fullName,
        string? password,
        bool rememberMe,
        bool privacyAccepted)
    {
        Initialize();


        if (!privacyAccepted)
        {
            throw new InvalidOperationException(
                "Privacy policy must be accepted before creating a researcher profile.");
        }


        fullName =
            fullName.Trim();


        var profile =
            new ResearcherProfile
            {
                Id =
                    Guid.NewGuid().ToString(),

                FullName =
                    fullName,

                RememberMe =
                    rememberMe,

                PrivacyAccepted =
                    true,

                CreatedAt =
                    DateTime.UtcNow,

                LastAccessAt =
                    DateTime.UtcNow
            };


        if (!string.IsNullOrWhiteSpace(
                password))
        {
            CreatePassword(
                password,
                out var hash,
                out var salt);


            profile.PasswordHash =
                hash;


            profile.PasswordSalt =
                salt;
        }


        SaveProfile(
            profile);


        if (rememberMe)
        {
            SetActiveResearcher(
                profile.Id);
        }


        return profile;
    }


    // =========================================================
    // SAVE PROFILE
    // =========================================================

    public static void SaveProfile(
        ResearcherProfile profile)
    {
        StorageService.InitializeResearcherWorkspace(
            profile.Id);


        var options =
            new JsonSerializerOptions
            {
                WriteIndented =
                    true
            };


        var json =
            JsonSerializer.Serialize(
                profile,
                options);


        File.WriteAllText(
            StorageService.GetResearcherProfileFile(
                profile.Id),
            json);
    }


    // =========================================================
    // PASSWORD EXISTS
    // =========================================================

    public static bool HasPassword(
        ResearcherProfile profile)
    {
        return
            !string.IsNullOrWhiteSpace(
                profile.PasswordHash)
            &&
            !string.IsNullOrWhiteSpace(
                profile.PasswordSalt);
    }


    // =========================================================
    // VERIFY PASSWORD
    // =========================================================

    public static bool VerifyPassword(
        ResearcherProfile profile,
        string? password)
    {
        if (!HasPassword(
                profile))
        {
            return true;
        }


        if (string.IsNullOrWhiteSpace(
                password))
        {
            return false;
        }


        try
        {
            var salt =
                Convert.FromBase64String(
                    profile.PasswordSalt!);


            var expectedHash =
                Convert.FromBase64String(
                    profile.PasswordHash!);


            var actualHash =
                Rfc2898DeriveBytes.Pbkdf2(
                    password,
                    salt,
                    Iterations,
                    HashAlgorithmName.SHA256,
                    HashSize);


            return CryptographicOperations
                .FixedTimeEquals(
                    expectedHash,
                    actualHash);
        }
        catch
        {
            return false;
        }
    }


    // =========================================================
    // UPDATE ACCESS
    // =========================================================

    public static void UpdateLastAccess(
        ResearcherProfile profile,
        bool rememberMe)
    {
        profile.LastAccessAt =
            DateTime.UtcNow;


        profile.RememberMe =
            rememberMe;


        SaveProfile(
            profile);


        if (rememberMe)
        {
            SetActiveResearcher(
                profile.Id);
        }
        else
        {
            ClearActiveResearcher();
        }
    }


    // =========================================================
    // DELETE ONE RESEARCHER
    // =========================================================

    public static bool DeleteResearcher(
        string researcherId)
    {
        var activeId =
            GetActiveResearcherId();


        if (string.Equals(
                activeId,
                researcherId,
                StringComparison.Ordinal))
        {
            ClearActiveResearcher();
        }


        return StorageService
            .DeleteResearcherWorkspace(
                researcherId);
    }


    // =========================================================
    // CLEAR ALL RESEARCHER PROFILES
    // =========================================================

    public static bool ClearAllResearchers()
    {
        ClearActiveResearcher();


        return StorageService
            .ClearAllResearcherProfiles();
    }


    // =========================================================
    // DEVELOPMENT RESET
    // =========================================================

    public static bool ResetApplicationData()
    {
        return StorageService
            .ResetAllLocalData();
    }


    // =========================================================
    // ACTIVE RESEARCHER
    // =========================================================

    public static void SetActiveResearcher(
        string researcherId)
    {
        StorageService.Initialize();


        File.WriteAllText(
            StorageService.ActiveResearcherFile,
            researcherId);
    }


    public static string? GetActiveResearcherId()
    {
        StorageService.Initialize();


        if (!File.Exists(
                StorageService.ActiveResearcherFile))
        {
            return null;
        }


        try
        {
            var id =
                File.ReadAllText(
                        StorageService.ActiveResearcherFile)
                    .Trim();


            return string.IsNullOrWhiteSpace(
                    id)
                ? null
                : id;
        }
        catch
        {
            return null;
        }
    }


    public static void ClearActiveResearcher()
    {
        try
        {
            if (File.Exists(
                    StorageService.ActiveResearcherFile))
            {
                File.Delete(
                    StorageService.ActiveResearcherFile);
            }
        }
        catch
        {
            // Non-critical.
        }
    }


    // =========================================================
    // PASSWORD HASHING
    // =========================================================

    private static void CreatePassword(
        string password,
        out string hash,
        out string salt)
    {
        var saltBytes =
            RandomNumberGenerator.GetBytes(
                SaltSize);


        var hashBytes =
            Rfc2898DeriveBytes.Pbkdf2(
                password,
                saltBytes,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSize);


        salt =
            Convert.ToBase64String(
                saltBytes);


        hash =
            Convert.ToBase64String(
                hashBytes);
    }
}