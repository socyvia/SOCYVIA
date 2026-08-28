using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SOCYVIA.Data;
using SOCYVIA.Models;

namespace SOCYVIA.Repositories;

public static class QuestionnaireRepository
{
    public static async Task<Questionnaire> CreateAsync(Questionnaire questionnaire, QuestionnaireVersion version)
    {
        Validate(questionnaire, version);
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        questionnaire.CurrentVersionId = version.Id;
        try
        {
            await using (var command = Command(connection, transaction, """
                INSERT INTO Questionnaires
                (Id, StudyId, Title, Description, SortOrder, CreatedAtUtc, UpdatedAtUtc,
                 IsActive, CurrentVersionId, InstrumentType, MetadataJson)
                VALUES ($id, $study, $title, $description, $sort, $created, $updated,
                        $active, $version, $type, $metadata);
                """))
            {
                Add(command, "$id", questionnaire.Id); Add(command, "$study", questionnaire.StudyId);
                Add(command, "$title", questionnaire.Title); Add(command, "$description", questionnaire.Description);
                Add(command, "$sort", questionnaire.SortOrder); Add(command, "$created", questionnaire.CreatedAtUtc);
                Add(command, "$updated", questionnaire.UpdatedAtUtc); Add(command, "$active", questionnaire.IsActive);
                Add(command, "$version", version.Id); Add(command, "$type", questionnaire.InstrumentType);
                Add(command, "$metadata", questionnaire.MetadataJson); await command.ExecuteNonQueryAsync();
            }
            await InsertVersionAsync(connection, transaction, version);
            await transaction.CommitAsync();
            questionnaire.Versions = [version];
            return questionnaire;
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public static async Task<List<Questionnaire>> GetByStudyAsync(string studyId)
    {
        var result = new List<Questionnaire>();
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, StudyId, Title, Description, SortOrder, CreatedAtUtc,
                   COALESCE(UpdatedAtUtc, CreatedAtUtc), COALESCE(IsActive, 1),
                   CurrentVersionId, COALESCE(InstrumentType, 'CUSTOM'), MetadataJson
            FROM Questionnaires WHERE StudyId = $study ORDER BY SortOrder, CreatedAtUtc;
            """;
        command.Parameters.AddWithValue("$study", studyId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(ReadQuestionnaire(reader));
        foreach (var questionnaire in result)
            questionnaire.Versions = await GetVersionsAsync(questionnaire.Id);
        return result;
    }

    public static async Task<Questionnaire?> GetByIdAsync(string id)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, StudyId, Title, Description, SortOrder, CreatedAtUtc,
                   COALESCE(UpdatedAtUtc, CreatedAtUtc), COALESCE(IsActive, 1),
                   CurrentVersionId, COALESCE(InstrumentType, 'CUSTOM'), MetadataJson
            FROM Questionnaires WHERE Id = $id LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var questionnaire = ReadQuestionnaire(reader);
        await reader.DisposeAsync();
        questionnaire.Versions = await GetVersionsAsync(id);
        return questionnaire;
    }

    public static async Task<QuestionnaireVersion?> GetVersionAsync(string id)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        return await LoadVersionAsync(connection, id);
    }

    public static async Task UpdateQuestionnaireAsync(Questionnaire questionnaire)
    {
        if (string.IsNullOrWhiteSpace(questionnaire.Title)) throw new ArgumentException("Questionnaire title is required.");
        questionnaire.UpdatedAtUtc = DateTime.UtcNow;
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Questionnaires SET Title=$title, Description=$description, SortOrder=$sort,
                UpdatedAtUtc=$updated, IsActive=$active, InstrumentType=$type, MetadataJson=$metadata
            WHERE Id=$id;
            """;
        Add(command, "$title", questionnaire.Title.Trim()); Add(command, "$description", questionnaire.Description);
        Add(command, "$sort", questionnaire.SortOrder); Add(command, "$updated", questionnaire.UpdatedAtUtc);
        Add(command, "$active", questionnaire.IsActive); Add(command, "$type", questionnaire.InstrumentType);
        Add(command, "$metadata", questionnaire.MetadataJson); Add(command, "$id", questionnaire.Id);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task UpdateVersionMetadataAsync(QuestionnaireVersion version)
    {
        await EnsureEditableAsync(version.Id);
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE QuestionnaireVersions SET VersionLabel=$label, Language=$language,
                InstrumentType=$type, Construct=$construct, Citation=$citation,
                LicenseStatus=$license, LicenseReference=$licenseReference,
                RedistributionStatus=$redistribution, ValidationNotes=$validation,
                TranslationNotes=$translation, ScoringAvailability=$scoring
            WHERE Id=$id;
            """;
        Add(command, "$label", version.VersionLabel); Add(command, "$language", version.Language);
        Add(command, "$type", version.InstrumentType); Add(command, "$construct", version.Construct);
        Add(command, "$citation", version.Citation); Add(command, "$license", version.LicenseStatus);
        Add(command, "$licenseReference", version.LicenseReference); Add(command, "$redistribution", version.RedistributionStatus);
        Add(command, "$validation", version.ValidationNotes); Add(command, "$translation", version.TranslationNotes);
        Add(command, "$scoring", version.ScoringAvailability); Add(command, "$id", version.Id);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<QuestionnaireSection> AddSectionAsync(QuestionnaireSection section)
    {
        await EnsureEditableAsync(section.QuestionnaireVersionId);
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO QuestionnaireSections (Id, QuestionnaireVersionId, Title, Description, SortOrder)
            VALUES ($id, $version, $title, $description, $sort);
            """;
        Add(command, "$id", section.Id); Add(command, "$version", section.QuestionnaireVersionId);
        Add(command, "$title", section.Title); Add(command, "$description", section.Description);
        Add(command, "$sort", section.SortOrder); await command.ExecuteNonQueryAsync();
        return section;
    }

    public static async Task<Question> AddQuestionAsync(Question question)
    {
        await EnsureEditableAsync(question.QuestionnaireVersionId);
        if (!QuestionnaireQuestionTypes.Supported.Contains(question.QuestionType))
            throw new ArgumentException("Unsupported question type.");
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            await InsertQuestionAsync(connection, transaction, question);
            await transaction.CommitAsync(); return question;
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public static async Task MoveQuestionAsync(string questionId, int sortOrder)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var lookup = connection.CreateCommand();
        lookup.CommandText = "SELECT QuestionnaireVersionId FROM QuestionnaireItems WHERE Id=$id;";
        lookup.Parameters.AddWithValue("$id", questionId);
        var versionId = Convert.ToString(await lookup.ExecuteScalarAsync()) ?? throw new InvalidOperationException("Question not found.");
        await EnsureEditableAsync(versionId);
        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE QuestionnaireItems SET SortOrder=$sort WHERE Id=$id;";
        update.Parameters.AddWithValue("$sort", sortOrder); update.Parameters.AddWithValue("$id", questionId);
        await update.ExecuteNonQueryAsync();
    }

    /// <summary>Draft-only editing surface used by the researcher questionnaire designer.</summary>
    public static async Task UpdateQuestionAsync(Question question)
    {
        await EnsureEditableAsync(question.QuestionnaireVersionId);
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE QuestionnaireItems SET VariableName=$variable, QuestionText=$text, QuestionType=$type, MeasurementLevel=$level, IsRequired=$required, ConfigurationJson=$configuration WHERE Id=$id;";
        Add(command, "$variable", question.VariableName); Add(command, "$text", question.QuestionText); Add(command, "$type", question.QuestionType); Add(command, "$level", question.MeasurementLevel); Add(command, "$required", question.IsRequired); Add(command, "$configuration", question.ConfigurationJson); Add(command, "$id", question.Id);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task DeleteQuestionAsync(string questionId)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var lookup = connection.CreateCommand();
        lookup.CommandText = "SELECT QuestionnaireVersionId FROM QuestionnaireItems WHERE Id=$id;";
        Add(lookup, "$id", questionId);
        var versionId = Convert.ToString(await lookup.ExecuteScalarAsync()) ?? throw new InvalidOperationException("Question not found.");
        await EnsureEditableAsync(versionId);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM QuestionnaireItems WHERE Id=$id;";
        Add(command, "$id", questionId);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<Question> DuplicateQuestionAsync(Question source)
    {
        var copy = new Question
        {
            QuestionnaireVersionId = source.QuestionnaireVersionId,
            SectionId = source.SectionId,
            VariableName = source.VariableName + "_copy",
            QuestionText = source.QuestionText,
            QuestionType = source.QuestionType,
            MeasurementLevel = source.MeasurementLevel,
            IsRequired = source.IsRequired,
            SortOrder = source.SortOrder + 1,
            ConfigurationJson = source.ConfigurationJson,
            Options = source.Options.Select(option => new QuestionOption { ValueCode = option.ValueCode, NumericCode = option.NumericCode, DisplayLabel = option.DisplayLabel, SortOrder = option.SortOrder }).ToList()
        };
        return await AddQuestionAsync(copy);
    }

    public static async Task<QuestionnaireVersion> CreateNewVersionAsync(string questionnaireId)
    {
        var questionnaire = await GetByIdAsync(questionnaireId) ?? throw new InvalidOperationException("Questionnaire not found.");
        var source = questionnaire.Versions.OrderByDescending(item => item.VersionNumber).FirstOrDefault()
                     ?? throw new InvalidOperationException("Questionnaire has no version.");
        var copy = CloneVersion(source, source.VersionNumber + 1);
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            await InsertVersionAsync(connection, transaction, copy);
            await using var update = Command(connection, transaction,
                "UPDATE Questionnaires SET CurrentVersionId=$version, UpdatedAtUtc=$updated WHERE Id=$id;");
            Add(update, "$version", copy.Id); Add(update, "$updated", DateTime.UtcNow); Add(update, "$id", questionnaireId);
            await update.ExecuteNonQueryAsync(); await transaction.CommitAsync(); return copy;
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public static async Task<Questionnaire> DuplicateAsync(string questionnaireId, string title)
        => await DuplicateToStudyAsync(questionnaireId, null, title);

    public static async Task<Questionnaire> DuplicateToStudyAsync(string questionnaireId, string? targetStudyId, string? title = null)
    {
        var source = await GetByIdAsync(questionnaireId) ?? throw new InvalidOperationException("Questionnaire not found.");
        var sourceVersion = source.Versions.OrderByDescending(item => item.VersionNumber).First();
        var questionnaire = new Questionnaire
        {
            StudyId = targetStudyId ?? source.StudyId, Title = title ?? source.Title, Description = source.Description,
            SortOrder = targetStudyId is null ? source.SortOrder + 1 : source.SortOrder,
            InstrumentType = source.InstrumentType,
            MetadataJson = source.MetadataJson
        };
        var version = CloneVersion(sourceVersion, 1);
        version.QuestionnaireId = questionnaire.Id;
        if (targetStudyId is null)
        {
            version.InstrumentType = QuestionnaireLicenseStatuses.Custom;
            version.LicenseStatus = QuestionnaireLicenseStatuses.Custom;
            version.RedistributionStatus = QuestionnaireLicenseStatuses.UserProvided;
        }
        return await CreateAsync(questionnaire, version);
    }

    public static async Task PublishVersionAsync(string questionnaireId, string versionId)
    {
        var version = await GetVersionAsync(versionId) ?? throw new InvalidOperationException("Version not found.");
        if (version.Questions.Count == 0) throw new InvalidOperationException("A questionnaire version must contain at least one question.");
        var hash = ComputeSchemaHash(version);
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        await using var publish = Command(connection, transaction, """
            UPDATE QuestionnaireVersions SET Status='Published', IsImmutable=1,
                SchemaHash=$hash, PublishedAtUtc=$published WHERE Id=$id;
            """);
        Add(publish, "$hash", hash); Add(publish, "$published", DateTime.UtcNow); Add(publish, "$id", versionId);
        await publish.ExecuteNonQueryAsync();
        await using var root = Command(connection, transaction,
            "UPDATE Questionnaires SET CurrentVersionId=$version, UpdatedAtUtc=$updated WHERE Id=$id;");
        Add(root, "$version", versionId); Add(root, "$updated", DateTime.UtcNow); Add(root, "$id", questionnaireId);
        await root.ExecuteNonQueryAsync(); await transaction.CommitAsync();
    }

    public static async Task<QuestionnaireAssignment> AssignAsync(QuestionnaireAssignment assignment)
    {
        var version = await GetVersionAsync(assignment.QuestionnaireVersionId) ?? throw new InvalidOperationException("Version not found.");
        if (!version.IsImmutable) await PublishVersionAsync(version.QuestionnaireId, version.Id);
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO QuestionnaireAssignments
            (Id, StudyId, QuestionnaireVersionId, Placement, SortOrder, IsRequired, IsActive, CreatedAtUtc)
            VALUES ($id,$study,$version,$placement,$sort,$required,$active,$created)
            ON CONFLICT(StudyId, QuestionnaireVersionId, Placement) DO UPDATE SET
                SortOrder=excluded.SortOrder, IsRequired=excluded.IsRequired, IsActive=excluded.IsActive;
            """;
        Add(command, "$id", assignment.Id); Add(command, "$study", assignment.StudyId);
        Add(command, "$version", assignment.QuestionnaireVersionId); Add(command, "$placement", assignment.Placement);
        Add(command, "$sort", assignment.SortOrder); Add(command, "$required", assignment.IsRequired);
        Add(command, "$active", assignment.IsActive); Add(command, "$created", assignment.CreatedAtUtc);
        await command.ExecuteNonQueryAsync(); return assignment;
    }

    public static async Task<List<QuestionnaireAssignment>> GetAssignmentsAsync(string studyId, string? placement = null)
    {
        var result = new List<QuestionnaireAssignment>();
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, StudyId, QuestionnaireVersionId, Placement, SortOrder, IsRequired, IsActive, CreatedAtUtc
            FROM QuestionnaireAssignments
            WHERE StudyId=$study AND IsActive=1 AND ($placement IS NULL OR Placement=$placement)
            ORDER BY Placement, SortOrder;
            """;
        Add(command, "$study", studyId); Add(command, "$placement", placement);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new QuestionnaireAssignment
        {
            Id = reader.GetString(0), StudyId = reader.GetString(1), QuestionnaireVersionId = reader.GetString(2),
            Placement = reader.GetString(3), SortOrder = reader.GetInt32(4), IsRequired = reader.GetInt32(5) != 0,
            IsActive = reader.GetInt32(6) != 0, CreatedAtUtc = DateTime.Parse(reader.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind)
        });
        foreach (var assignment in result)
        {
            assignment.Version = await GetVersionAsync(assignment.QuestionnaireVersionId);
            if (assignment.Version is not null) assignment.Questionnaire = await GetByIdAsync(assignment.Version.QuestionnaireId);
        }
        return result;
    }

    public static async Task<QuestionnaireResponse?> GetCompletedResponseAsync(string assignmentId, string participantId, string? sessionId)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, AssignmentId, StudyId, SessionId, ParticipantId, QuestionnaireId,
                   QuestionnaireVersionId, StartedAtUtc, CompletedAtUtc, DurationMilliseconds,
                   Status, IsDemo, MetadataJson
            FROM QuestionnaireResponseSets
            WHERE AssignmentId=$assignment AND ParticipantId=$participant
              AND (($session IS NULL AND SessionId IS NULL) OR SessionId=$session)
              AND Status='Completed' ORDER BY CompletedAtUtc DESC LIMIT 1;
            """;
        Add(command, "$assignment", assignmentId); Add(command, "$participant", participantId); Add(command, "$session", sessionId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var response = ReadResponse(reader); await reader.DisposeAsync();
        response.Responses = await GetQuestionResponsesAsync(response.Id); return response;
    }

    public static async Task<QuestionnaireResponse> SaveCompletedResponseAsync(
        QuestionnaireResponse response,
        IReadOnlyList<QuestionResponse> answers)
    {
        var version = await GetVersionAsync(response.QuestionnaireVersionId) ?? throw new InvalidOperationException("Questionnaire version not found.");
        var required = version.Questions.Where(question => question.IsRequired).Select(question => question.Id).ToHashSet();
        var answered = answers.Where(answer => !string.IsNullOrWhiteSpace(answer.RawValue) || answer.NumericValue.HasValue)
            .Select(answer => answer.QuestionId).ToHashSet();
        if (!required.IsSubsetOf(answered)) throw new InvalidOperationException("All required questionnaire items must be answered.");
        var now = DateTime.UtcNow; response.CompletedAtUtc = now; response.Status = "Completed";
        response.DurationMilliseconds ??= Math.Max(0, (long)(now - response.StartedAtUtc).TotalMilliseconds);
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            await using (var command = Command(connection, transaction, """
                INSERT INTO QuestionnaireResponseSets
                (Id, AssignmentId, StudyId, SessionId, ParticipantId, QuestionnaireId,
                 QuestionnaireVersionId, StartedAtUtc, CompletedAtUtc, DurationMilliseconds,
                 Status, IsDemo, MetadataJson)
                VALUES ($id,$assignment,$study,$session,$participant,$questionnaire,$version,
                        $started,$completed,$duration,$status,$demo,$metadata);
                """))
            {
                Add(command, "$id", response.Id); Add(command, "$assignment", response.AssignmentId);
                Add(command, "$study", response.StudyId); Add(command, "$session", response.SessionId);
                Add(command, "$participant", response.ParticipantId); Add(command, "$questionnaire", response.QuestionnaireId);
                Add(command, "$version", response.QuestionnaireVersionId); Add(command, "$started", response.StartedAtUtc);
                Add(command, "$completed", response.CompletedAtUtc); Add(command, "$duration", response.DurationMilliseconds);
                Add(command, "$status", response.Status); Add(command, "$demo", response.IsDemo);
                Add(command, "$metadata", response.MetadataJson); await command.ExecuteNonQueryAsync();
            }
            foreach (var answer in answers)
            {
                answer.ResponseSetId = response.Id;
                await using var command = Command(connection, transaction, """
                    INSERT INTO QuestionResponseValues
                    (Id, ResponseSetId, QuestionId, RawValue, NumericValue, SelectedOptionIdsJson, RespondedAtUtc)
                    VALUES ($id,$response,$question,$raw,$numeric,$selected,$responded);
                    """);
                Add(command, "$id", answer.Id); Add(command, "$response", answer.ResponseSetId);
                Add(command, "$question", answer.QuestionId); Add(command, "$raw", answer.RawValue);
                Add(command, "$numeric", answer.NumericValue); Add(command, "$selected", answer.SelectedOptionIdsJson);
                Add(command, "$responded", answer.RespondedAtUtc); await command.ExecuteNonQueryAsync();
            }
            await using var immutable = Command(connection, transaction,
                "UPDATE QuestionnaireVersions SET IsImmutable=1 WHERE Id=$id;");
            Add(immutable, "$id", response.QuestionnaireVersionId); await immutable.ExecuteNonQueryAsync();
            await transaction.CommitAsync(); response.Responses = answers.ToList(); return response;
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public static async Task<List<QuestionnaireResponse>> GetResponsesByStudyAsync(string studyId, bool? isDemo = null)
    {
        var result = new List<QuestionnaireResponse>();
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, AssignmentId, StudyId, SessionId, ParticipantId, QuestionnaireId,
                   QuestionnaireVersionId, StartedAtUtc, CompletedAtUtc, DurationMilliseconds,
                   Status, IsDemo, MetadataJson
            FROM QuestionnaireResponseSets
            WHERE StudyId=$study AND ($demo IS NULL OR IsDemo=$demo)
            ORDER BY StartedAtUtc;
            """;
        Add(command, "$study", studyId); Add(command, "$demo", isDemo.HasValue ? (isDemo.Value ? 1 : 0) : null);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(ReadResponse(reader));
        await reader.DisposeAsync();
        foreach (var response in result) response.Responses = await GetQuestionResponsesAsync(response.Id);
        return result;
    }

    private static async Task<List<QuestionnaireVersion>> GetVersionsAsync(string questionnaireId)
    {
        var versions = new List<QuestionnaireVersion>();
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM QuestionnaireVersions WHERE QuestionnaireId=$id ORDER BY VersionNumber;";
        command.Parameters.AddWithValue("$id", questionnaireId);
        await using var reader = await command.ExecuteReaderAsync();
        var ids = new List<string>(); while (await reader.ReadAsync()) ids.Add(reader.GetString(0));
        foreach (var id in ids) { var version = await GetVersionAsync(id); if (version is not null) versions.Add(version); }
        return versions;
    }

    private static async Task<QuestionnaireVersion?> LoadVersionAsync(SqliteConnection connection, string id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, QuestionnaireId, VersionNumber, VersionLabel, Status, Language,
                   InstrumentType, Construct, Citation, LicenseStatus, LicenseReference,
                   RedistributionStatus, ValidationNotes, TranslationNotes, ScoringAvailability,
                   SchemaHash, IsImmutable, CreatedAtUtc, PublishedAtUtc
            FROM QuestionnaireVersions WHERE Id=$id LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var version = new QuestionnaireVersion
        {
            Id = reader.GetString(0), QuestionnaireId = reader.GetString(1), VersionNumber = reader.GetInt32(2),
            VersionLabel = NullableString(reader, 3), Status = reader.GetString(4), Language = reader.GetString(5),
            InstrumentType = reader.GetString(6), Construct = NullableString(reader, 7), Citation = NullableString(reader, 8),
            LicenseStatus = reader.GetString(9), LicenseReference = NullableString(reader, 10), RedistributionStatus = reader.GetString(11),
            ValidationNotes = NullableString(reader, 12), TranslationNotes = NullableString(reader, 13), ScoringAvailability = NullableString(reader, 14),
            SchemaHash = NullableString(reader, 15), IsImmutable = reader.GetInt32(16) != 0,
            CreatedAtUtc = ParseDate(reader.GetString(17)), PublishedAtUtc = NullableDate(reader, 18)
        };
        await reader.DisposeAsync();
        version.Sections = await LoadSectionsAsync(connection, id);
        version.Questions = await LoadQuestionsAsync(connection, id);
        version.Scales = await LoadScalesAsync(connection, id);
        return version;
    }

    private static async Task<List<QuestionnaireSection>> LoadSectionsAsync(SqliteConnection connection, string versionId)
    {
        var result = new List<QuestionnaireSection>(); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, QuestionnaireVersionId, Title, Description, SortOrder FROM QuestionnaireSections WHERE QuestionnaireVersionId=$id ORDER BY SortOrder;";
        command.Parameters.AddWithValue("$id", versionId); await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new QuestionnaireSection { Id=reader.GetString(0), QuestionnaireVersionId=reader.GetString(1), Title=reader.GetString(2), Description=NullableString(reader,3), SortOrder=reader.GetInt32(4) });
        return result;
    }

    private static async Task<List<Question>> LoadQuestionsAsync(SqliteConnection connection, string versionId)
    {
        var result = new List<Question>(); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, QuestionnaireVersionId, SectionId, VariableName, QuestionText, QuestionType, MeasurementLevel, IsRequired, SortOrder, ConfigurationJson FROM QuestionnaireItems WHERE QuestionnaireVersionId=$id ORDER BY SortOrder;";
        command.Parameters.AddWithValue("$id", versionId); await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new Question { Id=reader.GetString(0), QuestionnaireVersionId=reader.GetString(1), SectionId=NullableString(reader,2), VariableName=reader.GetString(3), QuestionText=reader.GetString(4), QuestionType=reader.GetString(5), MeasurementLevel=reader.GetString(6), IsRequired=reader.GetInt32(7)!=0, SortOrder=reader.GetInt32(8), ConfigurationJson=NullableString(reader,9) });
        await reader.DisposeAsync(); foreach(var question in result) question.Options=await LoadOptionsAsync(connection,question.Id); return result;
    }

    private static async Task<List<QuestionOption>> LoadOptionsAsync(SqliteConnection connection, string questionId)
    {
        var result=new List<QuestionOption>(); await using var command=connection.CreateCommand(); command.CommandText="SELECT Id,QuestionId,ValueCode,NumericCode,DisplayLabel,SortOrder FROM QuestionOptions WHERE QuestionId=$id ORDER BY SortOrder;";command.Parameters.AddWithValue("$id",questionId);await using var reader=await command.ExecuteReaderAsync();while(await reader.ReadAsync())result.Add(new QuestionOption{Id=reader.GetString(0),QuestionId=reader.GetString(1),ValueCode=reader.GetString(2),NumericCode=reader.IsDBNull(3)?null:reader.GetDouble(3),DisplayLabel=reader.GetString(4),SortOrder=reader.GetInt32(5)});return result;
    }

    private static async Task<List<QuestionnaireScale>> LoadScalesAsync(SqliteConnection connection,string versionId)
    {
        var result=new List<QuestionnaireScale>();await using var command=connection.CreateCommand();command.CommandText="SELECT Id,QuestionnaireVersionId,Name,VariableName,ScoringMethod,MissingItemRule,MinimumAnsweredItems FROM QuestionnaireScales WHERE QuestionnaireVersionId=$id;";command.Parameters.AddWithValue("$id",versionId);await using var reader=await command.ExecuteReaderAsync();while(await reader.ReadAsync())result.Add(new QuestionnaireScale{Id=reader.GetString(0),QuestionnaireVersionId=reader.GetString(1),Name=reader.GetString(2),VariableName=reader.GetString(3),ScoringMethod=reader.GetString(4),MissingItemRule=reader.GetString(5),MinimumAnsweredItems=reader.GetInt32(6)});await reader.DisposeAsync();foreach(var scale in result)scale.Items=await LoadScaleItemsAsync(connection,scale.Id);return result;
    }

    private static async Task<List<QuestionnaireScaleItem>> LoadScaleItemsAsync(SqliteConnection connection,string scaleId)
    {
        var result=new List<QuestionnaireScaleItem>();await using var command=connection.CreateCommand();command.CommandText="SELECT Id,ScaleId,QuestionId,IsReverseCoded,ReverseMinimum,ReverseMaximum,Weight FROM QuestionnaireScaleItems WHERE ScaleId=$id;";command.Parameters.AddWithValue("$id",scaleId);await using var reader=await command.ExecuteReaderAsync();while(await reader.ReadAsync())result.Add(new QuestionnaireScaleItem{Id=reader.GetString(0),ScaleId=reader.GetString(1),QuestionId=reader.GetString(2),IsReverseCoded=reader.GetInt32(3)!=0,ReverseMinimum=reader.IsDBNull(4)?null:reader.GetDouble(4),ReverseMaximum=reader.IsDBNull(5)?null:reader.GetDouble(5),Weight=reader.GetDouble(6)});return result;
    }

    public static async Task<QuestionnaireScale> AddScaleAsync(QuestionnaireScale scale)
    {
        await EnsureEditableAsync(scale.QuestionnaireVersionId);await using var connection=await DatabaseService.OpenConnectionAsync();await using var transaction=connection.BeginTransaction();await using(var command=Command(connection,transaction,"INSERT INTO QuestionnaireScales (Id,QuestionnaireVersionId,Name,VariableName,ScoringMethod,MissingItemRule,MinimumAnsweredItems) VALUES ($id,$version,$name,$variable,$method,$missing,$minimum);")){Add(command,"$id",scale.Id);Add(command,"$version",scale.QuestionnaireVersionId);Add(command,"$name",scale.Name);Add(command,"$variable",scale.VariableName);Add(command,"$method",scale.ScoringMethod);Add(command,"$missing",scale.MissingItemRule);Add(command,"$minimum",scale.MinimumAnsweredItems);await command.ExecuteNonQueryAsync();}foreach(var item in scale.Items){item.ScaleId=scale.Id;await using var command=Command(connection,transaction,"INSERT INTO QuestionnaireScaleItems (Id,ScaleId,QuestionId,IsReverseCoded,ReverseMinimum,ReverseMaximum,Weight) VALUES ($id,$scale,$question,$reverse,$minimum,$maximum,$weight);");Add(command,"$id",item.Id);Add(command,"$scale",item.ScaleId);Add(command,"$question",item.QuestionId);Add(command,"$reverse",item.IsReverseCoded);Add(command,"$minimum",item.ReverseMinimum);Add(command,"$maximum",item.ReverseMaximum);Add(command,"$weight",item.Weight);await command.ExecuteNonQueryAsync();}await transaction.CommitAsync();return scale;
    }

    private static async Task<List<QuestionResponse>> GetQuestionResponsesAsync(string responseSetId)
    {
        var result=new List<QuestionResponse>();await using var connection=await DatabaseService.OpenConnectionAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT Id,ResponseSetId,QuestionId,RawValue,NumericValue,SelectedOptionIdsJson,RespondedAtUtc FROM QuestionResponseValues WHERE ResponseSetId=$id;";command.Parameters.AddWithValue("$id",responseSetId);await using var reader=await command.ExecuteReaderAsync();while(await reader.ReadAsync())result.Add(new QuestionResponse{Id=reader.GetString(0),ResponseSetId=reader.GetString(1),QuestionId=reader.GetString(2),RawValue=NullableString(reader,3),NumericValue=reader.IsDBNull(4)?null:reader.GetDouble(4),SelectedOptionIdsJson=NullableString(reader,5),RespondedAtUtc=ParseDate(reader.GetString(6))});return result;
    }

    private static async Task EnsureEditableAsync(string versionId)
    {
        await using var connection=await DatabaseService.OpenConnectionAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT IsImmutable,(SELECT COUNT(*) FROM QuestionnaireResponseSets WHERE QuestionnaireVersionId=$id) FROM QuestionnaireVersions WHERE Id=$id;";command.Parameters.AddWithValue("$id",versionId);await using var reader=await command.ExecuteReaderAsync();if(!await reader.ReadAsync())throw new InvalidOperationException("Questionnaire version not found.");if(reader.GetInt32(0)!=0||reader.GetInt64(1)>0)throw new InvalidOperationException("This questionnaire version is immutable. Create a new version before editing.");
    }

    private static async Task InsertVersionAsync(SqliteConnection connection,SqliteTransaction transaction,QuestionnaireVersion version)
    {
        await using(var command=Command(connection,transaction,"INSERT INTO QuestionnaireVersions (Id,QuestionnaireId,VersionNumber,VersionLabel,Status,Language,InstrumentType,Construct,Citation,LicenseStatus,LicenseReference,RedistributionStatus,ValidationNotes,TranslationNotes,ScoringAvailability,SchemaHash,IsImmutable,CreatedAtUtc,PublishedAtUtc) VALUES ($id,$questionnaire,$number,$label,$status,$language,$type,$construct,$citation,$license,$licenseReference,$redistribution,$validation,$translation,$scoring,$hash,$immutable,$created,$published);")){Add(command,"$id",version.Id);Add(command,"$questionnaire",version.QuestionnaireId);Add(command,"$number",version.VersionNumber);Add(command,"$label",version.VersionLabel);Add(command,"$status",version.Status);Add(command,"$language",version.Language);Add(command,"$type",version.InstrumentType);Add(command,"$construct",version.Construct);Add(command,"$citation",version.Citation);Add(command,"$license",version.LicenseStatus);Add(command,"$licenseReference",version.LicenseReference);Add(command,"$redistribution",version.RedistributionStatus);Add(command,"$validation",version.ValidationNotes);Add(command,"$translation",version.TranslationNotes);Add(command,"$scoring",version.ScoringAvailability);Add(command,"$hash",version.SchemaHash);Add(command,"$immutable",version.IsImmutable);Add(command,"$created",version.CreatedAtUtc);Add(command,"$published",version.PublishedAtUtc);await command.ExecuteNonQueryAsync();}foreach(var section in version.Sections){section.QuestionnaireVersionId=version.Id;await using var command=Command(connection,transaction,"INSERT INTO QuestionnaireSections (Id,QuestionnaireVersionId,Title,Description,SortOrder) VALUES ($id,$version,$title,$description,$sort);");Add(command,"$id",section.Id);Add(command,"$version",section.QuestionnaireVersionId);Add(command,"$title",section.Title);Add(command,"$description",section.Description);Add(command,"$sort",section.SortOrder);await command.ExecuteNonQueryAsync();}foreach(var question in version.Questions){question.QuestionnaireVersionId=version.Id;await InsertQuestionAsync(connection,transaction,question);}foreach(var scale in version.Scales){scale.QuestionnaireVersionId=version.Id;await using(var command=Command(connection,transaction,"INSERT INTO QuestionnaireScales (Id,QuestionnaireVersionId,Name,VariableName,ScoringMethod,MissingItemRule,MinimumAnsweredItems) VALUES ($id,$version,$name,$variable,$method,$missing,$minimum);")){Add(command,"$id",scale.Id);Add(command,"$version",scale.QuestionnaireVersionId);Add(command,"$name",scale.Name);Add(command,"$variable",scale.VariableName);Add(command,"$method",scale.ScoringMethod);Add(command,"$missing",scale.MissingItemRule);Add(command,"$minimum",scale.MinimumAnsweredItems);await command.ExecuteNonQueryAsync();}foreach(var item in scale.Items){item.ScaleId=scale.Id;await using var command=Command(connection,transaction,"INSERT INTO QuestionnaireScaleItems (Id,ScaleId,QuestionId,IsReverseCoded,ReverseMinimum,ReverseMaximum,Weight) VALUES ($id,$scale,$question,$reverse,$minimum,$maximum,$weight);");Add(command,"$id",item.Id);Add(command,"$scale",item.ScaleId);Add(command,"$question",item.QuestionId);Add(command,"$reverse",item.IsReverseCoded);Add(command,"$minimum",item.ReverseMinimum);Add(command,"$maximum",item.ReverseMaximum);Add(command,"$weight",item.Weight);await command.ExecuteNonQueryAsync();}}}

    private static async Task InsertQuestionAsync(SqliteConnection connection,SqliteTransaction transaction,Question question)
    {
        await using(var command=Command(connection,transaction,"INSERT INTO QuestionnaireItems (Id,QuestionnaireVersionId,SectionId,VariableName,QuestionText,QuestionType,MeasurementLevel,IsRequired,SortOrder,ConfigurationJson) VALUES ($id,$version,$section,$variable,$text,$type,$level,$required,$sort,$configuration);")){Add(command,"$id",question.Id);Add(command,"$version",question.QuestionnaireVersionId);Add(command,"$section",question.SectionId);Add(command,"$variable",question.VariableName);Add(command,"$text",question.QuestionText);Add(command,"$type",question.QuestionType);Add(command,"$level",question.MeasurementLevel);Add(command,"$required",question.IsRequired);Add(command,"$sort",question.SortOrder);Add(command,"$configuration",question.ConfigurationJson);await command.ExecuteNonQueryAsync();}foreach(var option in question.Options){option.QuestionId=question.Id;await using var command=Command(connection,transaction,"INSERT INTO QuestionOptions (Id,QuestionId,ValueCode,NumericCode,DisplayLabel,SortOrder) VALUES ($id,$question,$code,$numeric,$label,$sort);");Add(command,"$id",option.Id);Add(command,"$question",option.QuestionId);Add(command,"$code",option.ValueCode);Add(command,"$numeric",option.NumericCode);Add(command,"$label",option.DisplayLabel);Add(command,"$sort",option.SortOrder);await command.ExecuteNonQueryAsync();}
    }

    private static QuestionnaireVersion CloneVersion(QuestionnaireVersion source,int number)
    {
        var clone=new QuestionnaireVersion{QuestionnaireId=source.QuestionnaireId,VersionNumber=number,VersionLabel=$"Version {number}",Status="Draft",Language=source.Language,InstrumentType=source.InstrumentType,Construct=source.Construct,Citation=source.Citation,LicenseStatus=source.LicenseStatus,LicenseReference=source.LicenseReference,RedistributionStatus=source.RedistributionStatus,ValidationNotes=source.ValidationNotes,TranslationNotes=source.TranslationNotes,ScoringAvailability=source.ScoringAvailability};var sectionMap=source.Sections.ToDictionary(item=>item.Id,item=>new QuestionnaireSection{Title=item.Title,Description=item.Description,SortOrder=item.SortOrder});clone.Sections=sectionMap.Values.ToList();var questionMap=new Dictionary<string,Question>();foreach(var question in source.Questions){var copy=new Question{QuestionnaireVersionId=clone.Id,SectionId=question.SectionId is not null&&sectionMap.TryGetValue(question.SectionId,out var section)?section.Id:null,VariableName=question.VariableName,QuestionText=question.QuestionText,QuestionType=question.QuestionType,MeasurementLevel=question.MeasurementLevel,IsRequired=question.IsRequired,SortOrder=question.SortOrder,ConfigurationJson=question.ConfigurationJson,Options=question.Options.Select(option=>new QuestionOption{ValueCode=option.ValueCode,NumericCode=option.NumericCode,DisplayLabel=option.DisplayLabel,SortOrder=option.SortOrder}).ToList()};questionMap[question.Id]=copy;clone.Questions.Add(copy);}foreach(var scale in source.Scales){var copy=new QuestionnaireScale{QuestionnaireVersionId=clone.Id,Name=scale.Name,VariableName=scale.VariableName,ScoringMethod=scale.ScoringMethod,MissingItemRule=scale.MissingItemRule,MinimumAnsweredItems=scale.MinimumAnsweredItems};copy.Items=scale.Items.Where(item=>questionMap.ContainsKey(item.QuestionId)).Select(item=>new QuestionnaireScaleItem{ScaleId=copy.Id,QuestionId=questionMap[item.QuestionId].Id,IsReverseCoded=item.IsReverseCoded,ReverseMinimum=item.ReverseMinimum,ReverseMaximum=item.ReverseMaximum,Weight=item.Weight}).ToList();clone.Scales.Add(copy);}return clone;
    }

    private static string ComputeSchemaHash(QuestionnaireVersion version)
    {
        var canonical=JsonSerializer.Serialize(new{version.VersionNumber,Sections=version.Sections.OrderBy(x=>x.SortOrder).Select(x=>new{x.Title,x.SortOrder}),Questions=version.Questions.OrderBy(x=>x.SortOrder).Select(x=>new{x.VariableName,x.QuestionText,x.QuestionType,x.MeasurementLevel,x.IsRequired,x.SortOrder,Options=x.Options.OrderBy(o=>o.SortOrder).Select(o=>new{o.ValueCode,o.NumericCode,o.DisplayLabel,o.SortOrder})}),Scales=version.Scales.Select(x=>new{x.VariableName,x.ScoringMethod,x.MissingItemRule,x.MinimumAnsweredItems,Items=x.Items.Select(i=>new{i.QuestionId,i.IsReverseCoded,i.ReverseMinimum,i.ReverseMaximum,i.Weight})})});return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static void Validate(Questionnaire questionnaire,QuestionnaireVersion version){if(string.IsNullOrWhiteSpace(questionnaire.StudyId)||string.IsNullOrWhiteSpace(questionnaire.Title))throw new ArgumentException("Study and title are required.");if(version.QuestionnaireId!=questionnaire.Id)throw new ArgumentException("Questionnaire version relationship is invalid.");}
    private static Questionnaire ReadQuestionnaire(SqliteDataReader reader)=>new(){Id=reader.GetString(0),StudyId=reader.GetString(1),Title=reader.GetString(2),Description=NullableString(reader,3),SortOrder=reader.GetInt32(4),CreatedAtUtc=ParseDate(reader.GetString(5)),UpdatedAtUtc=ParseDate(reader.GetString(6)),IsActive=reader.GetInt32(7)!=0,CurrentVersionId=NullableString(reader,8),InstrumentType=reader.GetString(9),MetadataJson=NullableString(reader,10)};
    private static QuestionnaireResponse ReadResponse(SqliteDataReader reader)=>new(){Id=reader.GetString(0),AssignmentId=reader.GetString(1),StudyId=reader.GetString(2),SessionId=NullableString(reader,3),ParticipantId=reader.GetString(4),QuestionnaireId=reader.GetString(5),QuestionnaireVersionId=reader.GetString(6),StartedAtUtc=ParseDate(reader.GetString(7)),CompletedAtUtc=NullableDate(reader,8),DurationMilliseconds=reader.IsDBNull(9)?null:reader.GetInt64(9),Status=reader.GetString(10),IsDemo=reader.GetInt32(11)!=0,MetadataJson=NullableString(reader,12)};
    private static SqliteCommand Command(SqliteConnection connection,SqliteTransaction transaction,string sql){var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText=sql;return command;}
    private static void Add(SqliteCommand command,string name,object? value){command.Parameters.AddWithValue(name,value switch{null=>DBNull.Value,DateTime date=>date.ToString("O"),bool boolean=>boolean?1:0,_=>value});}
    private static string? NullableString(SqliteDataReader reader,int ordinal)=>reader.IsDBNull(ordinal)?null:reader.GetString(ordinal);
    private static DateTime ParseDate(string value)=>DateTime.Parse(value,null,System.Globalization.DateTimeStyles.RoundtripKind);
    private static DateTime? NullableDate(SqliteDataReader reader,int ordinal)=>reader.IsDBNull(ordinal)?null:ParseDate(reader.GetString(ordinal));
}
