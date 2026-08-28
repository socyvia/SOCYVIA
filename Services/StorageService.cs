using System;
using System.IO;

namespace SOCYVIA.Services;

public static class StorageService
{
    // =========================================================
    // ROOT
    // Windows + macOS + Linux
    // =========================================================

    /// <summary>Tests may isolate local state with SOCYVIA_STORAGE_ROOT; production retains the researcher-owned local app-data location.</summary>
    public static string RootPath { get; } = ResolveRootPath();

    private static string ResolveRootPath()
    {
        var isolatedRoot = Environment.GetEnvironmentVariable("SOCYVIA_STORAGE_ROOT");
        return !string.IsNullOrWhiteSpace(isolatedRoot)
            ? Path.GetFullPath(isolatedRoot)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SOCYVIA");
    }


    // =========================================================
    // GLOBAL FOLDERS
    // =========================================================

    public static string ResearchersFolder =>
        Path.Combine(
            RootPath,
            "researchers");


    public static string SettingsFolder =>
        Path.Combine(
            RootPath,
            "settings");


    public static string DatabaseFolder =>
        Path.Combine(
            RootPath,
            "database");


    public static string StudiesFolder =>
        Path.Combine(
            RootPath,
            "studies");


    public static string ExportsFolder =>
        Path.Combine(
            RootPath,
            "exports");


    public static string BackupsFolder =>
        Path.Combine(
            RootPath,
            "backups");


    // =========================================================
    // GLOBAL FILES
    // =========================================================

    public static string DatabaseFile =>
        Path.Combine(
            DatabaseFolder,
            "socyvia.db");


    public static string SettingsFile =>
        Path.Combine(
            SettingsFolder,
            "settings.json");


    public static string ActiveResearcherFile =>
        Path.Combine(
            SettingsFolder,
            "active-researcher.txt");


    public static string LanguageFile =>
        Path.Combine(
            SettingsFolder,
            "language.txt");


    // =========================================================
    // LEGACY PROFILE
    // =========================================================

    public static string LegacyProfileFolder =>
        Path.Combine(
            RootPath,
            "profile");


    public static string LegacyResearcherProfileFile =>
        Path.Combine(
            LegacyProfileFolder,
            "researcher.json");


    // =========================================================
    // RESEARCHER-SPECIFIC PATHS
    // =========================================================

    public static string GetResearcherFolder(
        string researcherId)
    {
        return Path.Combine(
            ResearchersFolder,
            researcherId);
    }


    public static string GetResearcherProfileFile(
        string researcherId)
    {
        return Path.Combine(
            GetResearcherFolder(
                researcherId),
            "profile.json");
    }


    public static string GetResearcherExportsFolder(
        string researcherId)
    {
        return Path.Combine(
            GetResearcherFolder(
                researcherId),
            "exports");
    }


    public static string GetResearcherBackupsFolder(
        string researcherId)
    {
        return Path.Combine(
            GetResearcherFolder(
                researcherId),
            "backups");
    }

    public static string GetResearcherMediaFolder(
        string researcherId)
    {
        return Path.Combine(
            GetResearcherFolder(researcherId),
            "media");
    }


    // =========================================================
    // INITIALIZATION
    // =========================================================

    public static void Initialize()
    {
        Directory.CreateDirectory(
            RootPath);

        Directory.CreateDirectory(
            ResearchersFolder);

        Directory.CreateDirectory(
            SettingsFolder);

        Directory.CreateDirectory(
            DatabaseFolder);

        Directory.CreateDirectory(
            StudiesFolder);

        Directory.CreateDirectory(
            ExportsFolder);

        Directory.CreateDirectory(
            BackupsFolder);
    }


    // =========================================================
    // RESEARCHER WORKSPACE
    // =========================================================

    public static void InitializeResearcherWorkspace(
        string researcherId)
    {
        var researcherFolder =
            GetResearcherFolder(
                researcherId);


        Directory.CreateDirectory(
            researcherFolder);


        Directory.CreateDirectory(
            GetResearcherExportsFolder(
                researcherId));


        Directory.CreateDirectory(
            GetResearcherBackupsFolder(
                researcherId));

        Directory.CreateDirectory(
            GetResearcherMediaFolder(
                researcherId));
    }


    // =========================================================
    // DELETE ONE RESEARCHER LOCAL WORKSPACE
    // =========================================================

    public static bool DeleteResearcherWorkspace(
        string researcherId)
    {
        try
        {
            var folder =
                GetResearcherFolder(
                    researcherId);


            if (Directory.Exists(folder))
            {
                Directory.Delete(
                    folder,
                    recursive: true);
            }


            return true;
        }
        catch
        {
            return false;
        }
    }


    // =========================================================
    // CLEAR ALL RESEARCHER PROFILES
    //
    // DOES NOT DELETE:
    // database
    // studies
    // exports
    // backups
    //
    // Intended for removing researcher accounts only.
    // =========================================================

    public static bool ClearAllResearcherProfiles()
    {
        try
        {
            if (Directory.Exists(
                    ResearchersFolder))
            {
                Directory.Delete(
                    ResearchersFolder,
                    recursive: true);
            }


            Directory.CreateDirectory(
                ResearchersFolder);


            if (File.Exists(
                    ActiveResearcherFile))
            {
                File.Delete(
                    ActiveResearcherFile);
            }


            if (File.Exists(
                    LegacyResearcherProfileFile))
            {
                File.Delete(
                    LegacyResearcherProfileFile);
            }


            return true;
        }
        catch
        {
            return false;
        }
    }


    // =========================================================
    // DEVELOPMENT RESET
    //
    // WARNING:
    // Deletes EVERYTHING stored locally by SOCYVIA:
    // researchers
    // database
    // studies
    // settings
    // exports
    // backups
    //
    // Useful during development/testing.
    // =========================================================

    public static bool ResetAllLocalData()
    {
        try
        {
            if (Directory.Exists(
                    RootPath))
            {
                Directory.Delete(
                    RootPath,
                    recursive: true);
            }


            Initialize();


            return true;
        }
        catch
        {
            return false;
        }
    }
}
