using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public static class ExperimentService
{
    // =========================================================
    // CREATE PARTICIPANT
    // =========================================================

    public static async Task<Participant>
        CreateParticipantAsync(
            string studyId,
            string participantCode,
            string? groupId = null)
    {
        participantCode =
            participantCode.Trim();


        if (string.IsNullOrWhiteSpace(
                studyId))
        {
            throw new ArgumentException(
                "Study ID is required.",
                nameof(studyId));
        }


        if (string.IsNullOrWhiteSpace(
                participantCode))
        {
            throw new ArgumentException(
                "Participant code is required.",
                nameof(participantCode));
        }


        var participant =
            new Participant
            {
                Id =
                    Guid.NewGuid().ToString(),

                StudyId =
                    studyId,

                GroupId =
                    groupId,

                ParticipantCode =
                    participantCode,

                Status =
                    "Active",

                IsEligible =
                    true,

                CreatedAtUtc =
                    DateTime.UtcNow,

                UpdatedAtUtc =
                    DateTime.UtcNow
            };


        await ParticipantRepository.CreateAsync(
            participant);


        if (!string.IsNullOrWhiteSpace(
                groupId))
        {
            await AssignParticipantToGroupAsync(
                participant,
                groupId,
                "Manual");
        }


        return participant;
    }


    // =========================================================
    // ASSIGN PARTICIPANT
    // =========================================================

    public static async Task<ParticipantAssignment>
        AssignParticipantToGroupAsync(
            Participant participant,
            string groupId,
            string assignmentMethod = "Manual",
            int? randomizationSeed = null,
            int? assignmentOrder = null)
    {
        if (string.IsNullOrWhiteSpace(
                groupId))
        {
            throw new ArgumentException(
                "Group ID is required.",
                nameof(groupId));
        }


        await ParticipantAssignmentRepository
            .DeactivateForParticipantAsync(
                participant.Id);


        participant.GroupId =
            groupId;


        participant.UpdatedAtUtc =
            DateTime.UtcNow;


        await ParticipantRepository.UpdateAsync(
            participant);


        var assignment =
            new ParticipantAssignment
            {
                Id =
                    Guid.NewGuid().ToString(),

                StudyId =
                    participant.StudyId,

                ParticipantId =
                    participant.Id,

                GroupId =
                    groupId,

                AssignmentMethod =
                    assignmentMethod,

                RandomizationSeed =
                    randomizationSeed,

                AssignmentOrder =
                    assignmentOrder,

                IsActive =
                    true,

                AssignedAtUtc =
                    DateTime.UtcNow
            };


        await ParticipantAssignmentRepository
            .CreateAsync(
                assignment);


        return assignment;
    }


    // =========================================================
    // BALANCED RANDOM ASSIGNMENT
    //
    // Assigns participant to the currently smallest group.
    // Ties are randomized.
    // =========================================================

    public static async Task<ParticipantAssignment>
        AssignBalancedRandomAsync(
            Participant participant)
    {
        var groups =
            await GroupRepository
                .GetActiveByStudyAsync(
                    participant.StudyId);


        if (groups.Count == 0)
        {
            throw new InvalidOperationException(
                "The study has no active groups.");
        }


        var participants =
            await ParticipantRepository
                .GetByStudyAsync(
                    participant.StudyId);


        var random =
            new Random();


        var selectedGroup =
            groups
                .Select(
                    group =>
                        new
                        {
                            Group =
                                group,

                            Count =
                                participants.Count(
                                    p =>
                                        p.GroupId ==
                                        group.Id),

                            RandomTieBreak =
                                random.Next()
                        })
                .OrderBy(
                    item =>
                        item.Count)
                .ThenBy(
                    item =>
                        item.RandomTieBreak)
                .First()
                .Group;


        return await AssignParticipantToGroupAsync(
            participant,
            selectedGroup.Id,
            "BalancedRandom");
    }


    // =========================================================
    // GET PARTICIPANTS
    // =========================================================

    public static async Task<List<Participant>>
        GetParticipantsAsync(
            string studyId)
    {
        return await ParticipantRepository
            .GetByStudyAsync(
                studyId);
    }


    // =========================================================
    // CREATE SESSION
    // =========================================================

    public static async Task<ExperimentSession>
        CreateSessionAsync(
            Participant participant)
    {
        return await SessionLifecycleService
            .CreateSessionAsync(participant);
    }


    // =========================================================
    // GET STIMULI FOR PARTICIPANT
    // =========================================================

    public static async Task<List<StimulusPost>>
        GetStimuliForParticipantAsync(
            Participant participant,
            bool randomize = false,
            int? seed = null)
    {
        List<StimulusPost> posts;


        if (!string.IsNullOrWhiteSpace(
                participant.GroupId))
        {
            posts =
                await StimulusPostRepository
                    .GetForGroupAsync(
                        participant.StudyId,
                        participant.GroupId);
        }
        else
        {
            posts =
                await StimulusPostRepository
                    .GetByStudyAsync(
                        participant.StudyId);


            posts =
                posts
                    .Where(
                        post =>
                            string.IsNullOrWhiteSpace(
                                post.GroupId))
                    .ToList();
        }


        if (!randomize)
        {
            return posts
                .OrderBy(
                    post =>
                        post.OrderIndex)
                .ToList();
        }


        var random =
            seed.HasValue
                ? new Random(seed.Value)
                : new Random();


        return posts
            .OrderBy(
                _ =>
                    random.Next())
            .ToList();
    }


    // =========================================================
    // RECORD EVENT
    // =========================================================

    public static async Task<InteractionEvent>
        RecordEventAsync(
            string studyId,
            string sessionId,
            string participantId,
            string eventType,
            long sessionElapsedMilliseconds,
            string? groupId = null,
            string? stimulusPostId = null,
            long? stimulusElapsedMilliseconds = null,
            long? durationMilliseconds = null,
            string? target = null,
            string? valueText = null,
            double? valueNumber = null,
            bool? valueBoolean = null,
            double? scrollDepthPercent = null,
            string? syncMarker = null,
            string? metadataJson = null)
    {
        var sequenceNumber =
            await InteractionEventRepository
                .GetNextSequenceNumberAsync(
                    sessionId);


        var interactionEvent =
            new InteractionEvent
            {
                Id =
                    Guid.NewGuid().ToString(),

                StudyId =
                    studyId,

                SessionId =
                    sessionId,

                ParticipantId =
                    participantId,

                GroupId =
                    groupId,

                StimulusPostId =
                    stimulusPostId,

                EventType =
                    eventType,

                TimestampUtc =
                    DateTime.UtcNow,

                SessionElapsedMilliseconds =
                    sessionElapsedMilliseconds,

                StimulusElapsedMilliseconds =
                    stimulusElapsedMilliseconds,

                DurationMilliseconds =
                    durationMilliseconds,

                SequenceNumber =
                    sequenceNumber,

                Target =
                    target,

                ValueText =
                    valueText,

                ValueNumber =
                    valueNumber,

                ValueBoolean =
                    valueBoolean,

                ScrollDepthPercent =
                    scrollDepthPercent,

                SyncMarker =
                    syncMarker,

                MetadataJson =
                    metadataJson
            };


        await InteractionEventRepository
            .CreateAsync(
                interactionEvent);


        return interactionEvent;
    }


    // =========================================================
    // SESSION START EVENT
    // =========================================================

    public static async Task RecordSessionStartedAsync(
        ExperimentSession session)
    {
        await RecordEventAsync(
            session.StudyId,
            session.Id,
            session.ParticipantId,
            "SessionStarted",
            0,
            session.GroupId,
            syncMarker:
                $"SESSION_START_{session.Id}");
    }


    // =========================================================
    // POST SHOWN
    // =========================================================

    public static async Task RecordPostShownAsync(
        ExperimentSession session,
        StimulusPost post,
        long sessionElapsedMilliseconds)
    {
        await RecordEventAsync(
            session.StudyId,
            session.Id,
            session.ParticipantId,
            "PostShown",
            sessionElapsedMilliseconds,
            session.GroupId,
            post.Id,
            stimulusElapsedMilliseconds:
                0,
            syncMarker:
                $"POST_SHOWN_{post.Id}");
    }


    // =========================================================
    // POST EXITED
    // =========================================================

    public static async Task RecordPostExitedAsync(
        ExperimentSession session,
        StimulusPost post,
        long sessionElapsedMilliseconds,
        long exposureMilliseconds)
    {
        await RecordEventAsync(
            session.StudyId,
            session.Id,
            session.ParticipantId,
            "PostExited",
            sessionElapsedMilliseconds,
            session.GroupId,
            post.Id,
            stimulusElapsedMilliseconds:
                exposureMilliseconds,
            durationMilliseconds:
                exposureMilliseconds,
            syncMarker:
                $"POST_EXITED_{post.Id}");
    }


    // =========================================================
    // CLICK
    // =========================================================

    public static async Task RecordClickAsync(
        ExperimentSession session,
        StimulusPost post,
        string target,
        long sessionElapsedMilliseconds,
        long stimulusElapsedMilliseconds)
    {
        await RecordEventAsync(
            session.StudyId,
            session.Id,
            session.ParticipantId,
            "Click",
            sessionElapsedMilliseconds,
            session.GroupId,
            post.Id,
            stimulusElapsedMilliseconds:
                stimulusElapsedMilliseconds,
            target:
                target,
            syncMarker:
                $"CLICK_{target}_{post.Id}");
    }
}
