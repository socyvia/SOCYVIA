using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public static class StimulusPostService
{
    // =========================================================
    // SUPPORTED CONTENT TYPES
    // =========================================================

    public static readonly string[] SupportedContentTypes =
    {
        "Text",
        "Image",
        "Video",
        "Audio",
        "Link",
        "Mixed"
    };


    // =========================================================
    // SUPPORTED PLATFORMS
    // =========================================================

    public static readonly string[] SupportedPlatforms =
    {
        "Generic",
        "Facebook",
        "Instagram",
        "TikTok",
        "X",
        "YouTube",
        "News",
        "Custom"
    };


    // =========================================================
    // GET POSTS
    // =========================================================

    public static async Task<List<StimulusPost>>
        GetPostsAsync(
            string studyId)
    {
        ValidateStudyId(
            studyId);


        return await StimulusPostRepository
            .GetByStudyAsync(
                studyId);
    }


    // =========================================================
    // GET ACTIVE POSTS
    // =========================================================

    public static async Task<List<StimulusPost>>
        GetActivePostsAsync(
            string studyId)
    {
        var posts =
            await GetPostsAsync(
                studyId);


        return posts
            .Where(
                post =>
                    post.IsActive)
            .OrderBy(
                post =>
                    post.OrderIndex)
            .ToList();
    }


    // =========================================================
    // GET POSTS FOR GROUP
    // =========================================================

    public static async Task<List<StimulusPost>>
        GetPostsForGroupAsync(
            string studyId,
            string groupId)
    {
        ValidateStudyId(
            studyId);


        if (string.IsNullOrWhiteSpace(
                groupId))
        {
            throw new ArgumentException(
                "Group ID is required.",
                nameof(groupId));
        }


        return await StimulusPostRepository
            .GetForGroupAsync(
                studyId.Trim(),
                groupId.Trim());
    }


    // =========================================================
    // COUNT POSTS
    // =========================================================

    public static async Task<int>
        CountPostsAsync(
            string studyId)
    {
        var posts =
            await GetPostsAsync(
                studyId);


        return posts.Count;
    }


    // =========================================================
    // COUNT ACTIVE POSTS
    // =========================================================

    public static async Task<int>
        CountActivePostsAsync(
            string studyId)
    {
        var posts =
            await GetPostsAsync(
                studyId);


        return posts.Count(
            post =>
                post.IsActive);
    }


    // =========================================================
    // CREATE POST
    // =========================================================

    public static async Task<StimulusPost>
        CreatePostAsync(
            string studyId,
            string title,
            string? bodyText,
            string contentType,
            string platform,
            string? groupId = null,
            string? sourceName = null,
            string? authorName = null,
            string? originalUrl = null,
            DateTime? publishedAtUtc = null,
            string? mediaPath = null,
            string? thumbnailPath = null,
            string? category = null,
            string? topic = null,
            string? conditionLabel = null,
            string? experimentalTag = null,
            int? originalLikes = null,
            int? originalComments = null,
            int? originalShares = null,
            int? originalSaves = null,
            long? originalViews = null,
            string? researcherNotes = null)
    {
        ValidateStudyId(
            studyId);


        title =
            NormalizeRequired(
                title,
                "Post title");


        contentType =
            NormalizeContentType(
                contentType);


        platform =
            NormalizePlatform(
                platform);


        ValidateMetrics(
            originalLikes,
            originalComments,
            originalShares,
            originalSaves,
            originalViews);


        var existingPosts =
            await StimulusPostRepository
                .GetByStudyAsync(
                    studyId.Trim());


        var nextOrder =
            existingPosts.Count == 0
                ? 0
                : existingPosts
                    .Max(
                        post =>
                            post.OrderIndex)
                  + 1;


        var now =
            DateTime.UtcNow;


        var post =
            new StimulusPost
            {
                Id =
                    Guid.NewGuid().ToString(),

                StudyId =
                    studyId.Trim(),

                GroupId =
                    NormalizeOptional(
                        groupId),

                Title =
                    title,

                BodyText =
                    NormalizeOptional(
                        bodyText)
                    ?? string.Empty,

                ContentType =
                    contentType,

                Platform =
                    platform,

                SourceName =
                    NormalizeOptional(
                        sourceName),

                AuthorName =
                    NormalizeOptional(
                        authorName),

                OriginalUrl =
                    NormalizeOptional(
                        originalUrl),

                PublishedAtUtc =
                    publishedAtUtc,

                MediaPath =
                    NormalizeOptional(
                        mediaPath),

                ThumbnailPath =
                    NormalizeOptional(
                        thumbnailPath),

                Category =
                    NormalizeOptional(
                        category),

                Topic =
                    NormalizeOptional(
                        topic),

                ConditionLabel =
                    NormalizeOptional(
                        conditionLabel),

                ExperimentalTag =
                    NormalizeOptional(
                        experimentalTag),

                OriginalLikes =
                    originalLikes,

                OriginalComments =
                    originalComments,

                OriginalShares =
                    originalShares,

                OriginalSaves =
                    originalSaves,

                OriginalViews =
                    originalViews,

                OrderIndex =
                    nextOrder,

                IsActive =
                    true,

                MinimumExposureMilliseconds =
                    0,

                MaximumExposureMilliseconds =
                    null,

                AllowRandomization =
                    true,

                ResearcherNotes =
                    NormalizeOptional(
                        researcherNotes),

                CreatedAtUtc =
                    now,

                UpdatedAtUtc =
                    now
            };


        await StimulusPostRepository
            .CreateAsync(
                post);


        return post;
    }


    // =========================================================
    // UPDATE POST
    // =========================================================

    public static async Task UpdatePostAsync(
        StimulusPost post)
    {
        if (post is null)
        {
            throw new ArgumentNullException(
                nameof(post));
        }


        ValidateStudyId(
            post.StudyId);


        post.Title =
            NormalizeRequired(
                post.Title,
                "Post title");


        post.BodyText =
            NormalizeOptional(
                post.BodyText)
            ?? string.Empty;


        post.ContentType =
            NormalizeContentType(
                post.ContentType);


        post.Platform =
            NormalizePlatform(
                post.Platform);


        post.GroupId =
            NormalizeOptional(
                post.GroupId);


        post.SourceName =
            NormalizeOptional(
                post.SourceName);


        post.AuthorName =
            NormalizeOptional(
                post.AuthorName);


        post.OriginalUrl =
            NormalizeOptional(
                post.OriginalUrl);


        post.MediaPath =
            NormalizeOptional(
                post.MediaPath);


        post.ThumbnailPath =
            NormalizeOptional(
                post.ThumbnailPath);

        post.PublishedMediaUrl =
            NormalizeOptional(
                post.PublishedMediaUrl);


        post.Category =
            NormalizeOptional(
                post.Category);


        post.Topic =
            NormalizeOptional(
                post.Topic);


        post.ConditionLabel =
            NormalizeOptional(
                post.ConditionLabel);


        post.ExperimentalTag =
            NormalizeOptional(
                post.ExperimentalTag);


        post.ResearcherNotes =
            NormalizeOptional(
                post.ResearcherNotes);


        ValidateMetrics(
            post.OriginalLikes,
            post.OriginalComments,
            post.OriginalShares,
            post.OriginalSaves,
            post.OriginalViews);


        post.OrderIndex =
            Math.Max(
                0,
                post.OrderIndex);


        post.MinimumExposureMilliseconds =
            Math.Max(
                0,
                post.MinimumExposureMilliseconds);


        if (post.MaximumExposureMilliseconds.HasValue)
        {
            post.MaximumExposureMilliseconds =
                Math.Max(
                    0,
                    post.MaximumExposureMilliseconds.Value);


            if (post.MaximumExposureMilliseconds.Value <
                post.MinimumExposureMilliseconds)
            {
                throw new ArgumentException(
                    "Maximum exposure duration cannot be lower than minimum exposure duration.");
            }
        }


        post.UpdatedAtUtc =
            DateTime.UtcNow;


        await StimulusPostRepository
            .UpdateAsync(
                post);
    }


    // =========================================================
    // DELETE POST
    // =========================================================

    public static async Task DeletePostAsync(
        string postId)
    {
        if (string.IsNullOrWhiteSpace(
                postId))
        {
            throw new ArgumentException(
                "Post ID is required.",
                nameof(postId));
        }


        await StimulusPostRepository
            .DeleteAsync(
                postId.Trim());
    }


    // =========================================================
    // SET ACTIVE
    // =========================================================

    public static async Task SetActiveAsync(
        StimulusPost post,
        bool isActive)
    {
        if (post is null)
        {
            throw new ArgumentNullException(
                nameof(post));
        }


        post.IsActive =
            isActive;


        await UpdatePostAsync(
            post);
    }


    // =========================================================
    // ASSIGN TO GROUP
    //
    // null = available to all groups
    // =========================================================

    public static async Task AssignToGroupAsync(
        StimulusPost post,
        string? groupId)
    {
        if (post is null)
        {
            throw new ArgumentNullException(
                nameof(post));
        }


        post.GroupId =
            NormalizeOptional(
                groupId);


        await UpdatePostAsync(
            post);
    }


    // =========================================================
    // DUPLICATE
    // =========================================================

    public static async Task<StimulusPost>
        DuplicatePostAsync(
            StimulusPost source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(
                nameof(source));
        }


        var existingPosts =
            await GetPostsAsync(
                source.StudyId);


        var nextOrder =
            existingPosts.Count == 0
                ? 0
                : existingPosts
                    .Max(
                        post =>
                            post.OrderIndex)
                  + 1;


        var now =
            DateTime.UtcNow;


        var duplicate =
            new StimulusPost
            {
                Id =
                    Guid.NewGuid().ToString(),

                StudyId =
                    source.StudyId,

                GroupId =
                    source.GroupId,

                Title =
                    source.Title,

                BodyText =
                    source.BodyText,

                ContentType =
                    source.ContentType,

                Platform =
                    source.Platform,

                SourceName =
                    source.SourceName,

                AuthorName =
                    source.AuthorName,

                OriginalUrl =
                    source.OriginalUrl,

                PublishedAtUtc =
                    source.PublishedAtUtc,

                MediaPath =
                    source.MediaPath,

                ThumbnailPath =
                    source.ThumbnailPath,

                PublishedMediaUrl =
                    source.PublishedMediaUrl,

                Category =
                    source.Category,

                Topic =
                    source.Topic,

                ConditionLabel =
                    source.ConditionLabel,

                ExperimentalTag =
                    source.ExperimentalTag,

                OriginalLikes =
                    source.OriginalLikes,

                OriginalComments =
                    source.OriginalComments,

                OriginalShares =
                    source.OriginalShares,

                OriginalSaves =
                    source.OriginalSaves,

                OriginalViews =
                    source.OriginalViews,

                OrderIndex =
                    nextOrder,

                IsActive =
                    source.IsActive,

                MinimumExposureMilliseconds =
                    source.MinimumExposureMilliseconds,

                MaximumExposureMilliseconds =
                    source.MaximumExposureMilliseconds,

                AllowRandomization =
                    source.AllowRandomization,

                CustomMetadataJson =
                    source.CustomMetadataJson,

                ResearcherNotes =
                    source.ResearcherNotes,

                CreatedAtUtc =
                    now,

                UpdatedAtUtc =
                    now
            };


        await StimulusPostRepository
            .CreateAsync(
                duplicate);


        return duplicate;
    }


    // =========================================================
    // REORDER
    // =========================================================

    public static async Task ReorderAsync(
        string studyId,
        IReadOnlyList<string> orderedPostIds)
    {
        ValidateStudyId(
            studyId);


        if (orderedPostIds is null)
        {
            throw new ArgumentNullException(
                nameof(orderedPostIds));
        }


        var posts =
            await GetPostsAsync(
                studyId);


        var postById =
            posts.ToDictionary(
                post =>
                    post.Id,
                StringComparer.Ordinal);


        for (var index = 0;
             index < orderedPostIds.Count;
             index++)
        {
            var id =
                orderedPostIds[index];


            if (!postById.TryGetValue(
                    id,
                    out var post))
            {
                continue;
            }


            if (post.OrderIndex ==
                index)
            {
                continue;
            }


            post.OrderIndex =
                index;


            await StimulusPostRepository
                .UpdateAsync(
                    post);
        }
    }


    // =========================================================
    // NORMALIZE ORDER
    //
    // Useful after deletion/import.
    // =========================================================

    public static async Task NormalizeOrderAsync(
        string studyId)
    {
        var posts =
            await GetPostsAsync(
                studyId);


        var ordered =
            posts
                .OrderBy(
                    post =>
                        post.OrderIndex)
                .ThenBy(
                    post =>
                        post.CreatedAtUtc)
                .ToList();


        for (var index = 0;
             index < ordered.Count;
             index++)
        {
            var post =
                ordered[index];


            if (post.OrderIndex ==
                index)
            {
                continue;
            }


            post.OrderIndex =
                index;


            await StimulusPostRepository
                .UpdateAsync(
                    post);
        }
    }


    // =========================================================
    // IMPORT VALIDATION
    //
    // We are preparing this now for the future
    // SOCYVIA CSV / Excel template.
    // =========================================================

    public static List<string> ValidateImportedPost(
        StimulusPost post)
    {
        var errors =
            new List<string>();


        if (string.IsNullOrWhiteSpace(
                post.Title))
        {
            errors.Add(
                "Title is required.");
        }


        if (!SupportedContentTypes.Contains(
                post.ContentType,
                StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(
                $"Unsupported content type: {post.ContentType}");
        }


        if (!SupportedPlatforms.Contains(
                post.Platform,
                StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(
                $"Unsupported platform: {post.Platform}");
        }


        if (post.OriginalLikes < 0)
        {
            errors.Add(
                "Likes cannot be negative.");
        }


        if (post.OriginalComments < 0)
        {
            errors.Add(
                "Comments cannot be negative.");
        }


        if (post.OriginalShares < 0)
        {
            errors.Add(
                "Shares cannot be negative.");
        }


        if (post.OriginalSaves < 0)
        {
            errors.Add(
                "Saves cannot be negative.");
        }


        if (post.OriginalViews < 0)
        {
            errors.Add(
                "Views cannot be negative.");
        }


        if (post.MinimumExposureMilliseconds < 0)
        {
            errors.Add(
                "Minimum exposure duration cannot be negative.");
        }


        if (post.MaximumExposureMilliseconds.HasValue &&
            post.MaximumExposureMilliseconds.Value < 0)
        {
            errors.Add(
                "Maximum exposure duration cannot be negative.");
        }


        if (post.MaximumExposureMilliseconds.HasValue &&
            post.MaximumExposureMilliseconds.Value <
            post.MinimumExposureMilliseconds)
        {
            errors.Add(
                "Maximum exposure duration cannot be lower than minimum exposure duration.");
        }


        return errors;
    }


    // =========================================================
    // VALIDATION HELPERS
    // =========================================================

    private static void ValidateStudyId(
        string studyId)
    {
        if (string.IsNullOrWhiteSpace(
                studyId))
        {
            throw new ArgumentException(
                "Study ID is required.",
                nameof(studyId));
        }
    }


    private static string NormalizeRequired(
        string? value,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new ArgumentException(
                $"{fieldName} is required.");
        }


        return value.Trim();
    }


    private static string? NormalizeOptional(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }


        return value.Trim();
    }


    private static string NormalizeContentType(
        string? contentType)
    {
        if (string.IsNullOrWhiteSpace(
                contentType))
        {
            return "Text";
        }


        var normalized =
            SupportedContentTypes
                .FirstOrDefault(
                    type =>
                        string.Equals(
                            type,
                            contentType.Trim(),
                            StringComparison.OrdinalIgnoreCase));


        if (normalized is null)
        {
            throw new ArgumentException(
                $"Unsupported content type: {contentType}");
        }


        return normalized;
    }


    private static string NormalizePlatform(
        string? platform)
    {
        if (string.IsNullOrWhiteSpace(
                platform))
        {
            return "Generic";
        }


        var normalized =
            SupportedPlatforms
                .FirstOrDefault(
                    value =>
                        string.Equals(
                            value,
                            platform.Trim(),
                            StringComparison.OrdinalIgnoreCase));


        if (normalized is null)
        {
            throw new ArgumentException(
                $"Unsupported platform: {platform}");
        }


        return normalized;
    }


    private static void ValidateMetrics(
        int? likes,
        int? comments,
        int? shares,
        int? saves,
        long? views)
    {
        if (likes < 0)
        {
            throw new ArgumentException(
                "Likes cannot be negative.");
        }


        if (comments < 0)
        {
            throw new ArgumentException(
                "Comments cannot be negative.");
        }


        if (shares < 0)
        {
            throw new ArgumentException(
                "Shares cannot be negative.");
        }


        if (saves < 0)
        {
            throw new ArgumentException(
                "Saves cannot be negative.");
        }


        if (views < 0)
        {
            throw new ArgumentException(
                "Views cannot be negative.");
        }
    }
}
