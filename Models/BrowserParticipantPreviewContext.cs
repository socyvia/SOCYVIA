using System;
using System.Collections.Generic;

namespace SOCYVIA.Models;

/// <summary>
/// A loopback-only researcher preview package. It intentionally contains no
/// participant, session, T0, telemetry, or deployment identity.
/// </summary>
public sealed record BrowserParticipantPreviewContext(
    string Ticket,
    BrowserPreviewEntry Entry,
    IReadOnlyDictionary<string, BrowserPreviewQuestionnaire> Questionnaires,
    IReadOnlyList<BrowserPreviewContentItem> Content,
    IReadOnlyDictionary<string, string> LocalMediaPaths,
    DateTime GeneratedAtUtc);

public sealed record BrowserPreviewEntry(
    string DeploymentPublicId,
    string DefaultRuntimeLanguage,
    IReadOnlyList<string> InterfaceLanguages,
    BrowserPreviewStudy Study,
    BrowserPreviewFlow ParticipantFlow,
    bool Preview = true);

public sealed record BrowserPreviewStudy(
    LocalizedPreviewText Title,
    LocalizedPreviewText Description,
    LocalizedPreviewText Instructions,
    LocalizedPreviewText Privacy,
    LocalizedPreviewText ConsentText,
    int? EstimatedDurationMinutes);

public sealed record BrowserPreviewFlow(bool PreQuestionnaire, bool PostQuestionnaire);

public sealed record LocalizedPreviewText(string En, string Ar);

public sealed record BrowserPreviewQuestionnaire(
    string Id,
    string VersionId,
    string Stage,
    LocalizedPreviewText Title,
    LocalizedPreviewText Description,
    LocalizedPreviewText Instructions,
    bool Required,
    IReadOnlyList<BrowserPreviewQuestionnaireItem> Items,
    string SchemaVersion = "SOCYVIA.Questionnaire/1");

public sealed record BrowserPreviewQuestionnaireItem(
    string Id,
    string Type,
    LocalizedPreviewText Question,
    LocalizedPreviewText Description,
    bool Required,
    int Order,
    object Configuration);

public sealed record BrowserPreviewContentItem(
    string ContentId,
    string? Title,
    string Body,
    BrowserPreviewInteractions Interactions,
    BrowserPreviewMedia? Media = null);

public sealed record BrowserPreviewInteractions(
    bool Like = true,
    bool Comment = true,
    bool ReadMore = true,
    bool Save = true,
    bool Share = true,
    bool CollectCommentText = false);

public sealed record BrowserPreviewMedia(string Url, string Kind, string? Alt = null);
