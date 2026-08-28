using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SOCYVIA.Repositories;

namespace SOCYVIA.Data;

public static class DatabaseInitializer
{
    private const int CurrentSchemaVersion = 10;


    // =========================================================
    // INITIALIZE
    // =========================================================

    public static async Task InitializeAsync()
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();


        // =====================================================
        // 1. CREATE / VERIFY TABLES
        //
        // IMPORTANT:
        // Do NOT create indexes here.
        //
        // An existing old table is not modified by
        // CREATE TABLE IF NOT EXISTS.
        // =====================================================

        await CreateSchemaInfoTableAsync(connection);

        await CreateResearchersTableAsync(connection);

        await CreateStudiesTableAsync(connection);

        await CreateGroupsTableAsync(connection);

        await CreateExperimentalConditionsTableAsync(connection);

        await CreateParticipantsTableAsync(connection);

        await CreateParticipantAssignmentsTableAsync(connection);

        await CreateParticipantConditionAssignmentsTableAsync(connection);

        await CreateSessionsTableAsync(connection);

        await CreateExperimentConfigurationSnapshotsTableAsync(connection);

        await CreateStimuliTableAsync(connection);

        await CreateContentItemsTableAsync(connection);

        await CreateEngagementObservationsTableAsync(connection);

        await CreateExperimentalFeedsTableAsync(connection);

        await CreateExperimentalFeedItemsTableAsync(connection);

        await CreateManagedMediaAssetsTableAsync(connection);

        await CreateDemoInstallationsTableAsync(connection);

        await CreateResearchDataClassificationsTableAsync(connection);

        await CreateExperimentBlocksTableAsync(connection);

        await CreateEventsTableAsync(connection);

        await CreateQuestionnairesTableAsync(connection);

        await CreateQuestionsTableAsync(connection);

        await CreateResponsesTableAsync(connection);

        await CreateScientificQuestionnaireTablesAsync(connection);

        await CreateScientificAnalysisTablesAsync(connection);

        // Researcher-owned local cache for normalized remote records. This is additive
        // and deliberately separate from validated local scientific session tables.
        await RemoteResearchRepository.EnsureSchemaAsync();


        // =====================================================
        // 2. MIGRATE EXISTING DATABASE
        // =====================================================

        await MigrateStudiesToVersion2Async(connection);

        await MigrateGroupsToVersion2Async(connection);

        await MigrateParticipantsToVersion2Async(connection);

        await MigrateSessionsToVersion2Async(connection);

        await MigrateSessionsToVersion4Async(connection);

        await MigrateSnapshotsToVersion5Async(connection);

        await MigrateStimuliToVersion2Async(connection);

        await MigrateEventsToVersion2Async(connection);

        await MigrateEventsToVersion5Async(connection);

        await MigrateContentArchitectureToVersion6Async(connection);

        await MigrateProductExperienceToVersion7Async(connection);

        await MigrateScientificCoreToVersion9Async(connection);

        await MigratePublishedMediaUrlsToVersion10Async(connection);


        // =====================================================
        // 3. INDEXES
        //
        // Only now are all V2 columns guaranteed to exist.
        // =====================================================

        await CreateIndexesAsync(connection);


        // =====================================================
        // 4. SCHEMA VERSION
        // =====================================================

        await SetSchemaVersionAsync(
            connection,
            CurrentSchemaVersion);
    }


    // =========================================================
    // SCHEMA INFO
    // =========================================================

    private static async Task CreateSchemaInfoTableAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS SchemaInfo
            (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),

                SchemaVersion INTEGER NOT NULL,

                UpdatedAtUtc TEXT NOT NULL
            );
            """;

        await ExecuteAsync(
            connection,
            sql);
    }


    // =========================================================
    // RESEARCHERS
    // =========================================================

    private static async Task CreateResearchersTableAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS Researchers
            (
                Id TEXT PRIMARY KEY,

                FullName TEXT NOT NULL,

                CreatedAtUtc TEXT NOT NULL,

                LastAccessAtUtc TEXT NOT NULL
            );
            """;

        await ExecuteAsync(
            connection,
            sql);
    }


    // =========================================================
    // STUDIES
    // =========================================================

    private static async Task CreateStudiesTableAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS Studies
            (
                Id TEXT PRIMARY KEY,

                ResearcherId TEXT NOT NULL,

                Title TEXT NOT NULL,

                Description TEXT,

                Status TEXT NOT NULL
                    DEFAULT 'Draft',

                StudyType TEXT NOT NULL
                    DEFAULT 'Experimental',

                DesignType TEXT NOT NULL
                    DEFAULT 'BetweenSubjects',

                AssignmentMethod TEXT NOT NULL
                    DEFAULT 'Manual',

                RandomizeStimuli INTEGER NOT NULL
                    DEFAULT 0,

                RandomizationSeed INTEGER,

                UsesStimuli INTEGER NOT NULL
                    DEFAULT 1,

                UsesQuestionnaires INTEGER NOT NULL
                    DEFAULT 0,

                UsesPhysiologicalData INTEGER NOT NULL
                    DEFAULT 0,

                EegEnabled INTEGER NOT NULL
                    DEFAULT 0,

                GsrEnabled INTEGER NOT NULL
                    DEFAULT 0,

                TargetSampleSize INTEGER,

                ExpectedSessionDurationMinutes INTEGER,

                AllowSessionResume INTEGER NOT NULL
                    DEFAULT 1,

                RequireParticipantConsent INTEGER NOT NULL
                    DEFAULT 1,

                ConsentText TEXT,

                ResearchQuestion TEXT,

                Hypothesis TEXT,

                PopulationDescription TEXT,

                InclusionCriteria TEXT,

                ExclusionCriteria TEXT,

                MetadataJson TEXT,

                CreatedAtUtc TEXT NOT NULL,

                UpdatedAtUtc TEXT NOT NULL,

                StartedAtUtc TEXT,

                CompletedAtUtc TEXT,

                IsArchived INTEGER NOT NULL
                    DEFAULT 0,

                FOREIGN KEY (ResearcherId)
                    REFERENCES Researchers(Id)
                    ON DELETE CASCADE
            );
            """;

        await ExecuteAsync(
            connection,
            sql);
    }


    // =========================================================
    // GROUPS
    // =========================================================

    private static async Task CreateGroupsTableAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS Groups
            (
                Id TEXT PRIMARY KEY,

                StudyId TEXT NOT NULL,

                Name TEXT NOT NULL,

                Description TEXT,

                ColorHex TEXT
                    DEFAULT '#6259EA',

                IsControlGroup INTEGER NOT NULL
                    DEFAULT 0,

                SortOrder INTEGER NOT NULL
                    DEFAULT 0,

                TargetSampleSize INTEGER,

                IsActive INTEGER NOT NULL
                    DEFAULT 1,

                CreatedAtUtc TEXT NOT NULL,

                UpdatedAtUtc TEXT,

                FOREIGN KEY (StudyId)
                    REFERENCES Studies(Id)
                    ON DELETE CASCADE
            );
            """;

        await ExecuteAsync(
            connection,
            sql);
    }


    // =========================================================
    // EXPERIMENTAL CONDITIONS
    // =========================================================

    private static async Task CreateExperimentalConditionsTableAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS ExperimentalConditions
            (
                Id TEXT PRIMARY KEY,

                StudyId TEXT NOT NULL,

                GroupId TEXT,

                Name TEXT NOT NULL,

                Description TEXT,

                ConditionType TEXT NOT NULL,

                SortOrder INTEGER NOT NULL,

                IsControlCondition INTEGER NOT NULL,

                IsActive INTEGER NOT NULL,

                ManipulationJson TEXT,

                CreatedAtUtc TEXT NOT NULL,

                UpdatedAtUtc TEXT NOT NULL,

                FOREIGN KEY (StudyId)
                    REFERENCES Studies(Id)
                    ON DELETE CASCADE,

                FOREIGN KEY (GroupId)
                    REFERENCES Groups(Id)
                    ON DELETE SET NULL
            );
            """;

        await ExecuteAsync(
            connection,
            sql);
    }


    // =========================================================
    // PARTICIPANTS
    // =========================================================

    private static async Task CreateParticipantsTableAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS Participants
            (
                Id TEXT PRIMARY KEY,

                StudyId TEXT NOT NULL,

                GroupId TEXT,

                ParticipantCode TEXT NOT NULL,

                Status TEXT NOT NULL
                    DEFAULT 'Active',

                Age INTEGER,

                Gender TEXT,

                EducationLevel TEXT,

                Occupation TEXT,

                IsEligible INTEGER NOT NULL
                    DEFAULT 1,

                EligibilityNotes TEXT,

                ConsentAccepted INTEGER NOT NULL
                    DEFAULT 0,

                ConsentAcceptedAtUtc TEXT,

                HasStartedStudy INTEGER NOT NULL
                    DEFAULT 0,

                HasCompletedStudy INTEGER NOT NULL
                    DEFAULT 0,

                StudyStartedAtUtc TEXT,

                StudyCompletedAtUtc TEXT,

                IsExcluded INTEGER NOT NULL
                    DEFAULT 0,

                ExclusionReason TEXT,

                HasWithdrawn INTEGER NOT NULL
                    DEFAULT 0,

                WithdrawalReason TEXT,

                ResearcherNotes TEXT,

                MetadataJson TEXT,

                CreatedAtUtc TEXT NOT NULL,

                UpdatedAtUtc TEXT,

                FOREIGN KEY (StudyId)
                    REFERENCES Studies(Id)
                    ON DELETE CASCADE,

                FOREIGN KEY (GroupId)
                    REFERENCES Groups(Id)
                    ON DELETE SET NULL
            );
            """;

        await ExecuteAsync(
            connection,
            sql);
    }


    // =========================================================
    // PARTICIPANT ASSIGNMENTS
    // =========================================================

    private static async Task CreateParticipantAssignmentsTableAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS ParticipantAssignments
            (
                Id TEXT PRIMARY KEY,

                StudyId TEXT NOT NULL,

                ParticipantId TEXT NOT NULL,

                GroupId TEXT NOT NULL,

                AssignmentMethod TEXT NOT NULL
                    DEFAULT 'Manual',

                RandomizationSeed INTEGER,

                AssignmentOrder INTEGER,

                IsActive INTEGER NOT NULL
                    DEFAULT 1,

                AssignedAtUtc TEXT NOT NULL,

                Notes TEXT,

                FOREIGN KEY (StudyId)
                    REFERENCES Studies(Id)
                    ON DELETE CASCADE,

                FOREIGN KEY (ParticipantId)
                    REFERENCES Participants(Id)
                    ON DELETE CASCADE,

                FOREIGN KEY (GroupId)
                    REFERENCES Groups(Id)
                    ON DELETE CASCADE
            );
            """;

        await ExecuteAsync(
            connection,
            sql);
    }


    // =========================================================
    // PARTICIPANT CONDITION ASSIGNMENTS
    // =========================================================

    private static async Task
        CreateParticipantConditionAssignmentsTableAsync(
            SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS ParticipantConditionAssignments
            (
                Id TEXT PRIMARY KEY,
                StudyId TEXT NOT NULL,
                ParticipantId TEXT NOT NULL,
                ConditionId TEXT NOT NULL,
                AssignmentMethod TEXT NOT NULL,
                RandomizationSeed INTEGER,
                AssignmentMetadataJson TEXT,
                AssignedAtUtc TEXT NOT NULL,
                IsActive INTEGER NOT NULL,

                FOREIGN KEY (StudyId)
                    REFERENCES Studies(Id)
                    ON DELETE CASCADE,

                FOREIGN KEY (ParticipantId)
                    REFERENCES Participants(Id)
                    ON DELETE CASCADE,

                FOREIGN KEY (ConditionId)
                    REFERENCES ExperimentalConditions(Id)
                    ON DELETE RESTRICT
            );
            """;

        await ExecuteAsync(
            connection,
            sql);
    }


    // =========================================================
    // SESSIONS
    // =========================================================

    private static async Task CreateSessionsTableAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS Sessions
            (
                Id TEXT PRIMARY KEY,

                StudyId TEXT NOT NULL,

                ParticipantId TEXT NOT NULL,

                GroupId TEXT,

                ConditionId TEXT,

                ConfigurationSnapshotId TEXT,

                CreatedAtUtc TEXT,

                LifecycleUpdatedAtUtc TEXT,

                StartedAtUtc TEXT NOT NULL,

                ActualStartedAtUtc TEXT,

                CompletedAtUtc TEXT,

                Status TEXT NOT NULL
                    DEFAULT 'Running',

                LifecycleVersion INTEGER NOT NULL
                    DEFAULT 1,

                DurationMilliseconds INTEGER,

                CurrentStimulusIndex INTEGER NOT NULL
                    DEFAULT 0,

                CompletedStimulusCount INTEGER NOT NULL
                    DEFAULT 0,

                DeviceName TEXT,

                OperatingSystem TEXT,

                ScreenWidth INTEGER,

                ScreenHeight INTEGER,

                EegEnabled INTEGER NOT NULL
                    DEFAULT 0,

                GsrEnabled INTEGER NOT NULL
                    DEFAULT 0,

                EegDeviceId TEXT,

                GsrDeviceId TEXT,

                SynchronizationSessionId TEXT,

                WasInterrupted INTEGER NOT NULL
                    DEFAULT 0,

                InterruptionReason TEXT,

                ResearcherNotes TEXT,

                MetadataJson TEXT,

                FOREIGN KEY (StudyId)
                    REFERENCES Studies(Id)
                    ON DELETE CASCADE,

                FOREIGN KEY (ParticipantId)
                    REFERENCES Participants(Id)
                    ON DELETE CASCADE,

                FOREIGN KEY (GroupId)
                    REFERENCES Groups(Id)
                    ON DELETE SET NULL
            );
            """;

        await ExecuteAsync(
            connection,
            sql);
    }


    // =========================================================
    // IMMUTABLE EXPERIMENT CONFIGURATION SNAPSHOTS
    // =========================================================

    private static async Task
        CreateExperimentConfigurationSnapshotsTableAsync(
            SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS ExperimentConfigurationSnapshots
            (
                Id TEXT PRIMARY KEY,
                SessionId TEXT NOT NULL UNIQUE,
                StudyId TEXT NOT NULL,
                ParticipantId TEXT NOT NULL,
                GroupId TEXT,
                ConditionId TEXT NOT NULL,
                SnapshotVersion TEXT NOT NULL,
                SnapshotJson TEXT NOT NULL,
                IntegrityHash TEXT,
                IntegrityHashAlgorithm TEXT,
                CreatedAtUtc TEXT NOT NULL,

                FOREIGN KEY (SessionId)
                    REFERENCES Sessions(Id)
                    ON DELETE RESTRICT
            );
            """;

        await ExecuteAsync(
            connection,
            sql);
    }


    // =========================================================
    // STIMULI
    //
    // StimulusPost model is stored here.
    // =========================================================

    private static async Task CreateStimuliTableAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS Stimuli
            (
                Id TEXT PRIMARY KEY,

                StudyId TEXT NOT NULL,

                GroupId TEXT,

                Name TEXT NOT NULL,

                StimulusType TEXT NOT NULL,

                Platform TEXT NOT NULL
                    DEFAULT 'Generic',

                SourceName TEXT,

                AuthorName TEXT,

                OriginalUrl TEXT,

                PublishedAtUtc TEXT,

                SourcePath TEXT,

                ThumbnailPath TEXT,

                PublishedMediaUrl TEXT,

                ContentText TEXT,

                Category TEXT,

                Topic TEXT,

                ConditionLabel TEXT,

                ExperimentalTag TEXT,

                OriginalLikes INTEGER,

                OriginalComments INTEGER,

                OriginalShares INTEGER,

                OriginalSaves INTEGER,

                OriginalViews INTEGER,

                SortOrder INTEGER NOT NULL
                    DEFAULT 0,

                IsActive INTEGER NOT NULL
                    DEFAULT 1,

                MinimumExposureMilliseconds INTEGER NOT NULL
                    DEFAULT 0,

                MaximumExposureMilliseconds INTEGER,

                AllowRandomization INTEGER NOT NULL
                    DEFAULT 1,

                MetadataJson TEXT,

                ResearcherNotes TEXT,

                CreatedAtUtc TEXT NOT NULL,

                UpdatedAtUtc TEXT,

                FOREIGN KEY (StudyId)
                    REFERENCES Studies(Id)
                    ON DELETE CASCADE,

                FOREIGN KEY (GroupId)
                    REFERENCES Groups(Id)
                    ON DELETE SET NULL
            );
            """;

        await ExecuteAsync(
            connection,
            sql);
    }


    // =========================================================
    // CONTENT LIBRARY (V6)
    // Source truth owned by a researcher, independent of study groups.
    // =========================================================

    private static async Task CreateContentItemsTableAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS ContentItems
            (
                Id TEXT PRIMARY KEY,
                ResearcherId TEXT NOT NULL,
                LegacyStimulusId TEXT,
                Title TEXT NOT NULL,
                BodyText TEXT NOT NULL DEFAULT '',
                ContentType TEXT NOT NULL DEFAULT 'Text',
                Platform TEXT NOT NULL DEFAULT 'Generic',
                SourceName TEXT,
                AuthorName TEXT,
                OriginalUrl TEXT,
                PublishedAtUtc TEXT,
                CapturedAtUtc TEXT NOT NULL,
                MediaPath TEXT,
                ThumbnailPath TEXT,
                PublishedMediaUrl TEXT,
                SourceMetadataJson TEXT,
                Category TEXT,
                Topic TEXT,
                Tags TEXT,
                ResearcherNotes TEXT,
                AcquisitionProvider TEXT NOT NULL DEFAULT 'Manual',
                AcquisitionStatus TEXT NOT NULL DEFAULT 'Manual',
                IsDemo INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (ResearcherId)
                    REFERENCES Researchers(Id) ON DELETE CASCADE,
                FOREIGN KEY (LegacyStimulusId)
                    REFERENCES Stimuli(Id) ON DELETE SET NULL
            );
            """;
        await ExecuteAsync(connection, sql);
    }


    private static async Task CreateEngagementObservationsTableAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS EngagementObservations
            (
                Id TEXT PRIMARY KEY,
                ContentItemId TEXT NOT NULL,
                Likes INTEGER,
                Comments INTEGER,
                Shares INTEGER,
                Saves INTEGER,
                Views INTEGER,
                CapturedAtUtc TEXT NOT NULL,
                ObservationSource TEXT NOT NULL,
                SourceMetadataJson TEXT,
                FOREIGN KEY (ContentItemId)
                    REFERENCES ContentItems(Id) ON DELETE CASCADE
            );
            """;
        await ExecuteAsync(connection, sql);
    }


    private static async Task CreateExperimentalFeedsTableAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS ExperimentalFeeds
            (
                Id TEXT PRIMARY KEY,
                StudyId TEXT NOT NULL,
                GroupId TEXT,
                ConditionId TEXT,
                Name TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,
                PresentationJson TEXT,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (StudyId)
                    REFERENCES Studies(Id) ON DELETE CASCADE,
                FOREIGN KEY (GroupId)
                    REFERENCES Groups(Id) ON DELETE SET NULL,
                FOREIGN KEY (ConditionId)
                    REFERENCES ExperimentalConditions(Id) ON DELETE SET NULL
            );
            """;
        await ExecuteAsync(connection, sql);
    }


    private static async Task CreateExperimentalFeedItemsTableAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS ExperimentalFeedItems
            (
                Id TEXT PRIMARY KEY,
                FeedId TEXT NOT NULL,
                ContentItemId TEXT NOT NULL,
                LegacyStimulusId TEXT,
                EngagementObservationId TEXT,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,
                ItemManipulationJson TEXT,
                PresentationJson TEXT,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (FeedId)
                    REFERENCES ExperimentalFeeds(Id) ON DELETE CASCADE,
                FOREIGN KEY (ContentItemId)
                    REFERENCES ContentItems(Id) ON DELETE RESTRICT,
                FOREIGN KEY (LegacyStimulusId)
                    REFERENCES Stimuli(Id) ON DELETE SET NULL,
                FOREIGN KEY (EngagementObservationId)
                    REFERENCES EngagementObservations(Id) ON DELETE SET NULL
            );
            """;
        await ExecuteAsync(connection, sql);
    }


    private static async Task CreateManagedMediaAssetsTableAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS ManagedMediaAssets
            (
                Id TEXT PRIMARY KEY,
                ResearcherId TEXT NOT NULL,
                ContentItemId TEXT,
                MediaKind TEXT NOT NULL,
                OriginalFileName TEXT NOT NULL,
                RelativePath TEXT NOT NULL,
                MimeType TEXT,
                ByteLength INTEGER NOT NULL,
                Sha256 TEXT NOT NULL,
                MetadataJson TEXT,
                IsDemo INTEGER NOT NULL DEFAULT 0,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (ResearcherId)
                    REFERENCES Researchers(Id) ON DELETE CASCADE,
                FOREIGN KEY (ContentItemId)
                    REFERENCES ContentItems(Id) ON DELETE SET NULL
            );
            """;
        await ExecuteAsync(connection, sql);
    }


    private static async Task CreateDemoInstallationsTableAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS DemoInstallations
            (
                ResearcherId TEXT PRIMARY KEY,
                DemoStudyId TEXT NOT NULL,
                DemoVersion INTEGER NOT NULL,
                ContentManifestJson TEXT NOT NULL,
                InstalledAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (ResearcherId)
                    REFERENCES Researchers(Id) ON DELETE CASCADE,
                FOREIGN KEY (DemoStudyId)
                    REFERENCES Studies(Id) ON DELETE CASCADE
            );
            """;
        await ExecuteAsync(connection, sql);
    }


    // =========================================================
    // EXPERIMENT BLOCKS
    // =========================================================

    private static async Task CreateExperimentBlocksTableAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS ExperimentBlocks
            (
                Id TEXT PRIMARY KEY,

                StudyId TEXT NOT NULL,

                Name TEXT NOT NULL,

                BlockType TEXT NOT NULL,

                SortOrder INTEGER NOT NULL
                    DEFAULT 0,

                DurationMilliseconds INTEGER,

                ConfigurationJson TEXT,

                CreatedAtUtc TEXT NOT NULL,

                FOREIGN KEY (StudyId)
                    REFERENCES Studies(Id)
                    ON DELETE CASCADE
            );
            """;

        await ExecuteAsync(
            connection,
            sql);
    }

    private static async Task CreateResearchDataClassificationsTableAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS ResearchDataClassifications
            (
                EntityType TEXT NOT NULL,
                EntityId TEXT NOT NULL,
                StudyId TEXT,
                IsDemo INTEGER NOT NULL DEFAULT 0,
                IsSynthetic INTEGER NOT NULL DEFAULT 0,
                ClassificationSource TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                PRIMARY KEY (EntityType, EntityId)
            );
            """;
        await ExecuteAsync(connection, sql);
    }


    // =========================================================
    // EVENTS
    //
    // Raw behavioural event log.
    // =========================================================

    private static async Task CreateEventsTableAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS Events
            (
                Id TEXT PRIMARY KEY,

                StudyId TEXT NOT NULL,

                SessionId TEXT NOT NULL,

                ParticipantId TEXT NOT NULL,

                GroupId TEXT,

                ExperimentBlockId TEXT,

                StimulusId TEXT,

                EventType TEXT NOT NULL,

                TimestampUtc TEXT NOT NULL,

                ElapsedMilliseconds INTEGER,

                StimulusElapsedMilliseconds INTEGER,

                DurationMilliseconds INTEGER,

                TargetElement TEXT,

                Value TEXT,

                ValueNumber REAL,

                ValueBoolean INTEGER,

                PreviousValue TEXT,

                PointerX REAL,

                PointerY REAL,

                ScrollPosition REAL,

                ScrollDepthPercent REAL,

                StimulusOrderIndex INTEGER,

                ScreenWidth INTEGER,

                ScreenHeight INTEGER,

                SyncMarker TEXT,

                SequenceNumber INTEGER NOT NULL
                    DEFAULT 0,

                MetadataJson TEXT,

                SnapshotStimulusId TEXT,

                FOREIGN KEY (StudyId)
                    REFERENCES Studies(Id)
                    ON DELETE CASCADE,

                FOREIGN KEY (SessionId)
                    REFERENCES Sessions(Id)
                    ON DELETE CASCADE,

                FOREIGN KEY (ParticipantId)
                    REFERENCES Participants(Id)
                    ON DELETE CASCADE,

                FOREIGN KEY (GroupId)
                    REFERENCES Groups(Id)
                    ON DELETE SET NULL,

                FOREIGN KEY (ExperimentBlockId)
                    REFERENCES ExperimentBlocks(Id)
                    ON DELETE SET NULL,

                FOREIGN KEY (StimulusId)
                    REFERENCES Stimuli(Id)
                    ON DELETE SET NULL
            );
            """;

        await ExecuteAsync(
            connection,
            sql);
    }


    // =========================================================
    // QUESTIONNAIRES
    // =========================================================

    private static async Task CreateQuestionnairesTableAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS Questionnaires
            (
                Id TEXT PRIMARY KEY,

                StudyId TEXT NOT NULL,

                Title TEXT NOT NULL,

                Description TEXT,

                SortOrder INTEGER NOT NULL
                    DEFAULT 0,

                CreatedAtUtc TEXT NOT NULL,

                FOREIGN KEY (StudyId)
                    REFERENCES Studies(Id)
                    ON DELETE CASCADE
            );
            """;

        await ExecuteAsync(
            connection,
            sql);
    }


    // =========================================================
    // QUESTIONS
    // =========================================================

    private static async Task CreateQuestionsTableAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS Questions
            (
                Id TEXT PRIMARY KEY,

                QuestionnaireId TEXT NOT NULL,

                VariableName TEXT NOT NULL,

                QuestionText TEXT NOT NULL,

                QuestionType TEXT NOT NULL,

                SortOrder INTEGER NOT NULL
                    DEFAULT 0,

                IsRequired INTEGER NOT NULL
                    DEFAULT 0,

                ConfigurationJson TEXT,

                FOREIGN KEY (QuestionnaireId)
                    REFERENCES Questionnaires(Id)
                    ON DELETE CASCADE
            );
            """;

        await ExecuteAsync(
            connection,
            sql);
    }


    // =========================================================
    // RESPONSES
    // =========================================================

    private static async Task CreateResponsesTableAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS Responses
            (
                Id TEXT PRIMARY KEY,

                SessionId TEXT NOT NULL,

                ParticipantId TEXT NOT NULL,

                QuestionnaireId TEXT NOT NULL,

                QuestionId TEXT NOT NULL,

                Value TEXT,

                ResponseTimeMilliseconds INTEGER,

                CreatedAtUtc TEXT NOT NULL,

                FOREIGN KEY (SessionId)
                    REFERENCES Sessions(Id)
                    ON DELETE CASCADE,

                FOREIGN KEY (ParticipantId)
                    REFERENCES Participants(Id)
                    ON DELETE CASCADE,

                FOREIGN KEY (QuestionnaireId)
                    REFERENCES Questionnaires(Id)
                    ON DELETE CASCADE,

                FOREIGN KEY (QuestionId)
                    REFERENCES Questions(Id)
                    ON DELETE CASCADE
            );
            """;

        await ExecuteAsync(
            connection,
            sql);
    }


    // =========================================================
    // SCIENTIFIC QUESTIONNAIRE CORE (V9)
    // =========================================================

    private static async Task CreateScientificQuestionnaireTablesAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS QuestionnaireVersions
            (
                Id TEXT PRIMARY KEY,
                QuestionnaireId TEXT NOT NULL,
                VersionNumber INTEGER NOT NULL,
                VersionLabel TEXT,
                Status TEXT NOT NULL DEFAULT 'Draft',
                Language TEXT NOT NULL DEFAULT 'en',
                InstrumentType TEXT NOT NULL DEFAULT 'CUSTOM',
                Construct TEXT,
                Citation TEXT,
                LicenseStatus TEXT NOT NULL DEFAULT 'CUSTOM',
                LicenseReference TEXT,
                RedistributionStatus TEXT NOT NULL DEFAULT 'USER_PROVIDED',
                ValidationNotes TEXT,
                TranslationNotes TEXT,
                ScoringAvailability TEXT,
                SchemaHash TEXT,
                IsImmutable INTEGER NOT NULL DEFAULT 0,
                CreatedAtUtc TEXT NOT NULL,
                PublishedAtUtc TEXT,
                FOREIGN KEY (QuestionnaireId) REFERENCES Questionnaires(Id) ON DELETE CASCADE,
                UNIQUE (QuestionnaireId, VersionNumber)
            );

            CREATE TABLE IF NOT EXISTS QuestionnaireSections
            (
                Id TEXT PRIMARY KEY,
                QuestionnaireVersionId TEXT NOT NULL,
                Title TEXT NOT NULL,
                Description TEXT,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (QuestionnaireVersionId) REFERENCES QuestionnaireVersions(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS QuestionnaireItems
            (
                Id TEXT PRIMARY KEY,
                QuestionnaireVersionId TEXT NOT NULL,
                SectionId TEXT,
                VariableName TEXT NOT NULL,
                QuestionText TEXT NOT NULL,
                QuestionType TEXT NOT NULL,
                MeasurementLevel TEXT NOT NULL DEFAULT 'ORDINAL',
                IsRequired INTEGER NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                ConfigurationJson TEXT,
                FOREIGN KEY (QuestionnaireVersionId) REFERENCES QuestionnaireVersions(Id) ON DELETE CASCADE,
                FOREIGN KEY (SectionId) REFERENCES QuestionnaireSections(Id) ON DELETE SET NULL,
                UNIQUE (QuestionnaireVersionId, VariableName)
            );

            CREATE TABLE IF NOT EXISTS QuestionOptions
            (
                Id TEXT PRIMARY KEY,
                QuestionId TEXT NOT NULL,
                ValueCode TEXT NOT NULL,
                NumericCode REAL,
                DisplayLabel TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (QuestionId) REFERENCES QuestionnaireItems(Id) ON DELETE CASCADE,
                UNIQUE (QuestionId, ValueCode)
            );

            CREATE TABLE IF NOT EXISTS QuestionnaireAssignments
            (
                Id TEXT PRIMARY KEY,
                StudyId TEXT NOT NULL,
                QuestionnaireVersionId TEXT NOT NULL,
                Placement TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsRequired INTEGER NOT NULL DEFAULT 1,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (StudyId) REFERENCES Studies(Id) ON DELETE CASCADE,
                FOREIGN KEY (QuestionnaireVersionId) REFERENCES QuestionnaireVersions(Id) ON DELETE RESTRICT,
                UNIQUE (StudyId, QuestionnaireVersionId, Placement)
            );

            CREATE TABLE IF NOT EXISTS QuestionnaireResponseSets
            (
                Id TEXT PRIMARY KEY,
                AssignmentId TEXT NOT NULL,
                StudyId TEXT NOT NULL,
                SessionId TEXT,
                ParticipantId TEXT NOT NULL,
                QuestionnaireId TEXT NOT NULL,
                QuestionnaireVersionId TEXT NOT NULL,
                StartedAtUtc TEXT NOT NULL,
                CompletedAtUtc TEXT,
                DurationMilliseconds INTEGER,
                Status TEXT NOT NULL DEFAULT 'Started',
                IsDemo INTEGER NOT NULL DEFAULT 0,
                MetadataJson TEXT,
                FOREIGN KEY (AssignmentId) REFERENCES QuestionnaireAssignments(Id) ON DELETE RESTRICT,
                FOREIGN KEY (StudyId) REFERENCES Studies(Id) ON DELETE CASCADE,
                FOREIGN KEY (SessionId) REFERENCES Sessions(Id) ON DELETE SET NULL,
                FOREIGN KEY (ParticipantId) REFERENCES Participants(Id) ON DELETE CASCADE,
                FOREIGN KEY (QuestionnaireId) REFERENCES Questionnaires(Id) ON DELETE RESTRICT,
                FOREIGN KEY (QuestionnaireVersionId) REFERENCES QuestionnaireVersions(Id) ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS QuestionResponseValues
            (
                Id TEXT PRIMARY KEY,
                ResponseSetId TEXT NOT NULL,
                QuestionId TEXT NOT NULL,
                RawValue TEXT,
                NumericValue REAL,
                SelectedOptionIdsJson TEXT,
                RespondedAtUtc TEXT NOT NULL,
                FOREIGN KEY (ResponseSetId) REFERENCES QuestionnaireResponseSets(Id) ON DELETE CASCADE,
                FOREIGN KEY (QuestionId) REFERENCES QuestionnaireItems(Id) ON DELETE RESTRICT,
                UNIQUE (ResponseSetId, QuestionId)
            );

            CREATE TABLE IF NOT EXISTS QuestionnaireScales
            (
                Id TEXT PRIMARY KEY,
                QuestionnaireVersionId TEXT NOT NULL,
                Name TEXT NOT NULL,
                VariableName TEXT NOT NULL,
                ScoringMethod TEXT NOT NULL DEFAULT 'MEAN',
                MissingItemRule TEXT NOT NULL DEFAULT 'REQUIRE_MINIMUM',
                MinimumAnsweredItems INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (QuestionnaireVersionId) REFERENCES QuestionnaireVersions(Id) ON DELETE CASCADE,
                UNIQUE (QuestionnaireVersionId, VariableName)
            );

            CREATE TABLE IF NOT EXISTS QuestionnaireScaleItems
            (
                Id TEXT PRIMARY KEY,
                ScaleId TEXT NOT NULL,
                QuestionId TEXT NOT NULL,
                IsReverseCoded INTEGER NOT NULL DEFAULT 0,
                ReverseMinimum REAL,
                ReverseMaximum REAL,
                Weight REAL NOT NULL DEFAULT 1,
                FOREIGN KEY (ScaleId) REFERENCES QuestionnaireScales(Id) ON DELETE CASCADE,
                FOREIGN KEY (QuestionId) REFERENCES QuestionnaireItems(Id) ON DELETE RESTRICT,
                UNIQUE (ScaleId, QuestionId)
            );
            """;

        await ExecuteAsync(connection, sql);
    }


    private static async Task CreateScientificAnalysisTablesAsync(
        SqliteConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS AnalysisSpecifications
            (
                Id TEXT PRIMARY KEY,
                StudyId TEXT NOT NULL,
                Name TEXT NOT NULL,
                ResearchQuestion TEXT,
                Classification TEXT NOT NULL DEFAULT 'EXPLORATORY',
                OutcomeVariableId TEXT NOT NULL,
                PredictorVariableId TEXT,
                CovariatesJson TEXT,
                AnalysisFamily TEXT NOT NULL,
                Method TEXT NOT NULL,
                AlternativeHypothesis TEXT NOT NULL DEFAULT 'TWO_SIDED',
                ConfidenceLevel REAL NOT NULL DEFAULT 0.95,
                MissingDataHandling TEXT NOT NULL DEFAULT 'COMPLETE_CASE',
                MultipleComparisonMethod TEXT NOT NULL DEFAULT 'NONE',
                ParametersJson TEXT,
                EngineVersion TEXT NOT NULL,
                IsDemo INTEGER NOT NULL DEFAULT 0,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (StudyId) REFERENCES Studies(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS AnalysisExecutions
            (
                Id TEXT PRIMARY KEY,
                AnalysisSpecificationId TEXT NOT NULL,
                StudyId TEXT NOT NULL,
                Status TEXT NOT NULL,
                DatasetHash TEXT NOT NULL,
                DatasetDescriptorJson TEXT NOT NULL,
                ResultJson TEXT,
                DiagnosticsJson TEXT,
                WarningJson TEXT,
                ErrorCode TEXT,
                ErrorDetail TEXT,
                EngineVersion TEXT NOT NULL,
                ExecutedAtUtc TEXT NOT NULL,
                IsDemo INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (AnalysisSpecificationId) REFERENCES AnalysisSpecifications(Id) ON DELETE CASCADE,
                FOREIGN KEY (StudyId) REFERENCES Studies(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS AnalysisExclusions
            (
                Id TEXT PRIMARY KEY,
                AnalysisExecutionId TEXT NOT NULL,
                ParticipantId TEXT,
                SessionId TEXT,
                ReasonCode TEXT NOT NULL,
                ReasonDetail TEXT,
                FOREIGN KEY (AnalysisExecutionId) REFERENCES AnalysisExecutions(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS StudyTransparencyMetadata
            (
                StudyId TEXT PRIMARY KEY,
                RegistrationStatus TEXT NOT NULL DEFAULT 'NOT_SPECIFIED',
                RegistrationReference TEXT,
                ProtocolAvailability TEXT,
                AnalysisPlanAvailability TEXT,
                MaterialsTransparency TEXT,
                DataTransparency TEXT,
                ComputationTransparency TEXT,
                ReportingTransparency TEXT,
                MetadataJson TEXT,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (StudyId) REFERENCES Studies(Id) ON DELETE CASCADE
            );
            """;

        await ExecuteAsync(connection, sql);
    }


    // =========================================================
    // MIGRATION - STUDIES V2
    // =========================================================

    private static async Task MigrateStudiesToVersion2Async(
        SqliteConnection connection)
    {
        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "StudyType",
            "TEXT NOT NULL DEFAULT 'Experimental'");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "DesignType",
            "TEXT NOT NULL DEFAULT 'BetweenSubjects'");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "AssignmentMethod",
            "TEXT NOT NULL DEFAULT 'Manual'");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "RandomizeStimuli",
            "INTEGER NOT NULL DEFAULT 0");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "RandomizationSeed",
            "INTEGER");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "UsesStimuli",
            "INTEGER NOT NULL DEFAULT 1");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "UsesQuestionnaires",
            "INTEGER NOT NULL DEFAULT 0");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "UsesPhysiologicalData",
            "INTEGER NOT NULL DEFAULT 0");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "EegEnabled",
            "INTEGER NOT NULL DEFAULT 0");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "GsrEnabled",
            "INTEGER NOT NULL DEFAULT 0");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "TargetSampleSize",
            "INTEGER");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "ExpectedSessionDurationMinutes",
            "INTEGER");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "AllowSessionResume",
            "INTEGER NOT NULL DEFAULT 1");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "RequireParticipantConsent",
            "INTEGER NOT NULL DEFAULT 1");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "ConsentText",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "ResearchQuestion",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "Hypothesis",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "PopulationDescription",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "InclusionCriteria",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "ExclusionCriteria",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "MetadataJson",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "StartedAtUtc",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Studies",
            "CompletedAtUtc",
            "TEXT");
    }


    // =========================================================
    // MIGRATION - GROUPS V2
    // =========================================================

    private static async Task MigrateGroupsToVersion2Async(
        SqliteConnection connection)
    {
        await AddColumnIfMissingAsync(
            connection,
            "Groups",
            "IsControlGroup",
            "INTEGER NOT NULL DEFAULT 0");

        await AddColumnIfMissingAsync(
            connection,
            "Groups",
            "IsActive",
            "INTEGER NOT NULL DEFAULT 1");

        await AddColumnIfMissingAsync(
            connection,
            "Groups",
            "UpdatedAtUtc",
            "TEXT");


        await ExecuteAsync(
            connection,
            """
            UPDATE Groups
            SET UpdatedAtUtc = CreatedAtUtc
            WHERE UpdatedAtUtc IS NULL;
            """);
    }


    // =========================================================
    // MIGRATION - PARTICIPANTS V2
    // =========================================================

    private static async Task MigrateParticipantsToVersion2Async(
        SqliteConnection connection)
    {
        await AddColumnIfMissingAsync(
            connection,
            "Participants",
            "Age",
            "INTEGER");

        await AddColumnIfMissingAsync(
            connection,
            "Participants",
            "Gender",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Participants",
            "EducationLevel",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Participants",
            "Occupation",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Participants",
            "IsEligible",
            "INTEGER NOT NULL DEFAULT 1");

        await AddColumnIfMissingAsync(
            connection,
            "Participants",
            "EligibilityNotes",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Participants",
            "ConsentAccepted",
            "INTEGER NOT NULL DEFAULT 0");

        await AddColumnIfMissingAsync(
            connection,
            "Participants",
            "ConsentAcceptedAtUtc",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Participants",
            "HasStartedStudy",
            "INTEGER NOT NULL DEFAULT 0");

        await AddColumnIfMissingAsync(
            connection,
            "Participants",
            "HasCompletedStudy",
            "INTEGER NOT NULL DEFAULT 0");

        await AddColumnIfMissingAsync(
            connection,
            "Participants",
            "StudyStartedAtUtc",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Participants",
            "StudyCompletedAtUtc",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Participants",
            "IsExcluded",
            "INTEGER NOT NULL DEFAULT 0");

        await AddColumnIfMissingAsync(
            connection,
            "Participants",
            "ExclusionReason",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Participants",
            "HasWithdrawn",
            "INTEGER NOT NULL DEFAULT 0");

        await AddColumnIfMissingAsync(
            connection,
            "Participants",
            "WithdrawalReason",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Participants",
            "ResearcherNotes",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Participants",
            "MetadataJson",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Participants",
            "UpdatedAtUtc",
            "TEXT");


        await ExecuteAsync(
            connection,
            """
            UPDATE Participants
            SET UpdatedAtUtc = CreatedAtUtc
            WHERE UpdatedAtUtc IS NULL;
            """);
    }


    // =========================================================
    // MIGRATION - SESSIONS V2
    // =========================================================

    private static async Task MigrateSessionsToVersion2Async(
        SqliteConnection connection)
    {
        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "GroupId",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "CreatedAtUtc",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "DurationMilliseconds",
            "INTEGER");

        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "CurrentStimulusIndex",
            "INTEGER NOT NULL DEFAULT 0");

        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "CompletedStimulusCount",
            "INTEGER NOT NULL DEFAULT 0");

        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "DeviceName",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "OperatingSystem",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "ScreenWidth",
            "INTEGER");

        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "ScreenHeight",
            "INTEGER");

        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "EegEnabled",
            "INTEGER NOT NULL DEFAULT 0");

        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "GsrEnabled",
            "INTEGER NOT NULL DEFAULT 0");

        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "EegDeviceId",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "GsrDeviceId",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "SynchronizationSessionId",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "WasInterrupted",
            "INTEGER NOT NULL DEFAULT 0");

        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "InterruptionReason",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "ResearcherNotes",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "MetadataJson",
            "TEXT");


        await ExecuteAsync(
            connection,
            """
            UPDATE Sessions
            SET CreatedAtUtc = StartedAtUtc
            WHERE CreatedAtUtc IS NULL;
            """);
    }


    // =========================================================
    // MIGRATION - SESSIONS V4
    // =========================================================

    private static async Task MigrateSessionsToVersion4Async(
        SqliteConnection connection)
    {
        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "ConditionId",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "ConfigurationSnapshotId",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "ActualStartedAtUtc",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "LifecycleVersion",
            "INTEGER NOT NULL DEFAULT 1");

        await AddColumnIfMissingAsync(
            connection,
            "Sessions",
            "LifecycleUpdatedAtUtc",
            "TEXT");

        await ExecuteAsync(
            connection,
            """
            UPDATE Sessions
            SET LifecycleUpdatedAtUtc = COALESCE(
                    CompletedAtUtc,
                    StartedAtUtc,
                    CreatedAtUtc)
            WHERE LifecycleUpdatedAtUtc IS NULL;
            """);
    }


    // =========================================================
    // MIGRATION - IMMUTABLE SNAPSHOTS V5
    // =========================================================

    private static async Task MigrateSnapshotsToVersion5Async(
        SqliteConnection connection)
    {
        await AddColumnIfMissingAsync(
            connection,
            "ExperimentConfigurationSnapshots",
            "IntegrityHash",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "ExperimentConfigurationSnapshots",
            "IntegrityHashAlgorithm",
            "TEXT");
    }


    private static async Task MigrateEventsToVersion5Async(
        SqliteConnection connection)
    {
        await AddColumnIfMissingAsync(
            connection,
            "Events",
            "SnapshotStimulusId",
            "TEXT");
    }


    // =========================================================
    // MIGRATION - STIMULI V2
    // =========================================================

    private static async Task MigrateStimuliToVersion2Async(
        SqliteConnection connection)
    {
        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "GroupId",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "Platform",
            "TEXT NOT NULL DEFAULT 'Generic'");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "SourceName",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "AuthorName",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "OriginalUrl",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "PublishedAtUtc",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "ThumbnailPath",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "Category",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "Topic",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "ConditionLabel",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "ExperimentalTag",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "OriginalLikes",
            "INTEGER");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "OriginalComments",
            "INTEGER");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "OriginalShares",
            "INTEGER");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "OriginalSaves",
            "INTEGER");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "OriginalViews",
            "INTEGER");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "SortOrder",
            "INTEGER NOT NULL DEFAULT 0");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "IsActive",
            "INTEGER NOT NULL DEFAULT 1");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "MinimumExposureMilliseconds",
            "INTEGER NOT NULL DEFAULT 0");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "MaximumExposureMilliseconds",
            "INTEGER");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "AllowRandomization",
            "INTEGER NOT NULL DEFAULT 1");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "ResearcherNotes",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Stimuli",
            "UpdatedAtUtc",
            "TEXT");


        await ExecuteAsync(
            connection,
            """
            UPDATE Stimuli
            SET UpdatedAtUtc = CreatedAtUtc
            WHERE UpdatedAtUtc IS NULL;
            """);
    }


    // =========================================================
    // MIGRATION - EVENTS V2
    // =========================================================

    private static async Task MigrateEventsToVersion2Async(
        SqliteConnection connection)
    {
        await AddColumnIfMissingAsync(
            connection,
            "Events",
            "GroupId",
            "TEXT");

        await AddColumnIfMissingAsync(
            connection,
            "Events",
            "StimulusElapsedMilliseconds",
            "INTEGER");

        await AddColumnIfMissingAsync(
            connection,
            "Events",
            "ValueNumber",
            "REAL");

        await AddColumnIfMissingAsync(
            connection,
            "Events",
            "ValueBoolean",
            "INTEGER");

        await AddColumnIfMissingAsync(
            connection,
            "Events",
            "PointerX",
            "REAL");

        await AddColumnIfMissingAsync(
            connection,
            "Events",
            "PointerY",
            "REAL");

        await AddColumnIfMissingAsync(
            connection,
            "Events",
            "ScrollPosition",
            "REAL");

        await AddColumnIfMissingAsync(
            connection,
            "Events",
            "ScrollDepthPercent",
            "REAL");

        await AddColumnIfMissingAsync(
            connection,
            "Events",
            "StimulusOrderIndex",
            "INTEGER");

        await AddColumnIfMissingAsync(
            connection,
            "Events",
            "ScreenWidth",
            "INTEGER");

        await AddColumnIfMissingAsync(
            connection,
            "Events",
            "ScreenHeight",
            "INTEGER");

        await AddColumnIfMissingAsync(
            connection,
            "Events",
            "SyncMarker",
            "TEXT");
    }


    // =========================================================
    // MIGRATION - CONTENT LIBRARY / EXPERIMENT FEEDS V6
    //
    // Existing Stimuli remain untouched. The compatibility rows use
    // stable identifiers and INSERT OR IGNORE so this migration is safe
    // to run repeatedly and never overwrites a later library edit.
    // =========================================================

    private static async Task MigrateContentArchitectureToVersion6Async(
        SqliteConnection connection)
    {
        await using var transaction =
            await connection.BeginTransactionAsync();

        try
        {
            await ExecuteAsync(
                connection,
                """
                INSERT OR IGNORE INTO ContentItems
                (
                    Id, ResearcherId, LegacyStimulusId, Title, BodyText,
                    ContentType, Platform, SourceName, AuthorName, OriginalUrl,
                    PublishedAtUtc, CapturedAtUtc, MediaPath, ThumbnailPath,
                    SourceMetadataJson, Category, Topic, Tags, ResearcherNotes,
                    AcquisitionProvider, AcquisitionStatus, IsActive,
                    CreatedAtUtc, UpdatedAtUtc
                )
                SELECT
                    s.Id, st.ResearcherId, s.Id, s.Name,
                    COALESCE(s.ContentText, ''), s.StimulusType,
                    COALESCE(s.Platform, 'Generic'), s.SourceName,
                    s.AuthorName, s.OriginalUrl, s.PublishedAtUtc,
                    COALESCE(s.CreatedAtUtc, CURRENT_TIMESTAMP),
                    s.SourcePath, s.ThumbnailPath, s.MetadataJson,
                    s.Category, s.Topic, s.ExperimentalTag,
                    s.ResearcherNotes, 'LegacyStimulus', 'Migrated',
                    s.IsActive, s.CreatedAtUtc,
                    COALESCE(s.UpdatedAtUtc, s.CreatedAtUtc)
                FROM Stimuli s
                JOIN Studies st ON st.Id = s.StudyId;
                """);

            await ExecuteAsync(
                connection,
                """
                INSERT OR IGNORE INTO EngagementObservations
                (
                    Id, ContentItemId, Likes, Comments, Shares, Saves, Views,
                    CapturedAtUtc, ObservationSource, SourceMetadataJson
                )
                SELECT
                    'legacy-observation-' || s.Id, s.Id,
                    s.OriginalLikes, s.OriginalComments, s.OriginalShares,
                    s.OriginalSaves, s.OriginalViews,
                    COALESCE(s.CreatedAtUtc, CURRENT_TIMESTAMP),
                    'LegacyStimulus', s.MetadataJson
                FROM Stimuli s
                JOIN ContentItems c ON c.Id = s.Id;
                """);

            await ExecuteAsync(
                connection,
                """
                INSERT OR IGNORE INTO ExperimentalFeeds
                (
                    Id, StudyId, GroupId, ConditionId, Name, SortOrder,
                    IsActive, PresentationJson, CreatedAtUtc, UpdatedAtUtc
                )
                SELECT DISTINCT
                    'legacy-feed-' || s.StudyId || '-' ||
                        COALESCE(s.GroupId, 'all'),
                    s.StudyId, s.GroupId, NULL,
                    CASE WHEN s.GroupId IS NULL
                        THEN 'All participants'
                        ELSE COALESCE(g.Name, 'Study group')
                    END,
                    0, 1, NULL,
                    MIN(s.CreatedAtUtc),
                    MAX(COALESCE(s.UpdatedAtUtc, s.CreatedAtUtc))
                FROM Stimuli s
                LEFT JOIN Groups g ON g.Id = s.GroupId
                GROUP BY s.StudyId, s.GroupId;
                """);

            await ExecuteAsync(
                connection,
                """
                INSERT OR IGNORE INTO ExperimentalFeedItems
                (
                    Id, FeedId, ContentItemId, LegacyStimulusId,
                    SortOrder, IsActive, ItemManipulationJson,
                    PresentationJson, CreatedAtUtc, UpdatedAtUtc
                )
                SELECT
                    'legacy-feed-item-' || s.Id,
                    'legacy-feed-' || s.StudyId || '-' ||
                        COALESCE(s.GroupId, 'all'),
                    s.Id, s.Id, s.SortOrder, s.IsActive,
                    NULL, NULL, s.CreatedAtUtc,
                    COALESCE(s.UpdatedAtUtc, s.CreatedAtUtc)
                FROM Stimuli s
                JOIN ContentItems c ON c.Id = s.Id;
                """);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }


    private static async Task MigrateProductExperienceToVersion7Async(
        SqliteConnection connection)
    {
        await AddColumnIfMissingAsync(
            connection,
            "ContentItems",
            "IsDemo",
            "INTEGER NOT NULL DEFAULT 0");

        await AddColumnIfMissingAsync(
            connection,
            "ExperimentalFeedItems",
            "EngagementObservationId",
            "TEXT");

        await ExecuteAsync(
            connection,
            """
            UPDATE ExperimentalFeedItems
            SET EngagementObservationId =
            (
                SELECT eo.Id
                FROM EngagementObservations eo
                WHERE eo.ContentItemId = ExperimentalFeedItems.ContentItemId
                ORDER BY eo.CapturedAtUtc DESC, eo.Id DESC
                LIMIT 1
            )
            WHERE EngagementObservationId IS NULL;
            """);
    }


    private static async Task MigrateScientificCoreToVersion9Async(
        SqliteConnection connection)
    {
        await AddColumnIfMissingAsync(connection, "Questionnaires", "UpdatedAtUtc", "TEXT");
        await AddColumnIfMissingAsync(connection, "Questionnaires", "IsActive", "INTEGER NOT NULL DEFAULT 1");
        await AddColumnIfMissingAsync(connection, "Questionnaires", "CurrentVersionId", "TEXT");
        await AddColumnIfMissingAsync(connection, "Questionnaires", "InstrumentType", "TEXT NOT NULL DEFAULT 'CUSTOM'");
        await AddColumnIfMissingAsync(connection, "Questionnaires", "MetadataJson", "TEXT");

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Questionnaires
            SET UpdatedAtUtc = COALESCE(UpdatedAtUtc, CreatedAtUtc)
            WHERE UpdatedAtUtc IS NULL;
            """;
        await command.ExecuteNonQueryAsync();
    }


    // =========================================================
    // MIGRATION - PARTICIPANT-ACCESSIBLE MEDIA URLS V10
    // =========================================================

    private static async Task MigratePublishedMediaUrlsToVersion10Async(
        SqliteConnection connection)
    {
        await AddColumnIfMissingAsync(connection, "Stimuli", "PublishedMediaUrl", "TEXT");
        await AddColumnIfMissingAsync(connection, "ContentItems", "PublishedMediaUrl", "TEXT");
        await ExecuteAsync(connection,
            """
            UPDATE ContentItems
            SET PublishedMediaUrl =
            (
                SELECT s.PublishedMediaUrl
                FROM Stimuli s
                WHERE s.Id = ContentItems.LegacyStimulusId
            )
            WHERE PublishedMediaUrl IS NULL
              AND LegacyStimulusId IS NOT NULL;
            """);
    }


    // =========================================================
    // CREATE INDEXES
    //
    // THIS MUST RUN AFTER MIGRATIONS.
    // =========================================================

    private static async Task CreateIndexesAsync(
        SqliteConnection connection)
    {
        const string sql = """
            -- =================================================
            -- STUDIES
            -- =================================================

            CREATE INDEX IF NOT EXISTS
                IX_Studies_ResearcherId
            ON Studies(ResearcherId);

            CREATE INDEX IF NOT EXISTS
                IX_Studies_Status
            ON Studies(Status);


            -- =================================================
            -- GROUPS
            -- =================================================

            CREATE INDEX IF NOT EXISTS
                IX_Groups_StudyId
            ON Groups(StudyId);


            -- =================================================
            -- EXPERIMENTAL CONDITIONS
            -- =================================================

            CREATE INDEX IF NOT EXISTS
                IX_ExperimentalConditions_StudyId
            ON ExperimentalConditions(StudyId);

            CREATE INDEX IF NOT EXISTS
                IX_ExperimentalConditions_GroupId
            ON ExperimentalConditions(GroupId);

            CREATE INDEX IF NOT EXISTS
                IX_ExperimentalConditions_Study_SortOrder
            ON ExperimentalConditions
            (
                StudyId,
                SortOrder
            );


            -- =================================================
            -- PARTICIPANTS
            -- =================================================

            CREATE UNIQUE INDEX IF NOT EXISTS
                UX_Participants_Study_Code
            ON Participants
            (
                StudyId,
                ParticipantCode
            );

            CREATE INDEX IF NOT EXISTS
                IX_Participants_GroupId
            ON Participants(GroupId);

            CREATE INDEX IF NOT EXISTS
                IX_Participants_Status
            ON Participants(Status);


            -- =================================================
            -- ASSIGNMENTS
            -- =================================================

            CREATE INDEX IF NOT EXISTS
                IX_ParticipantAssignments_StudyId
            ON ParticipantAssignments(StudyId);

            CREATE INDEX IF NOT EXISTS
                IX_ParticipantAssignments_ParticipantId
            ON ParticipantAssignments(ParticipantId);

            CREATE INDEX IF NOT EXISTS
                IX_ParticipantAssignments_GroupId
            ON ParticipantAssignments(GroupId);

            CREATE INDEX IF NOT EXISTS
                IX_ParticipantConditionAssignments_StudyId
            ON ParticipantConditionAssignments(StudyId);

            CREATE INDEX IF NOT EXISTS
                IX_ParticipantConditionAssignments_ParticipantId
            ON ParticipantConditionAssignments(ParticipantId);

            CREATE INDEX IF NOT EXISTS
                IX_ParticipantConditionAssignments_ConditionId
            ON ParticipantConditionAssignments(ConditionId);

            CREATE UNIQUE INDEX IF NOT EXISTS
                UX_ParticipantConditionAssignments_ActiveParticipant
            ON ParticipantConditionAssignments(ParticipantId)
            WHERE IsActive = 1;


            -- =================================================
            -- SESSIONS
            -- =================================================

            CREATE INDEX IF NOT EXISTS
                IX_Sessions_ParticipantId
            ON Sessions(ParticipantId);

            CREATE INDEX IF NOT EXISTS
                IX_Sessions_StudyId
            ON Sessions(StudyId);

            CREATE INDEX IF NOT EXISTS
                IX_Sessions_GroupId
            ON Sessions(GroupId);

            CREATE INDEX IF NOT EXISTS
                IX_Sessions_Status
            ON Sessions(Status);

            CREATE INDEX IF NOT EXISTS
                IX_Sessions_ConditionId
            ON Sessions(ConditionId);

            CREATE INDEX IF NOT EXISTS
                IX_Sessions_ConfigurationSnapshotId
            ON Sessions(ConfigurationSnapshotId);


            -- =================================================
            -- CONFIGURATION SNAPSHOTS
            -- =================================================

            CREATE INDEX IF NOT EXISTS
                IX_ExperimentSnapshots_StudyId
            ON ExperimentConfigurationSnapshots(StudyId);

            CREATE INDEX IF NOT EXISTS
                IX_ExperimentSnapshots_ParticipantId
            ON ExperimentConfigurationSnapshots(ParticipantId);

            CREATE INDEX IF NOT EXISTS
                IX_ExperimentSnapshots_ConditionId
            ON ExperimentConfigurationSnapshots(ConditionId);


            -- =================================================
            -- STIMULI
            -- =================================================

            CREATE INDEX IF NOT EXISTS
                IX_Stimuli_StudyId
            ON Stimuli(StudyId);

            CREATE INDEX IF NOT EXISTS
                IX_Stimuli_GroupId
            ON Stimuli(GroupId);

            CREATE INDEX IF NOT EXISTS
                IX_Stimuli_SortOrder
            ON Stimuli
            (
                StudyId,
                SortOrder
            );


            -- =================================================
            -- CONTENT LIBRARY / EXPERIMENT FEEDS
            -- =================================================

            CREATE INDEX IF NOT EXISTS
                IX_ContentItems_ResearcherId
            ON ContentItems(ResearcherId);

            CREATE UNIQUE INDEX IF NOT EXISTS
                UX_ContentItems_LegacyStimulusId
            ON ContentItems(LegacyStimulusId)
            WHERE LegacyStimulusId IS NOT NULL;

            CREATE INDEX IF NOT EXISTS
                IX_ContentItems_CapturedAtUtc
            ON ContentItems(CapturedAtUtc);

            CREATE INDEX IF NOT EXISTS
                IX_ContentItems_Researcher_IsDemo
            ON ContentItems(ResearcherId, IsDemo);

            CREATE INDEX IF NOT EXISTS
                IX_EngagementObservations_Content_Captured
            ON EngagementObservations(ContentItemId, CapturedAtUtc DESC);

            CREATE INDEX IF NOT EXISTS
                IX_ExperimentalFeeds_StudyId
            ON ExperimentalFeeds(StudyId);

            CREATE INDEX IF NOT EXISTS
                IX_ExperimentalFeeds_GroupId
            ON ExperimentalFeeds(GroupId);

            CREATE INDEX IF NOT EXISTS
                IX_ExperimentalFeeds_ConditionId
            ON ExperimentalFeeds(ConditionId);

            CREATE UNIQUE INDEX IF NOT EXISTS
                UX_ExperimentalFeeds_Scope
            ON ExperimentalFeeds
            (
                StudyId,
                IFNULL(GroupId, ''),
                IFNULL(ConditionId, '')
            );

            CREATE UNIQUE INDEX IF NOT EXISTS
                UX_ExperimentalFeedItems_Feed_Content
            ON ExperimentalFeedItems(FeedId, ContentItemId);

            CREATE INDEX IF NOT EXISTS
                IX_ExperimentalFeedItems_Feed_Order
            ON ExperimentalFeedItems(FeedId, SortOrder);

            CREATE INDEX IF NOT EXISTS
                IX_ExperimentalFeedItems_ContentId
            ON ExperimentalFeedItems(ContentItemId);

            CREATE INDEX IF NOT EXISTS
                IX_ExperimentalFeedItems_ObservationId
            ON ExperimentalFeedItems(EngagementObservationId);

            CREATE INDEX IF NOT EXISTS
                IX_ManagedMediaAssets_ResearcherId
            ON ManagedMediaAssets(ResearcherId);

            CREATE INDEX IF NOT EXISTS
                IX_ManagedMediaAssets_ContentItemId
            ON ManagedMediaAssets(ContentItemId);

            CREATE UNIQUE INDEX IF NOT EXISTS
                UX_ManagedMediaAssets_Researcher_Hash
            ON ManagedMediaAssets(ResearcherId, Sha256, RelativePath);

            CREATE INDEX IF NOT EXISTS
                IX_ResearchDataClassifications_Study
            ON ResearchDataClassifications(StudyId, IsDemo, IsSynthetic);


            -- =================================================
            -- EXPERIMENT BLOCKS
            -- =================================================

            CREATE INDEX IF NOT EXISTS
                IX_ExperimentBlocks_StudyId
            ON ExperimentBlocks(StudyId);


            -- =================================================
            -- EVENTS
            -- =================================================

            CREATE INDEX IF NOT EXISTS
                IX_Events_SessionId
            ON Events(SessionId);

            CREATE INDEX IF NOT EXISTS
                IX_Events_ParticipantId
            ON Events(ParticipantId);

            CREATE INDEX IF NOT EXISTS
                IX_Events_GroupId
            ON Events(GroupId);

            CREATE INDEX IF NOT EXISTS
                IX_Events_StimulusId
            ON Events(StimulusId);

            CREATE INDEX IF NOT EXISTS
                IX_Events_SnapshotStimulusId
            ON Events(SnapshotStimulusId);

            CREATE INDEX IF NOT EXISTS
                IX_Events_EventType
            ON Events(EventType);

            CREATE INDEX IF NOT EXISTS
                IX_Events_Sequence
            ON Events
            (
                SessionId,
                SequenceNumber
            );

            CREATE INDEX IF NOT EXISTS
                IX_Events_Timestamp
            ON Events(TimestampUtc);


            -- =================================================
            -- QUESTIONNAIRES
            -- =================================================

            CREATE INDEX IF NOT EXISTS
                IX_Questionnaires_StudyId
            ON Questionnaires(StudyId);


            -- =================================================
            -- QUESTIONS
            -- =================================================

            CREATE INDEX IF NOT EXISTS
                IX_Questions_QuestionnaireId
            ON Questions(QuestionnaireId);


            -- =================================================
            -- RESPONSES
            -- =================================================

            CREATE INDEX IF NOT EXISTS
                IX_Responses_SessionId
            ON Responses(SessionId);

            CREATE INDEX IF NOT EXISTS
                IX_Responses_ParticipantId
            ON Responses(ParticipantId);

            CREATE INDEX IF NOT EXISTS
                IX_Responses_QuestionId
            ON Responses(QuestionId);

            CREATE INDEX IF NOT EXISTS IX_QuestionnaireVersions_Questionnaire
            ON QuestionnaireVersions(QuestionnaireId, VersionNumber);

            CREATE INDEX IF NOT EXISTS IX_QuestionnaireSections_Version
            ON QuestionnaireSections(QuestionnaireVersionId, SortOrder);

            CREATE INDEX IF NOT EXISTS IX_QuestionnaireItems_Version
            ON QuestionnaireItems(QuestionnaireVersionId, SortOrder);

            CREATE INDEX IF NOT EXISTS IX_QuestionOptions_Question
            ON QuestionOptions(QuestionId, SortOrder);

            CREATE INDEX IF NOT EXISTS IX_QuestionnaireAssignments_Study_Placement
            ON QuestionnaireAssignments(StudyId, Placement, IsActive, SortOrder);

            CREATE INDEX IF NOT EXISTS IX_QuestionnaireResponseSets_Study
            ON QuestionnaireResponseSets(StudyId, IsDemo, Status);

            CREATE INDEX IF NOT EXISTS IX_QuestionnaireResponseSets_Session
            ON QuestionnaireResponseSets(SessionId);

            CREATE INDEX IF NOT EXISTS IX_QuestionnaireResponseSets_Participant
            ON QuestionnaireResponseSets(ParticipantId);

            CREATE INDEX IF NOT EXISTS IX_QuestionResponseValues_ResponseSet
            ON QuestionResponseValues(ResponseSetId);

            CREATE INDEX IF NOT EXISTS IX_QuestionnaireScales_Version
            ON QuestionnaireScales(QuestionnaireVersionId);

            CREATE INDEX IF NOT EXISTS IX_AnalysisSpecifications_Study
            ON AnalysisSpecifications(StudyId, IsDemo, Classification);

            CREATE INDEX IF NOT EXISTS IX_AnalysisExecutions_Specification
            ON AnalysisExecutions(AnalysisSpecificationId, ExecutedAtUtc DESC);

            CREATE INDEX IF NOT EXISTS IX_AnalysisExecutions_Study
            ON AnalysisExecutions(StudyId, IsDemo, Status);
            """;

        await ExecuteAsync(
            connection,
            sql);
    }


    // =========================================================
    // ADD COLUMN IF MISSING
    // =========================================================

    private static async Task AddColumnIfMissingAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string definition)
    {
        var exists =
            await ColumnExistsAsync(
                connection,
                tableName,
                columnName);


        if (exists)
        {
            return;
        }


        var sql =
            $"ALTER TABLE \"{tableName}\" " +
            $"ADD COLUMN \"{columnName}\" {definition};";


        await ExecuteAsync(
            connection,
            sql);
    }


    // =========================================================
    // CHECK COLUMN
    // =========================================================

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection,
        string tableName,
        string columnName)
    {
        await using var command =
            connection.CreateCommand();


        command.CommandText =
            $"PRAGMA table_info(\"{tableName}\");";


        await using var reader =
            await command.ExecuteReaderAsync();


        while (await reader.ReadAsync())
        {
            var existingColumn =
                reader.GetString(1);


            if (string.Equals(
                    existingColumn,
                    columnName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }


        return false;
    }


    // =========================================================
    // SET SCHEMA VERSION
    // =========================================================

    private static async Task SetSchemaVersionAsync(
        SqliteConnection connection,
        int version)
    {
        const string sql = """
            INSERT INTO SchemaInfo
            (
                Id,
                SchemaVersion,
                UpdatedAtUtc
            )
            VALUES
            (
                1,
                $version,
                $updatedAtUtc
            )

            ON CONFLICT(Id)
            DO UPDATE SET
                SchemaVersion = excluded.SchemaVersion,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;


        await using var command =
            connection.CreateCommand();


        command.CommandText =
            sql;


        command.Parameters.AddWithValue(
            "$version",
            version);


        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            DateTime.UtcNow.ToString("O"));


        await command.ExecuteNonQueryAsync();
    }


    // =========================================================
    // EXECUTE
    // =========================================================

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql)
    {
        await using var command =
            connection.CreateCommand();


        command.CommandText =
            sql;


        await command.ExecuteNonQueryAsync();
    }
}
