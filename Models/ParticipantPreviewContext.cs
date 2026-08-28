using System;
using System.Collections.Generic;

namespace SOCYVIA.Models;

/// <summary>Researcher-only presentation context with no participant, session, T0, or telemetry identity.</summary>
public sealed record ParticipantPreviewContext(
    string StudyId,
    string StudyTitle,
    string GroupName,
    string ConditionName,
    IReadOnlyList<RuntimePostPresentation> Posts,
    DateTime GeneratedAtUtc);
