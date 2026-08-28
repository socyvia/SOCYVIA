using System;
using System.Collections.Generic;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

public static class QuestionnaireLibraryService
{
    private static readonly string[] Categories =
    [
        "Social / Behavioral", "Media / Communication", "User Experience",
        "Affect / Emotion", "Well-being", "Trust / Credibility",
        "Cognitive / Attention", "Custom"
    ];

    public static IReadOnlyList<string> SupportedCategories => Categories;

    // The curated catalog is deliberately empty until item redistribution is verified.
    public static IReadOnlyList<QuestionnaireLibraryEntry> BuiltInCatalog => [];

    public static void ValidateForBundling(QuestionnaireLibraryEntry entry)
    {
        if (!entry.IncludesItems) return;
        if (entry.LicenseStatus != QuestionnaireLicenseStatuses.BuiltInOpen ||
            string.IsNullOrWhiteSpace(entry.LicenseReference) ||
            entry.RedistributionStatus != QuestionnaireLicenseStatuses.BuiltInOpen)
            throw new InvalidOperationException(
                "Questionnaire items may be bundled only when redistribution rights and their reference are explicitly verified.");
    }
}
