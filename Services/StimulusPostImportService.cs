using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public static class StimulusPostImportService
{
    // =========================================================
    // TEMPLATE VERSION
    // =========================================================

    public const string TemplateVersion =
        "SOCYVIA_POSTS_V1";


    // =========================================================
    // TEMPLATE COLUMNS
    // =========================================================

    public static readonly string[] TemplateColumns =
    {
        "Title",
        "BodyText",
        "Platform",
        "ContentType",
        "OriginalUrl",
        "AuthorName",
        "SourceName",
        "PublishedDate",
        "OriginalLikes",
        "OriginalComments",
        "OriginalShares",
        "OriginalSaves",
        "OriginalViews",
        "GroupName",
        "Category",
        "Topic",
        "ConditionLabel",
        "ExperimentalTag",
        "ResearcherNotes"
    };


    // =========================================================
    // TEMPLATE
    // =========================================================

    public static string CreateTemplateCsv()
    {
        var builder =
            new StringBuilder();


        builder.AppendLine(
            $"# {TemplateVersion}");


        builder.AppendLine(
            string.Join(
                ",",
                TemplateColumns.Select(
                    EscapeCsv)));


        // Example row.
        builder.AppendLine(
            string.Join(
                ",",
                new[]
                {
                    EscapeCsv(
                        "Example sports post"),

                    EscapeCsv(
                        "Example caption or post text"),

                    EscapeCsv(
                        "Instagram"),

                    EscapeCsv(
                        "Video"),

                    EscapeCsv(
                        "https://example.com/post"),

                    EscapeCsv(
                        "Example account"),

                    EscapeCsv(
                        "Example source"),

                    EscapeCsv(
                        "2026-08-14"),

                    EscapeCsv(
                        "1200"),

                    EscapeCsv(
                        "85"),

                    EscapeCsv(
                        "40"),

                    EscapeCsv(
                        "12"),

                    EscapeCsv(
                        "25000"),

                    EscapeCsv(
                        string.Empty),

                    EscapeCsv(
                        "Sport"),

                    EscapeCsv(
                        "Football"),

                    EscapeCsv(
                        "Condition A"),

                    EscapeCsv(
                        "sport_video_a"),

                    EscapeCsv(
                        "Example row - delete before import")
                }));


        return builder.ToString();
    }


    // =========================================================
    // PARSE
    // =========================================================

    public static async Task<ImportPreview>
        ParseAsync(
            Stream stream,
            string studyId,
            IReadOnlyList<StudyGroup> groups)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(
                nameof(stream));
        }


        if (string.IsNullOrWhiteSpace(
                studyId))
        {
            throw new ArgumentException(
                "Study ID is required.",
                nameof(studyId));
        }


        using var reader =
            new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: true);


        var content =
            await reader.ReadToEndAsync();


        return Parse(
            content,
            studyId,
            groups);
    }


    // =========================================================
    // PARSE TEXT
    // =========================================================

    public static ImportPreview Parse(
        string csv,
        string studyId,
        IReadOnlyList<StudyGroup> groups)
    {
        var preview =
            new ImportPreview();


        if (string.IsNullOrWhiteSpace(
                csv))
        {
            preview.GeneralErrors.Add(
                "The selected file is empty.");

            return preview;
        }


        var rows =
            ParseCsvRows(
                csv);


        if (rows.Count == 0)
        {
            preview.GeneralErrors.Add(
                "No CSV rows were found.");

            return preview;
        }


        // Remove empty rows.
        rows =
            rows
                .Where(
                    row =>
                        row.Any(
                            value =>
                                !string.IsNullOrWhiteSpace(
                                    value)))
                .ToList();


        if (rows.Count == 0)
        {
            preview.GeneralErrors.Add(
                "The selected file contains no data.");

            return preview;
        }


        // Optional SOCYVIA version marker.
        if (rows[0].Count > 0 &&
            rows[0][0]
                .Trim()
                .StartsWith(
                    "# SOCYVIA_POSTS_",
                    StringComparison.OrdinalIgnoreCase))
        {
            preview.TemplateMarker =
                rows[0][0]
                    .Trim()
                    .TrimStart('#')
                    .Trim();


            rows.RemoveAt(
                0);
        }


        if (rows.Count == 0)
        {
            preview.GeneralErrors.Add(
                "The template header is missing.");

            return preview;
        }


        var headers =
            rows[0]
                .Select(
                    value =>
                        value.Trim())
                .ToList();


        rows.RemoveAt(
            0);


        ValidateHeaders(
            headers,
            preview);


        if (preview.GeneralErrors.Count > 0)
        {
            return preview;
        }


        var headerMap =
            headers
                .Select(
                    (name, index) =>
                        new
                        {
                            Name =
                                name,

                            Index =
                                index
                        })
                .ToDictionary(
                    value =>
                        value.Name,

                    value =>
                        value.Index,

                    StringComparer.OrdinalIgnoreCase);


        var groupMap =
            groups
                .Where(
                    group =>
                        !string.IsNullOrWhiteSpace(
                            group.Name))
                .GroupBy(
                    group =>
                        group.Name.Trim(),

                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group =>
                        group.Key,

                    group =>
                        group.First(),

                    StringComparer.OrdinalIgnoreCase);


        var orderIndex =
            0;


        for (var rowIndex = 0;
             rowIndex < rows.Count;
             rowIndex++)
        {
            var row =
                rows[rowIndex];


            var result =
                ParseRow(
                    row,
                    headerMap,
                    studyId,
                    groupMap,
                    orderIndex,
                    rowIndex + 2);


            preview.Rows.Add(
                result);


            if (result.IsValid)
            {
                orderIndex++;
            }
        }


        return preview;
    }


    // =========================================================
    // PARSE ROW
    // =========================================================

    private static ImportRowResult ParseRow(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> headerMap,
        string studyId,
        IReadOnlyDictionary<string, StudyGroup> groupMap,
        int orderIndex,
        int sourceRowNumber)
    {
        var result =
            new ImportRowResult
            {
                SourceRowNumber =
                    sourceRowNumber
            };


        string Value(
            string column)
        {
            if (!headerMap.TryGetValue(
                    column,
                    out var index))
            {
                return string.Empty;
            }


            if (index < 0 ||
                index >= row.Count)
            {
                return string.Empty;
            }


            return row[index]
                .Trim();
        }


        var title =
            Value(
                "Title");


        if (string.IsNullOrWhiteSpace(
                title))
        {
            result.Errors.Add(
                "Title is required.");
        }


        var platform =
            NormalizePlatform(
                Value(
                    "Platform"));


        if (platform is null)
        {
            result.Errors.Add(
                $"Unsupported platform: {Value("Platform")}");

            platform =
                "Generic";
        }


        var contentType =
            NormalizeContentType(
                Value(
                    "ContentType"));


        if (contentType is null)
        {
            result.Errors.Add(
                $"Unsupported content type: {Value("ContentType")}");

            contentType =
                "Text";
        }


        var groupName =
            Value(
                "GroupName");


        string? groupId =
            null;


        if (!string.IsNullOrWhiteSpace(
                groupName))
        {
            if (groupMap.TryGetValue(
                    groupName,
                    out var group))
            {
                groupId =
                    group.Id;
            }
            else
            {
                result.Errors.Add(
                    $"Unknown group: {groupName}");
            }
        }


        var publishedDate =
            ParseDate(
                Value(
                    "PublishedDate"),
                result);


        var likes =
            ParseNullableInt(
                Value(
                    "OriginalLikes"),
                "OriginalLikes",
                result);


        var comments =
            ParseNullableInt(
                Value(
                    "OriginalComments"),
                "OriginalComments",
                result);


        var shares =
            ParseNullableInt(
                Value(
                    "OriginalShares"),
                "OriginalShares",
                result);


        var saves =
            ParseNullableInt(
                Value(
                    "OriginalSaves"),
                "OriginalSaves",
                result);


        var views =
            ParseNullableLong(
                Value(
                    "OriginalViews"),
                "OriginalViews",
                result);


        var now =
            DateTime.UtcNow;


        result.Post =
            new StimulusPost
            {
                Id =
                    Guid.NewGuid()
                        .ToString(),

                StudyId =
                    studyId,

                GroupId =
                    groupId,

                Title =
                    title,

                BodyText =
                    Value(
                        "BodyText"),

                Platform =
                    platform,

                ContentType =
                    contentType,

                OriginalUrl =
                    NullIfEmpty(
                        Value(
                            "OriginalUrl")),

                AuthorName =
                    NullIfEmpty(
                        Value(
                            "AuthorName")),

                SourceName =
                    NullIfEmpty(
                        Value(
                            "SourceName")),

                PublishedAtUtc =
                    publishedDate,

                OriginalLikes =
                    likes,

                OriginalComments =
                    comments,

                OriginalShares =
                    shares,

                OriginalSaves =
                    saves,

                OriginalViews =
                    views,

                Category =
                    NullIfEmpty(
                        Value(
                            "Category")),

                Topic =
                    NullIfEmpty(
                        Value(
                            "Topic")),

                ConditionLabel =
                    NullIfEmpty(
                        Value(
                            "ConditionLabel")),

                ExperimentalTag =
                    NullIfEmpty(
                        Value(
                            "ExperimentalTag")),

                ResearcherNotes =
                    NullIfEmpty(
                        Value(
                            "ResearcherNotes")),

                OrderIndex =
                    orderIndex,

                IsActive =
                    true,

                MinimumExposureMilliseconds =
                    0,

                MaximumExposureMilliseconds =
                    null,

                AllowRandomization =
                    true,

                CreatedAtUtc =
                    now,

                UpdatedAtUtc =
                    now
            };


        var serviceErrors =
            StimulusPostService
                .ValidateImportedPost(
                    result.Post);


        foreach (var error in serviceErrors)
        {
            if (!result.Errors.Contains(
                    error,
                    StringComparer.OrdinalIgnoreCase))
            {
                result.Errors.Add(
                    error);
            }
        }


        return result;
    }


    // =========================================================
    // IMPORT VALID ROWS
    // =========================================================

    public static async Task<ImportCommitResult>
        ImportValidRowsAsync(
            ImportPreview preview,
            string studyId)
    {
        if (preview is null)
        {
            throw new ArgumentNullException(
                nameof(preview));
        }


        if (string.IsNullOrWhiteSpace(
                studyId))
        {
            throw new ArgumentException(
                "Study ID is required.",
                nameof(studyId));
        }


        var result =
            new ImportCommitResult();


        var existingPosts =
            await StimulusPostRepository
                .GetByStudyAsync(
                    studyId);


        var nextOrder =
            existingPosts.Count == 0
                ? 0
                : existingPosts
                    .Max(
                        post =>
                            post.OrderIndex)
                  + 1;


        foreach (var row in
                 preview.Rows)
        {
            if (!row.IsValid ||
                row.Post is null)
            {
                result.SkippedRows++;

                continue;
            }


            try
            {
                row.Post.Id =
                    Guid.NewGuid()
                        .ToString();


                row.Post.StudyId =
                    studyId;


                row.Post.OrderIndex =
                    nextOrder;


                row.Post.CreatedAtUtc =
                    DateTime.UtcNow;


                row.Post.UpdatedAtUtc =
                    DateTime.UtcNow;


                await StimulusPostRepository
                    .CreateAsync(
                        row.Post);


                nextOrder++;


                result.ImportedRows++;
            }
            catch (Exception exception)
            {
                result.FailedRows++;


                result.Errors.Add(
                    new ImportCommitError
                    {
                        SourceRowNumber =
                            row.SourceRowNumber,

                        Message =
                            exception.Message
                    });
            }
        }


        return result;
    }


    // =========================================================
    // HEADER VALIDATION
    // =========================================================

    private static void ValidateHeaders(
        IReadOnlyList<string> headers,
        ImportPreview preview)
    {
        var duplicateHeaders =
            headers
                .Where(
                    header =>
                        !string.IsNullOrWhiteSpace(
                            header))
                .GroupBy(
                    header =>
                        header,
                    StringComparer.OrdinalIgnoreCase)
                .Where(
                    group =>
                        group.Count() > 1)
                .Select(
                    group =>
                        group.Key)
                .ToList();


        foreach (var duplicate in
                 duplicateHeaders)
        {
            preview.GeneralErrors.Add(
                $"Duplicate column: {duplicate}");
        }


        var requiredHeaders =
            new[]
            {
                "Title",
                "BodyText",
                "Platform",
                "ContentType"
            };


        foreach (var required in
                 requiredHeaders)
        {
            if (!headers.Contains(
                    required,
                    StringComparer.OrdinalIgnoreCase))
            {
                preview.GeneralErrors.Add(
                    $"Required column missing: {required}");
            }
        }
    }


    // =========================================================
    // PLATFORM
    // =========================================================

    private static string? NormalizePlatform(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return "Generic";
        }


        return StimulusPostService
            .SupportedPlatforms
            .FirstOrDefault(
                platform =>
                    string.Equals(
                        platform,
                        value.Trim(),
                        StringComparison.OrdinalIgnoreCase));
    }


    // =========================================================
    // CONTENT TYPE
    // =========================================================

    private static string? NormalizeContentType(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return "Text";
        }


        return StimulusPostService
            .SupportedContentTypes
            .FirstOrDefault(
                type =>
                    string.Equals(
                        type,
                        value.Trim(),
                        StringComparison.OrdinalIgnoreCase));
    }


    // =========================================================
    // DATE
    // =========================================================

    private static DateTime? ParseDate(
        string value,
        ImportRowResult result)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }


        var formats =
            new[]
            {
                "yyyy-MM-dd",
                "yyyy/MM/dd",
                "dd/MM/yyyy",
                "dd-MM-yyyy",
                "MM/dd/yyyy"
            };


        if (DateTime.TryParseExact(
                value.Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var exact))
        {
            return DateTime.SpecifyKind(
                exact.Date,
                DateTimeKind.Utc);
        }


        if (DateTime.TryParse(
                value.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var flexible))
        {
            return DateTime.SpecifyKind(
                flexible,
                DateTimeKind.Utc);
        }


        result.Errors.Add(
            $"Invalid PublishedDate: {value}");


        return null;
    }


    // =========================================================
    // INTEGER
    // =========================================================

    private static int? ParseNullableInt(
        string value,
        string column,
        ImportRowResult result)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }


        var normalized =
            NormalizeNumber(
                value);


        if (!long.TryParse(
                normalized,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var number))
        {
            result.Errors.Add(
                $"Invalid number in {column}: {value}");

            return null;
        }


        if (number < 0)
        {
            result.Errors.Add(
                $"{column} cannot be negative.");

            return null;
        }


        if (number > int.MaxValue)
        {
            result.Errors.Add(
                $"{column} is too large.");

            return null;
        }


        return (int)number;
    }


    // =========================================================
    // LONG
    // =========================================================

    private static long? ParseNullableLong(
        string value,
        string column,
        ImportRowResult result)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }


        var normalized =
            NormalizeNumber(
                value);


        if (!long.TryParse(
                normalized,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var number))
        {
            result.Errors.Add(
                $"Invalid number in {column}: {value}");

            return null;
        }


        if (number < 0)
        {
            result.Errors.Add(
                $"{column} cannot be negative.");

            return null;
        }


        return number;
    }


    private static string NormalizeNumber(
        string value)
    {
        return value
            .Trim()
            .Replace(
                " ",
                string.Empty)
            .Replace(
                ",",
                string.Empty);
    }


    // =========================================================
    // CSV PARSER
    // =========================================================

    private static List<List<string>>
        ParseCsvRows(
            string content)
    {
        var rows =
            new List<List<string>>();


        var row =
            new List<string>();


        var value =
            new StringBuilder();


        var insideQuotes =
            false;


        for (var index = 0;
             index < content.Length;
             index++)
        {
            var character =
                content[index];


            if (insideQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 <
                        content.Length &&
                        content[index + 1] ==
                        '"')
                    {
                        value.Append(
                            '"');

                        index++;
                    }
                    else
                    {
                        insideQuotes =
                            false;
                    }
                }
                else
                {
                    value.Append(
                        character);
                }


                continue;
            }


            switch (character)
            {
                case '"':
                    insideQuotes =
                        true;

                    break;


                case ',':
                    row.Add(
                        value.ToString());

                    value.Clear();

                    break;


                case '\r':
                    if (index + 1 <
                        content.Length &&
                        content[index + 1] ==
                        '\n')
                    {
                        index++;
                    }


                    row.Add(
                        value.ToString());

                    value.Clear();


                    rows.Add(
                        row);


                    row =
                        new List<string>();

                    break;


                case '\n':
                    row.Add(
                        value.ToString());

                    value.Clear();


                    rows.Add(
                        row);


                    row =
                        new List<string>();

                    break;


                default:
                    value.Append(
                        character);

                    break;
            }
        }


        if (insideQuotes)
        {
            // Let validation reveal malformed data.
            value.Append(
                '"');
        }


        if (value.Length > 0 ||
            row.Count > 0)
        {
            row.Add(
                value.ToString());


            rows.Add(
                row);
        }


        return rows;
    }


    // =========================================================
    // CSV ESCAPE
    // =========================================================

    private static string EscapeCsv(
        string? value)
    {
        value ??=
            string.Empty;


        var mustQuote =
            value.Contains(
                ',')
            ||
            value.Contains(
                '"')
            ||
            value.Contains(
                '\n')
            ||
            value.Contains(
                '\r');


        if (!mustQuote)
        {
            return value;
        }


        return "\"" +
               value.Replace(
                   "\"",
                   "\"\"") +
               "\"";
    }


    // =========================================================
    // NULL
    // =========================================================

    private static string? NullIfEmpty(
        string value)
    {
        return string.IsNullOrWhiteSpace(
                value)
            ? null
            : value.Trim();
    }
}


// =============================================================
// IMPORT PREVIEW
// =============================================================

public sealed class ImportPreview
{
    public string? TemplateMarker { get; set; }


    public List<string> GeneralErrors { get; } =
        new();


    public List<ImportRowResult> Rows { get; } =
        new();


    public int TotalRows =>
        Rows.Count;


    public int ValidRows =>
        Rows.Count(
            row =>
                row.IsValid);


    public int InvalidRows =>
        Rows.Count(
            row =>
                !row.IsValid);


    public bool CanImport =>
        GeneralErrors.Count == 0 &&
        ValidRows > 0;
}


// =============================================================
// IMPORT ROW
// =============================================================

public sealed class ImportRowResult
{
    public int SourceRowNumber { get; set; }


    public StimulusPost? Post { get; set; }


    public List<string> Errors { get; } =
        new();


    public bool IsValid =>
        Post is not null &&
        Errors.Count == 0;
}


// =============================================================
// COMMIT RESULT
// =============================================================

public sealed class ImportCommitResult
{
    public int ImportedRows { get; set; }


    public int SkippedRows { get; set; }


    public int FailedRows { get; set; }


    public List<ImportCommitError> Errors { get; } =
        new();


    public bool IsSuccess =>
        ImportedRows > 0 &&
        FailedRows == 0;
}


// =============================================================
// COMMIT ERROR
// =============================================================

public sealed class ImportCommitError
{
    public int SourceRowNumber { get; set; }


    public string Message { get; set; } =
        string.Empty;
}