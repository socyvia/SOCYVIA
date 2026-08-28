using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.IO.Compression;
using SOCYVIA.Data;
using SOCYVIA.Models;
using SOCYVIA.Repositories;
using SOCYVIA.Services;

var isolatedStorage = Path.Combine(Path.GetTempPath(), "socyvia-pilot-propagation-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("SOCYVIA_STORAGE_ROOT", isolatedStorage);
StorageService.Initialize();
ResearcherService.Initialize();
if (ResearcherService.GetProfiles().Count != 0 || ResearcherService.GetActiveResearcherId() is not null)
    throw new Exception("A clean startup state must remain a valid first-launch state.");
if (await new CloudflareProviderConfigurationStore().LoadAsync() is not null)
    throw new Exception("Clean startup unexpectedly required Cloudflare metadata.");
if (SocyviaAiGatewayConfiguration.LoadManagedConfiguration()?.Endpoint != SocyviaAiGatewayConfiguration.ProductionEndpoint ||
    await ResearchInterpretationProviderFactory.CreateConfiguredAsync() is not null)
    throw new Exception("Clean startup did not preserve the managed AI boundary and normal no-authorization state.");

// Reproduce a real pre-RunType researcher cache. Startup must migrate it
// additively before creating the RunType index, without losing its row.
await using (var legacyConnection = await DatabaseService.OpenConnectionAsync())
{
    await using var legacy = legacyConnection.CreateCommand();
    legacy.CommandText = """
        CREATE TABLE RemoteParticipantSessions(
            SessionId TEXT PRIMARY KEY, ParticipantId TEXT NOT NULL,
            DeploymentId TEXT NOT NULL, ConditionId TEXT NOT NULL,
            StartedAtUtc TEXT, FeedEndedAtUtc TEXT, PostCompletedAtUtc TEXT,
            CompletedAtUtc TEXT, CompletionState TEXT NOT NULL,
            LifecycleState TEXT NOT NULL, LastSyncedAtUtc TEXT NOT NULL);
        INSERT INTO RemoteParticipantSessions(
            SessionId,ParticipantId,DeploymentId,ConditionId,CompletionState,LifecycleState,LastSyncedAtUtc)
        VALUES('legacy-session','legacy-participant','legacy-deployment','legacy-condition','CompletedEligible','Completed','2026-01-01T00:00:00Z');
        """;
    await legacy.ExecuteNonQueryAsync();
}
await DatabaseInitializer.InitializeAsync();
await using (var migratedConnection = await DatabaseService.OpenConnectionAsync())
{
    await using var migrated = migratedConnection.CreateCommand();
    migrated.CommandText = "SELECT RunType FROM RemoteParticipantSessions WHERE SessionId='legacy-session';";
    if (!string.Equals(await migrated.ExecuteScalarAsync() as string, "Main", StringComparison.Ordinal))
        throw new Exception("Startup did not add RunType safely or preserve the existing remote cache row.");
    migrated.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_RemoteSessions_RunType';";
    if (Convert.ToInt32(await migrated.ExecuteScalarAsync()) != 1)
        throw new Exception("Startup did not create the RunType index after the additive migration.");

    migrated.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ContentItems') WHERE name='PublishedMediaUrl';";
    if (Convert.ToInt32(await migrated.ExecuteScalarAsync()) != 1)
        throw new Exception("The additive local schema does not persist participant-facing media URLs.");

    migrated.CommandText = "INSERT INTO Researchers(Id,FullName,CreatedAtUtc,LastAccessAtUtc) VALUES('media-researcher','Media Researcher','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');";
    await migrated.ExecuteNonQueryAsync();
}
var persistedMedia = new ContentItem
{
    Id = "media-content", ResearcherId = "media-researcher", Title = "Media",
    ContentType = "Image", MediaPath = "C:\\preview\\image.png",
    PublishedMediaUrl = "https://socyvia.com/experimentfeed/demo-media/sport-supporters.jpg"
};
await ContentItemRepository.CreateAsync(persistedMedia);
if ((await ContentItemRepository.GetByIdAsync(persistedMedia.Id))?.PublishedMediaUrl != persistedMedia.PublishedMediaUrl)
    throw new Exception("The Content & Media repository did not preserve the published media URL independently of the local preview source.");

var snapshot = new ExperimentConfigurationSnapshot
{
    StudyId = "study-1", StudyDesign = "BetweenSubjects", AssignmentMethod = "BalancedRandom",
    RandomizationSeed = 42, RandomizationAlgorithm = "SOCYVIA.SplitMix64/1", ConsentRequired = true,
    UsesStimuli = true, QuestionnaireModuleEnabled = true,
    Stimuli = [new SnapshotStimulus { StimulusId = "stimulus-1", ContentItemId = "content-1", PresentationOrder = 0, ContentType = "Image", MediaPath = "Assets/Branding/socyvia-mark.png" }]
};
var groups = new[] { new StudyGroup { Id = "group-1", StudyId = "study-1", Name = "Group", SortOrder = 0 } };
var conditions = new[] { new ExperimentalCondition { Id = "condition-1", StudyId = "study-1", GroupId = "group-1", Name = "Condition", SortOrder = 0 } };
var created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
var first = RemoteExperimentFoundationService.BuildPackage(snapshot, groups, conditions, createdAtUtc: created);
var second = RemoteExperimentFoundationService.BuildPackage(snapshot, groups, conditions, createdAtUtc: created);
if (first.ConfigurationHash != second.ConfigurationHash) throw new Exception("Configuration hash is not reproducible.");
if (first.MediaManifest.Count != 1 || string.IsNullOrWhiteSpace(first.MediaManifest[0].Sha256)) throw new Exception("Media manifest did not retain local integrity evidence.");
var deployment = RemoteExperimentFoundationService.CreateDraftDeployment(first, "researcher", "experiment", created);
if (deployment.Status != ExperimentDeploymentStatus.Draft || deployment.ExperimentPackageId != first.ExperimentPackageId || deployment.ConfigurationHash != first.ConfigurationHash) throw new Exception("Deployment does not preserve the immutable package boundary.");
var remoteEvent = new RemoteTelemetryEvent { EventId = "event-1", ParticipantId = "p", SessionId = "s", StudyId = "study-1", DeploymentId = deployment.DeploymentId, ConditionId = "condition-1", EventType = "ContentEnteredViewport", ClientTimestampUtc = created };
if (remoteEvent.SchemaVersion != "SOCYVIA.RemoteTelemetry/1" || remoteEvent.EventId != "event-1") throw new Exception("Remote telemetry identity contract is invalid.");

var preview = new BrowserParticipantPreviewContext(
    "previewtestticket000000000001",
    new BrowserPreviewEntry(
        "preview-only",
        "en",
        ["en"],
        new BrowserPreviewStudy(
            new LocalizedPreviewText("Preview", "معاينة"),
            new LocalizedPreviewText("Description", "وصف"),
            new LocalizedPreviewText("Instructions", "تعليمات"),
            new LocalizedPreviewText("Privacy", "خصوصية"),
            new LocalizedPreviewText("Consent", "موافقة"),
            5),
        new BrowserPreviewFlow(false, false)),
    new Dictionary<string, BrowserPreviewQuestionnaire>(),
    Array.Empty<BrowserPreviewContentItem>(),
    new Dictionary<string, string>(),
    created);

using var host = await BrowserParticipantPreviewService.StartHostAsync(preview);
if (!host.IsRunning || host.PreviewUri.Host != "127.0.0.1" || host.PreviewUri.Scheme != "http") throw new Exception("Loopback preview host did not produce a valid local URL.");
using var client = new HttpClient();
var page = await client.GetAsync(host.PreviewUri);
if (page.StatusCode != HttpStatusCode.OK || !(await page.Content.ReadAsStringAsync()).Contains("app.js", StringComparison.Ordinal)) throw new Exception("Preview web renderer was not served.");
var entry = await client.GetAsync(new Uri(host.PreviewUri, $"/experimentfeed/api/preview/{preview.Ticket}/entry"));
if (entry.StatusCode != HttpStatusCode.OK) throw new Exception("Preview entry GET was not served.");
var writeAttempt = await client.PostAsync(new Uri(host.PreviewUri, $"/experimentfeed/api/preview/{preview.Ticket}/entry"), new StringContent("{}"));
if (writeAttempt.StatusCode != HttpStatusCode.MethodNotAllowed) throw new Exception("Preview host accepted a research write request.");
var localMediaPath = Path.GetFullPath("Assets/Branding/socyvia-mark.png");
if (!File.Exists(localMediaPath)) throw new Exception("Preview fixture media is unavailable.");
var previewWithMedia = preview with
{
    Content = [new BrowserPreviewContentItem("content-1", "Local image", "Preview media", new BrowserPreviewInteractions(), new BrowserPreviewMedia($"/experimentfeed/api/preview/{preview.Ticket}/media/local-image", "image"))],
    LocalMediaPaths = new Dictionary<string, string> { ["local-image"] = localMediaPath }
};
using var mediaHost = await BrowserParticipantPreviewService.StartHostAsync(previewWithMedia);
var mediaResponse = await client.GetAsync(new Uri(mediaHost.PreviewUri, $"/experimentfeed/api/preview/{preview.Ticket}/media/local-image"));
if (mediaResponse.StatusCode != HttpStatusCode.OK || mediaResponse.Content.Headers.ContentType?.MediaType != "image/png" || (await mediaResponse.Content.ReadAsByteArrayAsync()).Length == 0)
    throw new Exception("Preview host did not resolve a managed local media asset.");
host.Dispose();
using var restartedHost = await BrowserParticipantPreviewService.StartHostAsync(preview);
if (!restartedHost.IsRunning) throw new Exception("A later preview could not start after host cleanup.");
Uri? launchedPreviewUri = null;
var openedPreviewUri = await BrowserParticipantPreviewService.OpenContextAsync(preview, uri => launchedPreviewUri = uri);
if (launchedPreviewUri is null || launchedPreviewUri != openedPreviewUri || launchedPreviewUri.Host != "127.0.0.1") throw new Exception("Preview browser launch did not receive the valid loopback URL.");

var synchronizedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
var syncPublication = new ExperimentDeployment { StudyId = "sync-study", DeploymentId = "deployment", Status = ExperimentDeploymentStatus.Published, ResearcherHandle = "sync-researcher", ExperimentCode = "87654321", ConfigurationHash = "sync-hash", DeploymentVersion = 1, PublishedAtUtc = synchronizedAt };
await PublishedExperimentStatusStore.SaveAsync(syncPublication, new Uri("https://runtime.example/experimentfeed/sync-researcher/87654321"));
var mainSession = new RemoteParticipantSessionContract { SessionId = "sync-main", ParticipantId = "participant-main", DeploymentId = "deployment", ConditionId = "condition-main", GroupId = "group-main", RunType = ExperimentRunType.Main, StartedAtUtc = synchronizedAt, FeedEndedAtUtc = synchronizedAt.AddMinutes(4), PostQuestionnaireCompletedAtUtc = synchronizedAt.AddMinutes(4.5), CompletedAtUtc = synchronizedAt.AddMinutes(5), CompletionState = RemoteParticipantCompletionState.CompletedEligible, LifecycleState = RemoteParticipantLifecycleState.Completed };
var pilotSession = new RemoteParticipantSessionContract { SessionId = "sync-pilot", ParticipantId = "participant-pilot", StudyId = "sync-study", DeploymentId = "deployment", ConditionId = "condition-pilot", GroupId = "group-pilot", RunType = ExperimentRunType.Pilot, StartedAtUtc = synchronizedAt, CompletedAtUtc = synchronizedAt.AddMinutes(6), CompletionState = RemoteParticipantCompletionState.CompletedEligible, LifecycleState = RemoteParticipantLifecycleState.Completed };
var mainEvent = new RemoteTelemetryEvent { EventId = "main-event", ParticipantId = mainSession.ParticipantId, SessionId = mainSession.SessionId, DeploymentId = "deployment", ConditionId = mainSession.ConditionId, ContentId = "content-main", EventType = "like", ClientTimestampUtc = synchronizedAt.AddMinutes(2), ClientRelativeMilliseconds = 120000 };
var pilotEvent = new RemoteTelemetryEvent { EventId = "pilot-event", ParticipantId = pilotSession.ParticipantId, SessionId = pilotSession.SessionId, StudyId = "sync-study", DeploymentId = "deployment", ConditionId = pilotSession.ConditionId, EventType = "ContentEnteredViewport", ClientTimestampUtc = synchronizedAt, ClientRelativeMilliseconds = 100 };
var preResponse = new RemoteQuestionnaireResponseContract { ResponseId = "pre-response", DeploymentId = "deployment", ParticipantId = mainSession.ParticipantId, QuestionnaireId = "pre", QuestionnaireVersionId = "pre-v1", Stage = QuestionnaireStage.Pre, ResponseJson = "{\"pre-score\":3}", SubmittedAtUtc = synchronizedAt.AddMinutes(1) };
var postResponse = new RemoteQuestionnaireResponseContract { ResponseId = "post-response", DeploymentId = "deployment", ParticipantId = mainSession.ParticipantId, SessionId = mainSession.SessionId, QuestionnaireId = "post", QuestionnaireVersionId = "post-v1", Stage = QuestionnaireStage.Post, ResponseJson = "{\"post-score\":4}", SubmittedAtUtc = synchronizedAt.AddMinutes(4.5) };
var pilotResponse = new RemoteQuestionnaireResponseContract { ResponseId = "pilot-response", DeploymentId = "deployment", ParticipantId = pilotSession.ParticipantId, SessionId = pilotSession.SessionId, QuestionnaireId = "post", QuestionnaireVersionId = "post-v1", Stage = QuestionnaireStage.Post, ResponseJson = "{\"q\":\"1\"}", SubmittedAtUtc = synchronizedAt.AddMinutes(6) };
var associatedPull = await RemoteResearchSynchronizationService.AssociateStudyIdsAsync(
    new RemoteSyncPullResult(new RemoteSyncCursor("one"), [mainSession, pilotSession], [mainEvent, pilotEvent], [preResponse, postResponse, pilotResponse]));
if (associatedPull.Sessions.Single(item => item.SessionId == mainSession.SessionId).StudyId != "sync-study" ||
    associatedPull.Events.Single(item => item.EventId == mainEvent.EventId).StudyId != "sync-study")
    throw new Exception("Remote synchronization did not restore authoritative study/deployment association.");
await RemoteResearchRepository.ImportAsync(associatedPull);
var allRuns = await RemoteResearchRepository.GetSessionsAsync(studyId: "sync-study");
var mainRuns = await RemoteResearchRepository.GetSessionsAsync(studyId: "sync-study", runType: ExperimentRunType.Main);
var pilotRuns = await RemoteResearchRepository.GetSessionsAsync(studyId: "sync-study", runType: ExperimentRunType.Pilot);
if (allRuns.Count != 2 || mainRuns.Count != 1 || pilotRuns.Count != 1 || pilotRuns[0].RunType != ExperimentRunType.Pilot) throw new Exception("Run-type cache filters did not preserve All/Main/Pilot composition.");
if ((await RemoteResearchRepository.GetEventsAsync(studyId: "sync-study", runType: ExperimentRunType.Pilot)).Single().RunType != ExperimentRunType.Pilot ||
    (await RemoteResearchRepository.GetQuestionnaireResponsesAsync(QuestionnaireStage.Post, studyId: "sync-study", runType: ExperimentRunType.Pilot)).Single().RunType != ExperimentRunType.Pilot)
    throw new Exception("Session-linked export query boundaries did not resolve Pilot provenance.");
var synchronizedDataset = await RemoteAnalysisDatasetService.BuildCompletedEligibleAsync("sync-study", ExperimentRunType.Main);
var preCsv = await RemoteResearchExportService.ExportQuestionnaireCsvAsync(QuestionnaireStage.Pre, studyId: "sync-study", completedOnly: true);
var postCsv = await RemoteResearchExportService.ExportQuestionnaireCsvAsync(QuestionnaireStage.Post, studyId: "sync-study", completedOnly: true);
var behaviorCsv = await RemoteResearchExportService.ExportBehavioralEventsCsvAsync(studyId: "sync-study", completedOnly: true);
var sessionCsv = await RemoteResearchExportService.ExportSessionsCsvAsync(studyId: "sync-study");
var synchronizedQuality = DataQualityService.Evaluate(synchronizedDataset);
var synchronizedReport = ResearchReportService.Build(new Study { Id = "sync-study", Title = "Synchronized QA study" }, synchronizedDataset, synchronizedQuality, [], ["Study Overview", "Sample / Participation", "Data Quality"]);
if (synchronizedDataset.Rows.Count != 1 || synchronizedDataset.Rows[0].ParticipantId != mainSession.ParticipantId ||
    synchronizedDataset.Rows[0].SessionId != mainSession.SessionId || synchronizedDataset.Rows[0].NumericValues.GetValueOrDefault("likes") != 1 ||
    !synchronizedDataset.Rows[0].NumericValues.ContainsKey("pre:pre-v1:pre-score") || !synchronizedDataset.Rows[0].NumericValues.ContainsKey("post:post-v1:post-score") ||
    !preCsv.Contains(mainSession.ParticipantId, StringComparison.Ordinal) || !postCsv.Contains(mainSession.SessionId, StringComparison.Ordinal) ||
    !behaviorCsv.Contains("content-main", StringComparison.Ordinal) || !behaviorCsv.Contains(",like,", StringComparison.Ordinal) || !sessionCsv.Contains(mainSession.DeploymentId, StringComparison.Ordinal) ||
    synchronizedReport.StudyId != "sync-study" || synchronizedReport.DatasetHash != synchronizedDataset.DatasetHash)
    throw new Exception("Synchronized participant, questionnaire, behavior, analysis, report, or export provenance diverged.");
await RemoteResearchRepository.ImportAsync(await RemoteResearchSynchronizationService.AssociateStudyIdsAsync(
    new RemoteSyncPullResult(new RemoteSyncCursor("two"), [mainSession with { RunType = ExperimentRunType.Pilot }], [], [])));
var resynchronized = await RemoteResearchRepository.GetSessionsAsync(studyId: "sync-study");
if (resynchronized.Count != 2 || resynchronized.Single(item => item.SessionId == mainSession.SessionId).RunType != ExperimentRunType.Pilot)
    throw new Exception("Remote session upsert was not idempotent or did not update RunType.");

var pilotDeployment = new ExperimentDeployment { StudyId = "pilot-study", DeploymentId = "pilot-deployment", Status = ExperimentDeploymentStatus.Published, ResearcherHandle = "researcher", ExperimentCode = "12345678", ConfigurationHash = "pilot-hash", DeploymentVersion = 1, PublishedAtUtc = synchronizedAt };
await PublishedExperimentStatusStore.SaveAsync(pilotDeployment, new Uri("https://runtime.example/experimentfeed/researcher/12345678"));
await PublishedExperimentStatusStore.SetPilotStateAsync("pilot-study", PilotLifecycleState.Running);
var pilotRunning = await PublishedExperimentStatusStore.GetAsync("pilot-study");
if (pilotRunning?.PilotState != PilotLifecycleState.Running || pilotRunning.IsMainRecruitmentStarted)
    throw new Exception("Pilot Running did not preserve the explicit Main recruitment boundary.");
await PublishedExperimentStatusStore.SetPilotStateAsync("pilot-study", PilotLifecycleState.Completed);
var pilotCompleted = await PublishedExperimentStatusStore.GetAsync("pilot-study");
if (pilotCompleted?.PilotState != PilotLifecycleState.Completed || pilotCompleted.PilotConfigurationHash != "pilot-hash")
    throw new Exception("Pilot completion did not preserve deployment-version provenance.");
await PublishedExperimentStatusStore.SetMainRecruitmentStartedAsync("pilot-study", true);
if ((await PublishedExperimentStatusStore.GetAsync("pilot-study"))?.IsMainRecruitmentStarted != true)
    throw new Exception("Main recruitment must require an explicit lifecycle transition after Pilot.");

var duplicateResearcher = ResearcherService.CreateProfile("Duplicate Test Researcher", null, false, true);
await ResearcherRepository.EnsureExistsAsync(duplicateResearcher);
var sourceStudy = await StudyService.CreateStudyAsync(duplicateResearcher.Id, "دراسة الثقة الرقمية", "Design source", 2);
sourceStudy.TargetSampleSize = 120;
sourceStudy.UsesQuestionnaires = true;
sourceStudy.ConsentText = "أوافق على المشاركة في هذه الدراسة.";
sourceStudy.MetadataJson = "{\"DesignNote\":\"retain\",\"DeploymentId\":\"must-not-copy\",\"ResearchNumber\":\"secret-route\"}";
await StudyService.UpdateStudyAsync(sourceStudy);
var sourceGroups = await GroupRepository.GetByStudyAsync(sourceStudy.Id);
var sourceConditions = await ExperimentalConditionRepository.GetByStudyAsync(sourceStudy.Id);
foreach (var group in sourceGroups)
    await StimulusPostRepository.CreateAsync(new StimulusPost { StudyId = sourceStudy.Id, GroupId = group.Id, Title = "محتوى بحثي", BodyText = $"مادة رقمية مضبوطة للمجموعة {group.Name}", ContentType = "Text", OrderIndex = 0, IsActive = true });
var participant = new Participant { StudyId = sourceStudy.Id, GroupId = sourceGroups[0].Id, ParticipantCode = "P-001", Status = "Completed", ConsentAccepted = true, HasStartedStudy = true, HasCompletedStudy = true };
await ParticipantRepository.CreateAsync(participant);
await ExperimentSessionRepository.CreateAsync(new ExperimentSession { StudyId = sourceStudy.Id, ParticipantId = participant.Id, GroupId = sourceGroups[0].Id, ConditionId = sourceConditions[0].Id, Status = SessionLifecycleStates.Completed, StartedAtUtc = created, CompletedAtUtc = created.AddMinutes(5), DurationMilliseconds = 300000 });
var questionnaire = new Questionnaire { StudyId = sourceStudy.Id, Title = "مقياس الثقة", Description = "أداة من إنشاء الباحث" };
var questionnaireVersion = new QuestionnaireVersion { QuestionnaireId = questionnaire.Id, VersionLabel = "1.0", Language = "ar" };
questionnaireVersion.Questions.Add(new Question { QuestionnaireVersionId = questionnaireVersion.Id, VariableName = "digital_trust", QuestionText = "ما مستوى ثقتك بالمحتوى الرقمي؟", QuestionType = QuestionnaireQuestionTypes.Likert, MeasurementLevel = MeasurementLevels.Ordinal, IsRequired = true });
await QuestionnaireRepository.CreateAsync(questionnaire, questionnaireVersion);
await QuestionnaireRepository.AssignAsync(new QuestionnaireAssignment { StudyId = sourceStudy.Id, QuestionnaireVersionId = questionnaireVersion.Id, Placement = QuestionnairePlacements.PreExperiment });

var duplicated = await StudyDuplicationService.DuplicateAsync(sourceStudy, false);
if (duplicated.Id == sourceStudy.Id || duplicated.Status != "Draft" || duplicated.Title != "Copy of دراسة الثقة الرقمية" || duplicated.TargetSampleSize != 120)
    throw new Exception("Study duplication did not create an independent editable design identity.");
if ((await GroupRepository.GetByStudyAsync(duplicated.Id)).Count != sourceGroups.Count ||
    (await ExperimentalConditionRepository.GetByStudyAsync(duplicated.Id)).Count != sourceConditions.Count ||
    (await StimulusPostRepository.GetByStudyAsync(duplicated.Id)).Count != sourceGroups.Count ||
    (await QuestionnaireRepository.GetByStudyAsync(duplicated.Id)).Count != 1 ||
    (await QuestionnaireRepository.GetAssignmentsAsync(duplicated.Id)).Count != 1)
    throw new Exception("Study duplication did not preserve groups, conditions, questionnaires, and PRE/POST structure.");
if (await ParticipantRepository.CountByStudyAsync(duplicated.Id) != 0 || await ExperimentSessionRepository.CountByStudyAsync(duplicated.Id) != 0 ||
    duplicated.MetadataJson?.Contains("Deployment", StringComparison.OrdinalIgnoreCase) == true ||
    duplicated.MetadataJson?.Contains("ResearchNumber", StringComparison.OrdinalIgnoreCase) == true ||
    duplicated.MetadataJson?.Contains("retain", StringComparison.Ordinal) != true)
    throw new Exception("Design-only duplication copied collected or remote publication state, or removed safe design metadata.");

var dictionary = await ResearchDataDictionaryService.ForStudyAsync(sourceStudy, true);
var questionEntry = dictionary.Single(item => item.VariableId == "digital_trust");
if (questionEntry.DisplayName != "ما مستوى ثقتك بالمحتوى الرقمي؟" || questionEntry.QuestionWording != questionEntry.DisplayName ||
    !ResearchDataDictionaryService.Csv(dictionary).Contains("ما مستوى ثقتك", StringComparison.Ordinal) ||
    !ResearchDataDictionaryService.Json(dictionary).Contains("RunTypeProvenance", StringComparison.Ordinal))
    throw new Exception("The Data Dictionary did not preserve researcher wording, UTF-8, version, and RunType provenance.");

var packagePath = await ResearchPackageExportService.ExportAsync(sourceStudy, true);
using (var package = ZipFile.OpenRead(packagePath))
{
    var requiredEntries = new[] { "README.md", "study/study_metadata.json", "study/groups.json", "study/conditions.json", "study/questionnaires.json", "data/participants.csv", "data/sessions.csv", "data/participant_analysis_dataset.csv", "dictionary/data_dictionary.csv", "dictionary/data_dictionary.json", "analysis/analysis_provenance.json", "reports/generated_report.md", "metadata/package_manifest.json" };
    if (requiredEntries.Any(name => package.GetEntry(name) is null)) throw new Exception("The Research Package omitted an available reproducibility artifact.");
    foreach (var archiveEntry in package.Entries)
    {
        using var reader = new StreamReader(archiveEntry.Open());
        var contentValue = await reader.ReadToEndAsync();
        if (ResearchPackageExportService.ContainsSecretMaterial(contentValue)) throw new Exception($"Research Package credential regression in {archiveEntry.FullName}.");
    }
}
if (!ResearchPackageExportService.ContainsSecretMaterial("{\"refresh_token\":\"forbidden\"}"))
    throw new Exception("Research Package secret regression guard did not recognize OAuth material.");

var preparedPublication = await RemotePublicationPreparationService.PrepareAsync(sourceStudy);
var preparedAgain = await RemotePublicationPreparationService.PrepareAsync(sourceStudy);
if (preparedPublication.Package.Study.Title != sourceStudy.Title ||
    preparedPublication.Package.ConfigurationHash != preparedAgain.Package.ConfigurationHash ||
    preparedPublication.Content.Count < sourceConditions.Count ||
    preparedPublication.Content.Any(item => string.IsNullOrWhiteSpace(item.ConditionId)) ||
    preparedPublication.Questionnaires.Single().Stage != QuestionnaireStage.Pre ||
    !preparedPublication.Package.ParticipantFlow.PreMeasureEnabled)
    throw new Exception("The publish source did not preserve title, deterministic hash, condition content, or questionnaire stage.");
var publishReadiness = await ResearcherPublishValidationService.EvaluateAsync(sourceStudy,
    preparedPublication.Entry, preparedPublication.Content, preparedPublication.Questionnaires,
    new CloudflareProviderConfiguration { AccountId = "account", D1DatabaseId = "database", WorkerEndpoint = "https://runtime.example.workers.dev", ConnectionMode = CloudflareConnectionMode.Manual, ProviderStatus = CloudflareProviderConnectionState.Ready },
    preparedPublication.Package.MediaManifest);
if (!publishReadiness.IsReady) throw new Exception("A complete persisted text study did not pass the source-side publication gate.");

Console.WriteLine("13/13 remote foundation, Pilot, publication, duplicate-study, Data Dictionary, and Research Package gates passed");
