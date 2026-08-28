using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public static class BehavioralMetricDefinitions
{
    public static readonly IReadOnlyDictionary<string, string> Definitions = new Dictionary<string, string>
    {
        ["session_duration_ms"] = "Completed session duration in milliseconds from the persisted lifecycle record.",
        ["content_exposures"] = "Distinct content items that entered the viewport.",
        ["meaningful_exposures"] = "Distinct content items meeting the configured visibility ratio and minimum-duration threshold.",
        ["meaningful_exposure_time_ms"] = "Sum of persisted meaningful-exposure durations across content items.",
        ["mean_dwell_time_ms"] = "Arithmetic mean of completed meaningful-exposure durations.",
        ["median_dwell_time_ms"] = "Median of completed meaningful-exposure durations.",
        ["post_opens"] = "Count of PostOpened events.",
        ["focused_view_duration_ms"] = "Sum of FocusedViewEnded durations.",
        ["likes"] = "Count of LikeClicked events.",
        ["comments"] = "Count of CommentSubmitted events.",
        ["link_opens"] = "Count of LinkOpened events.",
        ["maximum_scroll_depth_percent"] = "Maximum persisted ScrollDepthPercent in the session.",
        ["interaction_count"] = "LikeClicked + CommentSubmitted + LinkOpened + PostOpened events.",
        ["interaction_rate_per_meaningful_exposure"] = "Interaction count divided by meaningful exposures; missing when denominator is zero."
    };
}

public static class AnalysisDatasetService
{
    public static async Task<AnalysisDataset> BuildParticipantDatasetAsync(string studyId, bool isDemo)
    {
        var participantsTask = ParticipantRepository.GetByStudyAsync(studyId);
        var sessionsTask = ExperimentSessionRepository.GetByStudyAsync(studyId);
        var groupsTask = GroupRepository.GetByStudyAsync(studyId);
        var conditionsTask = ExperimentalConditionRepository.GetByStudyAsync(studyId);
        var responsesTask = QuestionnaireRepository.GetResponsesByStudyAsync(studyId, isDemo);
        await Task.WhenAll(participantsTask, sessionsTask, groupsTask, conditionsTask, responsesTask);

        var groups = groupsTask.Result.ToDictionary(item => item.Id);
        var conditions = conditionsTask.Result.ToDictionary(item => item.Id);
        var sessionsByParticipant = sessionsTask.Result.GroupBy(item => item.ParticipantId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.CompletedAtUtc ?? item.UpdatedAtUtc).ToArray());
        var responsesByParticipant = responsesTask.Result.GroupBy(item => item.ParticipantId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var exclusions = new List<AnalysisExclusion>();
        var rows = new List<AnalysisRow>();
        var questionnaireVariables = new Dictionary<string, AnalysisVariable>(StringComparer.Ordinal);
        var versionCache = new Dictionary<string, QuestionnaireVersion>(StringComparer.Ordinal);

        foreach (var participant in participantsTask.Result.OrderBy(item => item.ParticipantCode, StringComparer.Ordinal))
        {
            sessionsByParticipant.TryGetValue(participant.Id, out var participantSessions);
            participantSessions ??= [];
            var completed = participantSessions.Where(item => item.Status == SessionLifecycleStates.Completed).ToArray();
            var selectedSession = completed.FirstOrDefault() ?? participantSessions.FirstOrDefault();
            foreach (var duplicate in completed.Skip(1))
                exclusions.Add(new AnalysisExclusion(participant.Id, duplicate.Id, "DUPLICATE_COMPLETED_SESSION",
                    "An older completed session was excluded; the latest completed session represents the participant-level row."));

            if (participant.IsExcluded || participant.HasWithdrawn)
            {
                exclusions.Add(new AnalysisExclusion(participant.Id, selectedSession?.Id,
                    participant.HasWithdrawn ? "PARTICIPANT_WITHDREW" : "PARTICIPANT_EXCLUDED",
                    participant.WithdrawalReason ?? participant.ExclusionReason ?? "Participant record is excluded from analysis."));
                continue;
            }

            var numeric = new Dictionary<string, double?>(StringComparer.Ordinal);
            if (selectedSession is not null)
            {
                var events = await InteractionEventRepository.GetBySessionAsync(selectedSession.Id);
                AddBehavioralMetrics(numeric, selectedSession, events);
            }
            else
            {
                foreach (var key in BehavioralMetricDefinitions.Definitions.Keys) numeric[key] = null;
            }

            if (responsesByParticipant.TryGetValue(participant.Id, out var responseSets))
            {
                foreach (var responseSet in responseSets.Where(item => item.Status == "Completed"))
                {
                    if (!versionCache.TryGetValue(responseSet.QuestionnaireVersionId, out var version))
                    {
                        version = await QuestionnaireRepository.GetVersionAsync(responseSet.QuestionnaireVersionId)
                                  ?? throw new InvalidOperationException("Questionnaire version referenced by a response no longer exists.");
                        versionCache[version.Id] = version;
                    }
                    var answerMap = responseSet.Responses.ToDictionary(item => item.QuestionId, item => item.NumericValue);
                    foreach (var question in version.Questions)
                    {
                        var variableId = $"question:{question.Id}";
                        numeric[variableId] = answerMap.GetValueOrDefault(question.Id);
                        questionnaireVariables[variableId] = new AnalysisVariable
                        {
                            Id = variableId, Name = question.VariableName, Source = "QUESTIONNAIRE_ITEM",
                            Role = VariableRoles.Outcome, DataType = "Double", MeasurementLevel = question.MeasurementLevel,
                            Definition = question.QuestionText
                        };
                    }
                    foreach (var scale in version.Scales)
                    {
                        var score = QuestionnaireScoringService.Score(scale, answerMap);
                        var variableId = $"scale:{scale.Id}";
                        numeric[variableId] = score.Score;
                        questionnaireVariables[variableId] = new AnalysisVariable
                        {
                            Id = variableId, Name = scale.Name, Source = "QUESTIONNAIRE_SCALE",
                            Role = VariableRoles.Outcome, DataType = "Double", MeasurementLevel = MeasurementLevels.Continuous,
                            Definition = $"Deterministic {scale.ScoringMethod} score from raw questionnaire responses; missing-item rule {scale.MissingItemRule}."
                        };
                    }
                }
            }

            var groupId = selectedSession?.GroupId ?? participant.GroupId;
            var conditionId = selectedSession?.ConditionId;
            rows.Add(new AnalysisRow
            {
                ParticipantId = participant.Id, ParticipantCode = participant.ParticipantCode,
                GroupId = groupId, GroupName = groupId is not null && groups.TryGetValue(groupId, out var group) ? group.Name : null,
                ConditionId = conditionId, ConditionName = conditionId is not null && conditions.TryGetValue(conditionId, out var condition) ? condition.Name : null,
                SessionId = selectedSession?.Id, SessionCompleted = selectedSession?.Status == SessionLifecycleStates.Completed,
                IsDemo = isDemo, NumericValues = numeric,
                CategoricalValues = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["participant_code"] = participant.ParticipantCode,
                    ["group"] = groupId is not null && groups.TryGetValue(groupId, out group) ? group.Name : null,
                    ["condition"] = conditionId is not null && conditions.TryGetValue(conditionId, out condition) ? condition.Name : null,
                    ["session_status"] = selectedSession?.Status
                }
            });
        }

        var variables = BuildCoreVariables().Concat(questionnaireVariables.Values.OrderBy(item => item.Name)).ToArray();
        var hash = ComputeHash(studyId, rows, variables);
        return new AnalysisDataset
        {
            StudyId = studyId, DatasetHash = hash, Rows = rows, Variables = variables,
            Exclusions = exclusions, IsDemo = isDemo
        };
    }

    private static void AddBehavioralMetrics(
        IDictionary<string, double?> values,
        ExperimentSession session,
        IReadOnlyList<InteractionEvent> events)
    {
        var meaningfulStarts = events.Where(item => item.EventType == CanonicalInteractionEventTypes.ContentMeaningfullyExposed)
            .Select(item => item.SnapshotStimulusId ?? item.StimulusPostId).Where(item => item is not null).Distinct().Count();
        var entered = events.Where(item => item.EventType == CanonicalInteractionEventTypes.ContentEnteredViewport)
            .Select(item => item.SnapshotStimulusId ?? item.StimulusPostId).Where(item => item is not null).Distinct().Count();
        var dwell = events.Where(item => item.EventType == CanonicalInteractionEventTypes.ContentMeaningfulExposureEnded && item.DurationMilliseconds.HasValue)
            .Select(item => (double)item.DurationMilliseconds!.Value).ToArray();
        var interactions = events.Count(item => item.EventType is CanonicalInteractionEventTypes.LikeClicked
            or CanonicalInteractionEventTypes.CommentSubmitted or CanonicalInteractionEventTypes.LinkOpened
            or CanonicalInteractionEventTypes.PostOpened);
        values["session_duration_ms"] = session.DurationMilliseconds;
        values["content_exposures"] = entered;
        values["meaningful_exposures"] = meaningfulStarts;
        values["meaningful_exposure_time_ms"] = dwell.Sum();
        values["mean_dwell_time_ms"] = dwell.Length == 0 ? null : dwell.Average();
        values["median_dwell_time_ms"] = dwell.Length == 0 ? null : ScientificStatistics.Describe(dwell.Cast<double?>()).Median;
        values["post_opens"] = events.Count(item => item.EventType == CanonicalInteractionEventTypes.PostOpened);
        values["focused_view_duration_ms"] = events.Where(item => item.EventType == CanonicalInteractionEventTypes.FocusedViewEnded).Sum(item => item.DurationMilliseconds ?? 0);
        values["likes"] = events.Count(item => item.EventType == CanonicalInteractionEventTypes.LikeClicked);
        values["comments"] = events.Count(item => item.EventType == CanonicalInteractionEventTypes.CommentSubmitted);
        values["link_opens"] = events.Count(item => item.EventType == CanonicalInteractionEventTypes.LinkOpened);
        values["maximum_scroll_depth_percent"] = events.Where(item => item.ScrollDepthPercent.HasValue).Select(item => item.ScrollDepthPercent!.Value).DefaultIfEmpty(0).Max();
        values["interaction_count"] = interactions;
        values["interaction_rate_per_meaningful_exposure"] = meaningfulStarts == 0 ? null : (double)interactions / meaningfulStarts;
    }

    private static IEnumerable<AnalysisVariable> BuildCoreVariables()
    {
        yield return new AnalysisVariable { Id="group",Name="Group",Source="PARTICIPANT_ASSIGNMENT",Role=VariableRoles.Grouping,DataType="String",MeasurementLevel=MeasurementLevels.Nominal };
        yield return new AnalysisVariable { Id="condition",Name="Condition",Source="CONDITION_ASSIGNMENT",Role=VariableRoles.Grouping,DataType="String",MeasurementLevel=MeasurementLevels.Nominal };
        foreach (var definition in BehavioralMetricDefinitions.Definitions)
            yield return new AnalysisVariable
            {
                Id=definition.Key,Name=Humanize(definition.Key),Source="BEHAVIORAL_TELEMETRY",Role=VariableRoles.Outcome,
                DataType="Double",MeasurementLevel=definition.Key.Contains("count",StringComparison.Ordinal)||definition.Key.EndsWith("s",StringComparison.Ordinal)?MeasurementLevels.Count:MeasurementLevels.Continuous,
                Unit=definition.Key.EndsWith("_ms",StringComparison.Ordinal)?"milliseconds":definition.Key.EndsWith("_percent",StringComparison.Ordinal)?"percent":null,
                Definition=definition.Value
            };
    }

    private static string ComputeHash(string studyId,IReadOnlyList<AnalysisRow> rows,IReadOnlyList<AnalysisVariable> variables)
    {
        var canonical=JsonSerializer.Serialize(new{StudyId=studyId,Engine=ScientificEngineMetadata.Version,Variables=variables.Select(item=>new{item.Id,item.Source,item.MeasurementLevel}),Rows=rows.OrderBy(item=>item.ParticipantId).Select(item=>new{item.ParticipantId,item.GroupId,item.ConditionId,item.SessionId,Numeric=item.NumericValues.OrderBy(pair=>pair.Key),Categorical=item.CategoricalValues.OrderBy(pair=>pair.Key)})});
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string Humanize(string value)=>string.Join(' ',value.Split('_',StringSplitOptions.RemoveEmptyEntries).Select(word=>char.ToUpperInvariant(word[0])+word[1..]));
}

public static class DataQualityService
{
    public static DataQualityResult Evaluate(AnalysisDataset dataset)
    {
        var missing=new Dictionary<string,int>(StringComparer.Ordinal);var constants=new List<string>();var insufficient=new List<string>();
        foreach(var variable in dataset.Variables.Where(item=>item.DataType=="Double"))
        {
            var values=dataset.Rows.Select(row=>row.NumericValues.GetValueOrDefault(variable.Id)).ToArray();
            missing[variable.Id]=values.Count(value=>!value.HasValue||!double.IsFinite(value.Value));
            var distinct=values.Where(value=>value.HasValue&&double.IsFinite(value.Value)).Select(value=>value!.Value).Distinct().Count();
            if(distinct==1)constants.Add(variable.Id);else if(distinct<2)insufficient.Add(variable.Id);
        }
        var duplicateWarnings=dataset.Exclusions.Where(item=>item.ReasonCode=="DUPLICATE_COMPLETED_SESSION").Select(item=>item.ReasonDetail).Distinct().ToArray();
        var incomplete=dataset.Rows.Where(row=>row.NumericValues.Keys.Any(key=>key.StartsWith("question:",StringComparison.Ordinal))&&row.NumericValues.Where(pair=>pair.Key.StartsWith("question:",StringComparison.Ordinal)).Any(pair=>!pair.Value.HasValue)).Select(row=>$"Participant {row.ParticipantCode} has incomplete questionnaire data.").ToArray();
        var sessionWarnings=dataset.Rows.Where(row=>!row.SessionCompleted).Select(row=>$"Participant {row.ParticipantCode} has no completed session.").ToArray();
        var includedIds=dataset.Rows.Select(item=>item.ParticipantId).ToHashSet(StringComparer.Ordinal);
        var excludedParticipantIds=dataset.Exclusions.Select(item=>item.ParticipantId)
            .Where(item=>!string.IsNullOrWhiteSpace(item)&&!includedIds.Contains(item!))
            .Select(item=>item!).Distinct(StringComparer.Ordinal).ToArray();
        return new DataQualityResult{TotalN=dataset.Rows.Count+excludedParticipantIds.Length,IncludedN=dataset.Rows.Count,ExcludedN=excludedParticipantIds.Length,MissingByVariable=missing,ConstantVariables=constants,InsufficientVariationVariables=insufficient,DuplicateParticipantWarnings=duplicateWarnings,IncompleteQuestionnaireWarnings=incomplete,SessionWarnings=sessionWarnings,Exclusions=dataset.Exclusions};
    }
}
