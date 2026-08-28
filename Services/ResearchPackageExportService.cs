using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public sealed record ResearchPackageArtifact(
    string RelativePath, string LogicalType, string Format, long Size, int? RowCount, string Sha256);

public sealed record ResearchPackageManifest(
    string StudyId, int? StudyVersion, string? ConfigurationHash, string SocyviaVersion,
    DateTime GeneratedAtUtc, string DatasetHash, int EligibleN, int ExcludedN,
    int IncompleteN, int PilotN, IReadOnlyList<string> Groups, IReadOnlyList<string> Conditions,
    IReadOnlyList<string> AnalysisIds, IReadOnlyList<ResearchPackageArtifact> Artifacts);

/// <summary>Single local reproducibility ZIP. Infrastructure credentials and AI conversation state are out of scope by construction.</summary>
public static class ResearchPackageExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<string> ExportAsync(Study study, bool arabic)
    {
        var generatedAt = DateTime.UtcNow;
        var folder = StorageService.GetResearcherExportsFolder(study.ResearcherId);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"SOCYVIA_Research_Package_{study.Id}_{generatedAt:yyyyMMddHHmmss}.zip");

        var groupsTask = GroupRepository.GetByStudyAsync(study.Id);
        var conditionsTask = ExperimentalConditionRepository.GetByStudyAsync(study.Id);
        var stimuliTask = StimulusPostRepository.GetByStudyAsync(study.Id);
        var questionnairesTask = QuestionnaireRepository.GetByStudyAsync(study.Id);
        var assignmentsTask = QuestionnaireRepository.GetAssignmentsAsync(study.Id);
        var participantsTask = ParticipantRepository.GetByStudyAsync(study.Id);
        var localSessionsTask = ExperimentSessionRepository.GetByStudyAsync(study.Id);
        var localResponsesTask = QuestionnaireRepository.GetResponsesByStudyAsync(study.Id);
        var sessionsTask = RemoteResearchRepository.GetSessionsAsync(studyId: study.Id);
        var dictionaryTask = ResearchDataDictionaryService.ForStudyAsync(study, arabic);
        var specificationsTask = AnalysisRepository.GetSpecificationsAsync(study.Id, false);
        var publicationTask = PublishedExperimentStatusStore.GetAsync(study.Id);
        await Task.WhenAll(groupsTask, conditionsTask, stimuliTask, questionnairesTask, assignmentsTask,
            participantsTask, localSessionsTask, localResponsesTask, sessionsTask, dictionaryTask, specificationsTask, publicationTask);

        var groups = groupsTask.Result.OrderBy(item => item.SortOrder).ToArray();
        var conditions = conditionsTask.Result.OrderBy(item => item.SortOrder).ToArray();
        var stimuli = stimuliTask.Result.OrderBy(item => item.OrderIndex).ToArray();
        var questionnaires = questionnairesTask.Result.OrderBy(item => item.SortOrder).ToArray();
        var assignments = assignmentsTask.Result;
        var sessions = sessionsTask.Result;
        var localSessions = localSessionsTask.Result;
        var mainSessions = sessions.Where(item => item.RunType == ExperimentRunType.Main).ToArray();
        var pilotN = sessions.Where(item => item.RunType == ExperimentRunType.Pilot)
            .Select(item => item.ParticipantId).Distinct(StringComparer.Ordinal).Count();
        var incompleteN = mainSessions.Count(item => item.CompletionState != RemoteParticipantCompletionState.CompletedEligible) +
                          localSessions.Count(item => item.Status != SessionLifecycleStates.Completed);
        var remoteDataset = await RemoteAnalysisDatasetService.BuildCompletedEligibleAsync(study.Id, ExperimentRunType.Main);
        var localDataset = participantsTask.Result.Count == 0
            ? new AnalysisDataset { StudyId = study.Id, DatasetHash = remoteDataset.DatasetHash }
            : await AnalysisDatasetService.BuildParticipantDatasetAsync(study.Id, DemoAccessPolicy.IsDemoStudy(study));
        var dataset = remoteDataset.Rows.Count > 0 ? remoteDataset : localDataset;
        var quality = DataQualityService.Evaluate(dataset);
        var executions = new List<AnalysisExecution>();
        foreach (var specification in specificationsTask.Result)
            if (await AnalysisRepository.GetLatestExecutionAsync(specification.Id) is { } execution) executions.Add(execution);

        var sessionCsv = SessionCsv(localSessions, sessions);
        var incompleteCsv = IncompleteSessionCsv(localSessions, sessions);
        var remoteEligibleCsv = await RemoteResearchExportService.ExportCompletedAnalyticalDatasetCsvAsync(studyId: study.Id);
        var eligibleCsv = remoteDataset.Rows.Count > 0 ? remoteEligibleCsv : AnalysisDatasetCsv(localDataset);
        var preCsv = await QuestionnaireCsv(QuestionnaireStage.Pre, localResponsesTask.Result, assignments, study.Id);
        var postCsv = await QuestionnaireCsv(QuestionnaireStage.Post, localResponsesTask.Result, assignments, study.Id);
        var eventsCsv = await BehavioralEventsCsv(localSessions, sessions, study.Id);
        var participantCsv = ParticipantCsv(participantsTask.Result, sessions);
        var publication = publicationTask.Result;

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var artifacts = new List<ResearchPackageArtifact>();
        Add(zip, artifacts, "study/study_metadata.json", "study metadata", Json(new
        {
            study.Id, study.Title, study.Description, study.Status, study.StudyType, study.DesignType,
            study.AssignmentMethod, study.RandomizeStimuli, study.RandomizationSeed, study.UsesStimuli,
            study.UsesQuestionnaires, study.TargetSampleSize, study.ExpectedSessionDurationMinutes,
            study.AllowSessionResume, study.RequireParticipantConsent, study.ConsentText,
            study.ResearchQuestion, study.Hypothesis, study.PopulationDescription,
            study.InclusionCriteria, study.ExclusionCriteria, study.CreatedAtUtc, study.UpdatedAtUtc
        }), 1);
        Add(zip, artifacts, "study/groups.json", "study groups", Json(groups), groups.Length);
        Add(zip, artifacts, "study/conditions.json", "experimental conditions", Json(conditions), conditions.Length);
        Add(zip, artifacts, "study/content_metadata.json", "stimulus metadata", Json(stimuli.Select(item => new
        {
            item.Id, item.GroupId, item.ContentItemId, item.Title, item.BodyText, item.ContentType,
            item.Platform, item.SourceName, item.AuthorName, item.OriginalUrl, item.PublishedAtUtc,
            MediaFile = SafeFileName(item.MediaPath), ThumbnailFile = SafeFileName(item.ThumbnailPath),
            item.PublishedMediaUrl,
            item.Category, item.Topic, item.ConditionLabel, item.ExperimentalTag, item.OrderIndex,
            item.IsActive, item.MinimumExposureMilliseconds, item.MaximumExposureMilliseconds,
            item.AllowRandomization
        })), stimuli.Length);
        Add(zip, artifacts, "study/questionnaires.json", "questionnaire definitions", Json(new { Questionnaires = questionnaires, Assignments = assignments }), questionnaires.Length);
        var scales = questionnaires.SelectMany(item => item.Versions).SelectMany(item => item.Scales).ToArray();
        if (scales.Length > 0) Add(zip, artifacts, "study/scales.json", "questionnaire scales", Json(scales), scales.Length);

        AddIfData(zip, artifacts, "data/participants.csv", "participants", participantCsv);
        AddIfData(zip, artifacts, "data/sessions.csv", "sessions", sessionCsv);
        AddIfData(zip, artifacts, "data/incomplete_sessions.csv", "attrition", incompleteCsv);
        AddIfData(zip, artifacts, "data/participant_analysis_dataset.csv", "analytical dataset", eligibleCsv);
        if (remoteDataset.Rows.Count > 0 && localDataset.Rows.Count > 0)
            AddIfData(zip, artifacts, "data/local_participant_analysis_dataset.csv", "local analytical dataset", AnalysisDatasetCsv(localDataset));
        AddIfData(zip, artifacts, "data/questionnaire_pre.csv", "pre-questionnaire responses", preCsv);
        AddIfData(zip, artifacts, "data/questionnaire_post.csv", "post-questionnaire responses", postCsv);
        AddIfData(zip, artifacts, "data/behavioral_events.csv", "behavioral events", eventsCsv);

        Add(zip, artifacts, "dictionary/data_dictionary.csv", "data dictionary", ResearchDataDictionaryService.Csv(dictionaryTask.Result), dictionaryTask.Result.Count);
        Add(zip, artifacts, "dictionary/data_dictionary.json", "data dictionary", ResearchDataDictionaryService.Json(dictionaryTask.Result), dictionaryTask.Result.Count);

        if (executions.Count > 0)
            Add(zip, artifacts, "analysis/analysis_results.json", "deterministic analysis results", Json(executions), executions.Count);
        Add(zip, artifacts, "analysis/analysis_provenance.json", "analysis provenance", Json(new
        {
            StudyId = study.Id, DatasetHash = dataset.DatasetHash, DatasetSource = remoteDataset.Rows.Count > 0 ? "Remote Main" : "Local Main",
            EngineVersion = ScientificEngineMetadata.Version,
            RunType = ExperimentRunType.Main.ToString(), EligibleN = dataset.Rows.Count,
            ExcludedN = quality.ExcludedN, AnalysisIds = executions.Select(item => item.Id).ToArray(), GeneratedAtUtc = generatedAt
        }), executions.Count);

        var figureVariable = dataset.Variables.FirstOrDefault(variable => variable.DataType == "Double" &&
            dataset.Rows.Any(row => row.NumericValues.GetValueOrDefault(variable.Id).HasValue) &&
            dataset.Rows.Any(row => !string.IsNullOrWhiteSpace(row.GroupName ?? row.ConditionName)));
        if (figureVariable is not null)
        {
            var grouping = dataset.Rows.Any(row => !string.IsNullOrWhiteSpace(row.GroupName)) ? "group" : "condition";
            var figure = ResearchFigureService.CreateGroupedMeanFigure(dataset, figureVariable.Id, grouping);
            Add(zip, artifacts, $"figures/{figure.Id}.svg", "deterministic figure", figure.Svg, null);
            Add(zip, artifacts, $"analysis/figure_data_{figure.Id}.csv", "figure source data", figure.CsvData, CsvRows(figure.CsvData));
        }

        var report = ResearchReportService.Build(study, dataset, quality, executions,
            ["Study Overview", "Sample / Participation", "Data Quality", "Statistical Analyses"]);
        Add(zip, artifacts, "reports/generated_report.md", "deterministic report", report.Markdown, null);
        Add(zip, artifacts, "README.md", "package guide", Readme(study, generatedAt, dataset.DatasetHash), null, scanSecrets: false);

        var version = typeof(ResearchPackageExportService).Assembly.GetName().Version?.ToString() ?? "1.0";
        var manifest = new ResearchPackageManifest(
            study.Id, publication?.DeploymentVersion, publication?.ConfigurationHash, version,
            generatedAt, dataset.DatasetHash, dataset.Rows.Count, quality.ExcludedN, incompleteN, pilotN,
            groups.Select(item => item.Name).ToArray(), conditions.Select(item => item.Name).ToArray(),
            executions.Select(item => item.Id).ToArray(), artifacts.ToArray());
        Add(zip, artifacts, "metadata/package_manifest.json", "package manifest", Json(manifest), artifacts.Count, scanSecrets: true);
        return path;
    }

    public static bool ContainsSecretMaterial(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var unsafeTerms = new[] { "access_token", "refresh_token", "client_secret", "code_verifier", "authorization_code", "pkce verifier", "cfut_", "cfat_", "cfk_" };
        return unsafeTerms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string ParticipantCsv(IReadOnlyList<Participant> local, IReadOnlyList<RemoteParticipantSessionContract> remote)
    {
        var builder = new StringBuilder("participant_code,group_id,status,is_eligible,consent_accepted,started,completed,excluded,withdrawn\n");
        foreach (var participant in local.OrderBy(item => item.ParticipantCode, StringComparer.Ordinal))
            builder.AppendLine(string.Join(',', new object?[] { participant.ParticipantCode, participant.GroupId, participant.Status, participant.IsEligible, participant.ConsentAccepted, participant.HasStartedStudy, participant.HasCompletedStudy, participant.IsExcluded, participant.HasWithdrawn }.Select(Csv)));
        if (local.Count == 0)
            foreach (var participant in remote.GroupBy(item => item.ParticipantId, StringComparer.Ordinal).OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var session = participant.OrderByDescending(item => item.CompletedAtUtc).First();
                builder.AppendLine(string.Join(',', Csv(session.ParticipantId), Csv(session.GroupId), Csv(session.CompletionState), "", "", "true", Csv(session.CompletionState == RemoteParticipantCompletionState.CompletedEligible), "", ""));
            }
        return builder.ToString();
    }

    private static string SessionCsv(IReadOnlyList<ExperimentSession> local, IReadOnlyList<RemoteParticipantSessionContract> remote)
    {
        var builder = new StringBuilder("source,participant_id,session_id,deployment_id,condition_id,group_id,run_type,started_at_utc,completed_at_utc,status,duration_ms,interrupted\n");
        foreach (var item in local.OrderBy(item => item.CreatedAtUtc))
            builder.AppendLine(string.Join(',', new object?[] { "Local", item.ParticipantId, item.Id, null, item.ConditionId, item.GroupId, "Main", item.StartedAtUtc, item.CompletedAtUtc, item.Status, item.DurationMilliseconds, item.WasInterrupted }.Select(Csv)));
        foreach (var item in remote.OrderBy(item => item.StartedAtUtc))
            builder.AppendLine(string.Join(',', new object?[] { "Remote", item.ParticipantId, item.SessionId, item.DeploymentId, item.ConditionId, item.GroupId, item.RunType, item.StartedAtUtc, item.CompletedAtUtc, item.CompletionState, null, item.LifecycleState == RemoteParticipantLifecycleState.Incomplete }.Select(Csv)));
        return builder.ToString();
    }

    private static string IncompleteSessionCsv(IReadOnlyList<ExperimentSession> local, IReadOnlyList<RemoteParticipantSessionContract> remote)
    {
        var incompleteLocal = local.Where(item => item.Status != SessionLifecycleStates.Completed).ToArray();
        var incompleteRemote = remote.Where(item => item.CompletionState != RemoteParticipantCompletionState.CompletedEligible).ToArray();
        return SessionCsv(incompleteLocal, incompleteRemote);
    }

    private static async Task<string> BehavioralEventsCsv(
        IReadOnlyList<ExperimentSession> local,
        IReadOnlyList<RemoteParticipantSessionContract> remote,
        string studyId)
    {
        var builder = new StringBuilder("source,participant_id,session_id,deployment_id,condition_id,group_id,run_type,content_id,event_type,timestamp_utc,relative_time_ms,duration_ms,sequence,payload_or_value\n");
        foreach (var session in local.OrderBy(item => item.CreatedAtUtc))
        foreach (var item in await InteractionEventRepository.GetBySessionAsync(session.Id))
            builder.AppendLine(string.Join(',', new object?[] { "Local", item.ParticipantId, item.SessionId, null, session.ConditionId, item.GroupId, "Main", item.SnapshotStimulusId ?? item.StimulusPostId, item.EventType, item.TimestampUtc, item.SessionElapsedMilliseconds, item.DurationMilliseconds, item.SequenceNumber, item.ValueText }.Select(Csv)));
        foreach (var item in await RemoteResearchRepository.GetEventsAsync(studyId: studyId))
            builder.AppendLine(string.Join(',', new object?[] { "Remote", item.ParticipantId, item.SessionId, item.DeploymentId, item.ConditionId, null, item.RunType, item.ContentId, item.EventType, item.ClientTimestampUtc, item.ClientRelativeMilliseconds, null, null, item.PayloadJson }.Select(Csv)));
        return builder.ToString();
    }

    private static async Task<string> QuestionnaireCsv(
        QuestionnaireStage stage,
        IReadOnlyList<QuestionnaireResponse> local,
        IReadOnlyList<QuestionnaireAssignment> assignments,
        string studyId)
    {
        var placement = stage == QuestionnaireStage.Pre ? QuestionnairePlacements.PreExperiment : QuestionnairePlacements.PostExperiment;
        var assignmentIds = assignments.Where(item => item.Placement == placement).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var builder = new StringBuilder("source,participant_id,session_id,deployment_id,condition_id,run_type,questionnaire_id,questionnaire_version_id,stage,question_id,response,submitted_at_utc\n");
        foreach (var response in local.Where(item => assignmentIds.Contains(item.AssignmentId)).OrderBy(item => item.CompletedAtUtc))
        foreach (var answer in response.Responses)
            builder.AppendLine(string.Join(',', new object?[] { "Local", response.ParticipantId, response.SessionId, null, null, "Main", response.QuestionnaireId, response.QuestionnaireVersionId, stage, answer.QuestionId, answer.RawValue ?? Convert.ToString(answer.NumericValue, CultureInfo.InvariantCulture), answer.RespondedAtUtc }.Select(Csv)));
        foreach (var response in await RemoteResearchRepository.GetQuestionnaireResponsesAsync(stage, studyId: studyId))
        {
            using var document = JsonDocument.Parse(response.ResponseJson);
            foreach (var answer in document.RootElement.EnumerateObject())
                builder.AppendLine(string.Join(',', new object?[] { "Remote", response.ParticipantId, response.SessionId, response.DeploymentId, response.ConditionId, response.RunType, response.QuestionnaireId, response.QuestionnaireVersionId, response.Stage, answer.Name, answer.Value.ToString(), response.SubmittedAtUtc }.Select(Csv)));
        }
        return builder.ToString();
    }

    private static string AnalysisDatasetCsv(AnalysisDataset dataset)
    {
        var variables = dataset.Variables.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var builder = new StringBuilder("participant_id,session_id,group,condition," + string.Join(',', variables.Select(item => Csv(item.Id))) + "\n");
        foreach (var row in dataset.Rows)
            builder.AppendLine(string.Join(',', new[] { Csv(row.ParticipantId), Csv(row.SessionId), Csv(row.GroupName), Csv(row.ConditionName) }
                .Concat(variables.Select(variable => Csv(row.NumericValues.GetValueOrDefault(variable.Id))))) );
        return builder.ToString();
    }

    private static string Readme(Study study, DateTime generatedAt, string datasetHash) =>
        $"# SOCYVIA Research Package\n\nStudy: {study.Title}\nStudy ID: {study.Id}\nGenerated UTC: {generatedAt:O}\nDataset hash: `{datasetHash}`\n\nThe default analytical dataset contains completed eligible Main runs only. Pilot, incomplete, lifecycle, questionnaire-version, and run-type provenance remain separate and traceable. Deterministic outputs are the numerical authority. AI conversation history and all credential material are excluded.\n";

    private static string Json(object value) => JsonSerializer.Serialize(value, JsonOptions);
    private static string? SafeFileName(string? path) => string.IsNullOrWhiteSpace(path) ? null : Path.GetFileName(path);
    private static int CsvRows(string value) => Math.Max(0, value.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1);
    private static string Csv(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }

    private static void AddIfData(ZipArchive zip, List<ResearchPackageArtifact> manifest, string name, string type, string data)
    {
        var rows = CsvRows(data);
        if (rows > 0) Add(zip, manifest, name, type, data, rows);
    }

    private static void Add(ZipArchive zip, List<ResearchPackageArtifact> manifest, string name, string type, string data, int? rows, bool scanSecrets = true)
    {
        if (scanSecrets && ContainsSecretMaterial(data))
            throw new InvalidOperationException($"Research Package security check rejected {name} because it resembles credential material.");
        var bytes = new UTF8Encoding(false).GetBytes(data);
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using (var stream = entry.Open()) stream.Write(bytes);
        manifest.Add(new ResearchPackageArtifact(
            name, type, Path.GetExtension(name).TrimStart('.').ToUpperInvariant(), bytes.LongLength, rows,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
    }
}
