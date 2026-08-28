using System.Collections.Generic;

namespace SOCYVIA.Services;

/// <summary>Approved future-only physiological terminology; no acquisition claim is implied.</summary>
public static class FuturePhysiologyPresentationService
{
    public static IReadOnlyList<FuturePhysiologyPresentation> Cards(bool arabic) =>
    [
        new(arabic ? "التخطيط الكهربائي للدماغ (EEG)" : "EEG", "OpenBCI"),
        new(arabic ? "الاستجابة الجلدية الكهربائية (GSR / EDA)" : "GSR / EDA", "EmotiBit"),
        new(arabic ? "تتبع العين (Eye Tracking)" : "Eye Tracking", "Pupil Labs")
    ];
}

public sealed record FuturePhysiologyPresentation(string Measurement, string Ecosystem);
