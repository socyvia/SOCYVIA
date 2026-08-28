using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SOCYVIA.Models;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

public sealed class ParticipantFeedCardOptions
{
    public bool IsPreview { get; init; }
    public bool IsFocused { get; init; }
    public Func<string, string, bool?, string?, Task>? InteractionAsync { get; init; }
    public Func<RuntimePostPresentation, Task>? OpenRequestedAsync { get; init; }
    public Func<RuntimePostPresentation, Task>? CommentRequestedAsync { get; init; }
    public Func<RuntimePostPresentation, int, Task>? CommentSubmittedAsync { get; init; }
}

public static class ParticipantFeedCardFactory
{
    public static Border Create(
        RuntimePostPresentation post,
        ParticipantFeedCardOptions? options = null)
    {
        options ??= new ParticipantFeedCardOptions();
        var rtl = post.IsRightToLeftContent;
        var alignment = rtl ? TextAlignment.Right : TextAlignment.Left;
        var panel = new StackPanel
        {
            Spacing = options.IsFocused ? 16 : 13,
            FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };

        panel.Children.Add(CreateIdentityHeader(post, alignment));
        if (!string.IsNullOrWhiteSpace(post.Source.Title))
        {
            panel.Children.Add(new TextBlock
            {
                Text = post.Source.Title,
                FontSize = options.IsFocused ? 18 : 15,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush("#172941"),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = alignment,
                FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
            });
        }

        var body = new TextBlock
        {
            Text = post.Source.BodyText,
            FontSize = options.IsFocused ? 12 : 11.2,
            LineHeight = options.IsFocused ? 22 : 20,
            Foreground = Brush("#33455D"),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = alignment,
            FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            MaxHeight = options.IsFocused ? double.PositiveInfinity : 104
        };
        if (!string.IsNullOrWhiteSpace(post.Source.BodyText))
            panel.Children.Add(body);

        if (!options.IsFocused && post.Source.BodyText.Length > 280)
        {
            var readMore = Action(Localize("قراءة المزيد", "Read more"), quiet: true);
            readMore.HorizontalAlignment = rtl
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left;
            readMore.Click += async (_, _) =>
            {
                post.IsExpanded = !post.IsExpanded;
                body.MaxHeight = post.IsExpanded ? double.PositiveInfinity : 104;
                readMore.Content = post.IsExpanded
                    ? Localize("عرض أقل", "Show less")
                    : Localize("قراءة المزيد", "Read more");
                await NotifyAsync(options, "Click", "ReadMore", post.IsExpanded, null);
            };
            panel.Children.Add(readMore);
        }

        var media = CreateMedia(post.Source);
        if (media is not null) panel.Children.Add(media);
        var metrics = CreateMetrics(post);
        if (metrics is not null) panel.Children.Add(metrics);
        panel.Children.Add(new Border { Height = 1, Background = Brush("#E7EBF1") });
        panel.Children.Add(CreateActionRow(post, options, rtl));

        return new Border
        {
            Padding = new Thickness(options.IsFocused ? 26 : 20),
            Background = Brushes.White,
            BorderBrush = Brush("#DDE3EC"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(15),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 16,
                OffsetY = 4,
                Color = Color.Parse("#0B172941")
            }),
            Child = panel
        };
    }

    public static Control CreateFocused(
        RuntimePostPresentation post,
        ParticipantFeedCardOptions options,
        Func<Task> closeAsync)
    {
        var rtl = post.IsRightToLeftContent;
        var comments = ReadConfiguredComments(post.Source);
        var focusedOptions = new ParticipantFeedCardOptions
        {
            IsPreview = options.IsPreview,
            IsFocused = true,
            InteractionAsync = options.InteractionAsync,
            CommentRequestedAsync = options.CommentRequestedAsync,
            CommentSubmittedAsync = options.CommentSubmittedAsync
        };
        var content = new StackPanel { Spacing = 14 };
        var close = Action(Localize("العودة إلى الخلاصة", "Back to feed"), quiet: true);
        close.HorizontalAlignment = rtl ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        close.Click += async (_, _) => await closeAsync();
        content.Children.Add(close);
        content.Children.Add(Create(post, focusedOptions));

        var commentsPanel = new StackPanel
        {
            Spacing = 10,
            FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };
        commentsPanel.Children.Add(new TextBlock
        {
            Text = Localize("التعليقات", "Comments"),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("#22344D"),
            TextAlignment = rtl ? TextAlignment.Right : TextAlignment.Left
        });
        if (comments.Count == 0)
        {
            commentsPanel.Children.Add(new TextBlock
            {
                Text = Localize(
                    "لا توجد تعليقات مهيأة لهذه المادة.",
                    "No comments are configured for this content."),
                FontSize = 9,
                Foreground = Brush("#7A8799"),
                TextAlignment = rtl ? TextAlignment.Right : TextAlignment.Left
            });
        }
        else
        {
            foreach (var comment in comments)
                commentsPanel.Children.Add(CreateComment(comment, rtl));
        }

        var commentBox = new TextBox
        {
            PlaceholderText = Localize("اكتب تعليقا", "Write a comment"),
            AcceptsReturn = true,
            MinHeight = 68,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = rtl ? TextAlignment.Right : TextAlignment.Left
        };
        var submit = Action(Localize("إرسال التعليق", "Submit comment"));
        submit.HorizontalAlignment = rtl ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        submit.Click += async (_, _) =>
        {
            var length = commentBox.Text?.Trim().Length ?? 0;
            if (length == 0) return;
            await NotifyAsync(options, CanonicalInteractionEventTypes.CommentSubmitted,
                "FocusedCommentComposer", null, $"length:{length}");
            if (options.CommentSubmittedAsync is not null)
                await options.CommentSubmittedAsync(post, length);
            commentBox.Text = string.Empty;
        };
        commentsPanel.Children.Add(commentBox);
        commentsPanel.Children.Add(submit);
        content.Children.Add(new Border
        {
            Padding = new Thickness(20),
            Background = Brush("#F9FBFD"),
            BorderBrush = Brush("#DDE3EC"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = commentsPanel
        });
        return content;
    }

    public static int GetConfiguredCommentCount(SnapshotStimulus stimulus) =>
        ReadConfiguredComments(stimulus).Count;

    private static Control CreateActionRow(
        RuntimePostPresentation post,
        ParticipantFeedCardOptions options,
        bool rtl)
    {
        var actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            FlowDirection = FlowDirection.LeftToRight
        };
        var like = Action(post.IsLiked
            ? Localize("تم الإعجاب", "Liked")
            : Localize("إعجاب", "Like"));
        like.Click += async (_, _) =>
        {
            post.IsLiked = !post.IsLiked;
            if (post.Likes.HasValue)
                post.Likes = Math.Max(0, post.Likes.Value + (post.IsLiked ? 1 : -1));
            like.Content = post.IsLiked
                ? Localize("تم الإعجاب", "Liked")
                : Localize("إعجاب", "Like");
            await NotifyAsync(options, "Click", "LikeButton", post.IsLiked, null);
            await NotifyAsync(options, CanonicalInteractionEventTypes.LikeClicked,
                "Like", post.IsLiked, null);
        };
        var comment = Action(Localize("تعليق", "Comment"));
        comment.Click += async (_, _) =>
        {
            await NotifyAsync(options, "Click", "CommentButton", null, null);
            await NotifyAsync(options, CanonicalInteractionEventTypes.CommentOpened,
                "CommentButton", null, null);
            if (options.CommentRequestedAsync is not null)
                await options.CommentRequestedAsync(post);
        };
        var open = Action(options.IsFocused && !string.IsNullOrWhiteSpace(post.Source.OriginalUrl)
            ? Localize("فتح الرابط", "Open link")
            : Localize("فتح", "Open"));
        open.Click += async (_, _) =>
        {
            await NotifyAsync(options, "Click",
                options.IsFocused ? "SourceLink" : "OpenContent", null,
                post.Source.OriginalUrl);
            if (options.IsFocused)
            {
                if (!string.IsNullOrWhiteSpace(post.Source.OriginalUrl))
                    await NotifyAsync(options, CanonicalInteractionEventTypes.LinkOpened,
                        "SourceLink", null, post.Source.OriginalUrl);
            }
            else if (options.OpenRequestedAsync is not null)
            {
                await options.OpenRequestedAsync(post);
            }
        };
        Grid.SetColumn(like, rtl ? 2 : 0);
        Grid.SetColumn(comment, 1);
        Grid.SetColumn(open, rtl ? 0 : 2);
        actions.Children.Add(like);
        actions.Children.Add(comment);
        actions.Children.Add(open);
        return actions;
    }

    private static Control CreateIdentityHeader(RuntimePostPresentation post, TextAlignment alignment)
    {
        var author = post.ShowAuthor
            ? post.Source.AuthorName ?? post.Source.SourceName ?? Localize("حساب بحثي", "Research account")
            : Localize("حساب", "Account");
        var initials = string.IsNullOrWhiteSpace(author) ? "R" : author.Trim()[0].ToString().ToUpperInvariant();
        var rtl = post.IsRightToLeftContent;
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("42,10,*,Auto"),
            FlowDirection = FlowDirection.LeftToRight
        };
        grid.Children.Add(new Border
        {
            Width = 42, Height = 42, CornerRadius = new CornerRadius(21),
            Background = Brush("#E9EFF7"),
            Child = new TextBlock
            {
                Text = initials, FontSize = 12, FontWeight = FontWeight.SemiBold,
                Foreground = Brush("#36577E"), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center
            }
        }.WithColumn(rtl ? 3 : 0));
        grid.Children.Add(new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            Children =
            {
                new TextBlock
                {
                    Text = post.ShowAuthor ? author : string.Empty, FontSize = 10.8,
                    FontWeight = FontWeight.SemiBold, Foreground = Brush("#263750"),
                    TextAlignment = alignment
                },
                new TextBlock
                {
                    Text = post.ShowTimestamp && post.Source.PublishedAtUtc.HasValue
                        ? post.Source.PublishedAtUtc.Value.ToLocalTime().ToString("g") : string.Empty,
                    FontSize = 8.2, Foreground = Brush("#8491A3"), TextAlignment = alignment
                }
            }
        }.WithColumn(2));
        var source = new Border
        {
            IsVisible = post.ShowPlatformIdentity && !string.IsNullOrWhiteSpace(post.Source.Platform),
            Padding = new Thickness(9, 4), Background = Brush("#EEF4FF"),
            CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = post.Source.Platform, FontSize = 7.5, FontWeight = FontWeight.SemiBold,
                Foreground = Brush("#315EB4"), TextAlignment = TextAlignment.Center
            }
        };
        Grid.SetColumn(source, rtl ? 0 : 3);
        grid.Children.Add(source);
        return grid;
    }

    private static Control? CreateMedia(SnapshotStimulus stimulus)
    {
        var path = !string.IsNullOrWhiteSpace(stimulus.MediaPath)
            ? stimulus.MediaPath : stimulus.ThumbnailPath;
        Bitmap? bitmap = null;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try { bitmap = new Bitmap(path); }
            catch (Exception exception)
            {
                ApplicationDiagnosticsService.LogException(exception, "Participant media preview");
            }
        }
        if (stimulus.ContentType.Equals("Image", StringComparison.OrdinalIgnoreCase) ||
            stimulus.ContentType.Equals("Mixed", StringComparison.OrdinalIgnoreCase))
            return bitmap is not null ? ImageSurface(bitmap, stimulus.ContentType)
                : string.IsNullOrWhiteSpace(stimulus.OriginalUrl) ? null : LinkSurface(stimulus);
        if (stimulus.ContentType.Equals("Video", StringComparison.OrdinalIgnoreCase))
            return MediaStateSurface(bitmap, "VIDEO", Localize("نسخة فيديو بحثية محلية", "Local video research copy"));
        if (stimulus.ContentType.Equals("Audio", StringComparison.OrdinalIgnoreCase))
            return MediaStateSurface(bitmap, "AUDIO", Localize("نسخة صوتية بحثية محلية", "Local audio research copy"), 118);
        if (stimulus.ContentType.Equals("Link", StringComparison.OrdinalIgnoreCase))
            return LinkSurface(stimulus, bitmap);
        return bitmap is null ? null : ImageSurface(bitmap, stimulus.ContentType);
    }

    private static Control ImageSurface(Bitmap bitmap, string type) => new Border
    {
        Height = 340, Background = Brush("#EEF1F5"), CornerRadius = new CornerRadius(11),
        ClipToBounds = true,
        Child = new Grid
        {
            Children =
            {
                new Image { Source = bitmap, Stretch = Stretch.UniformToFill },
                new Border
                {
                    Padding = new Thickness(8, 4), Margin = new Thickness(12),
                    Background = Brush("#D918263A"), CornerRadius = new CornerRadius(7),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Child = new TextBlock { Text = type.ToUpperInvariant(), Foreground = Brushes.White, FontSize = 7, FontWeight = FontWeight.SemiBold }
                }
            }
        }
    };

    private static Control MediaStateSurface(Bitmap? bitmap, string type, string label, double height = 250)
    {
        var grid = new Grid { Background = bitmap is null ? Brush("#E9EEF4") : null };
        if (bitmap is not null)
        {
            grid.Children.Add(new Image { Source = bitmap, Stretch = Stretch.UniformToFill, Opacity = 0.72 });
            grid.Children.Add(new Border { Background = Brush("#70152436") });
        }
        grid.Children.Add(new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 7,
            Children =
            {
                new Border
                {
                    Width = 48, Height = 48, CornerRadius = new CornerRadius(24),
                    Background = bitmap is null ? Brushes.White : Brush("#EFFFFFFF"),
                    Child = new TextBlock
                    {
                        Text = type == "VIDEO" ? "▶" : "≈", FontSize = 17,
                        Foreground = Brush("#2563EB"), HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                },
                new TextBlock
                {
                    Text = label, Foreground = bitmap is null ? Brush("#53647B") : Brushes.White,
                    FontSize = 9, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center
                }
            }
        });
        return new Border { Height = height, CornerRadius = new CornerRadius(11), ClipToBounds = true, Child = grid };
    }

    private static Control LinkSurface(SnapshotStimulus source, Bitmap? bitmap = null)
    {
        var host = Uri.TryCreate(source.OriginalUrl, UriKind.Absolute, out var uri)
            ? uri.Host : Localize("مصدر محفوظ", "Saved source");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(bitmap is null ? "0,*" : "126,*") };
        if (bitmap is not null) grid.Children.Add(new Image { Source = bitmap, Stretch = Stretch.UniformToFill });
        grid.Children.Add(new StackPanel
        {
            Margin = new Thickness(14), Spacing = 5, VerticalAlignment = VerticalAlignment.Center,
            [Grid.ColumnProperty] = 1,
            Children =
            {
                new TextBlock { Text = "LINK", FontSize = 7, FontWeight = FontWeight.SemiBold, Foreground = Brush("#2563EB") },
                new TextBlock { Text = source.Title, FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = Brush("#263750"), TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = host, FontSize = 8, Foreground = Brush("#7A8799"), TextTrimming = TextTrimming.CharacterEllipsis }
            }
        });
        return new Border { MinHeight = 116, Background = Brush("#F3F6F9"), BorderBrush = Brush("#DDE3EC"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), ClipToBounds = true, Child = grid };
    }

    private static Control? CreateMetrics(RuntimePostPresentation post)
    {
        var values = new List<(string Label, long? Value)>
        {
            (Localize("إعجاب", "Likes"), post.Likes), (Localize("تعليق", "Comments"), post.Comments),
            (Localize("مشاركة", "Shares"), post.Shares), (Localize("حفظ", "Saves"), post.Saves),
            (Localize("مشاهدة", "Views"), post.Views)
        };
        var visible = values.Where(item => item.Value.HasValue).ToList();
        if (visible.Count == 0) return null;
        var panel = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, ItemSpacing = 22 };
        foreach (var (label, value) in visible)
        {
            panel.Children.Add(new StackPanel
            {
                Spacing = 1, HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = RuntimePresentationService.FormatMetric(value!.Value), FontSize = 10.2, FontWeight = FontWeight.SemiBold, Foreground = Brush("#2B3D55"), TextAlignment = TextAlignment.Center },
                    new TextBlock { Text = label, FontSize = 7.5, Foreground = Brush("#7A8799"), TextAlignment = TextAlignment.Center }
                }
            });
        }
        return panel;
    }

    private static IReadOnlyList<DemoComment> ReadConfiguredComments(SnapshotStimulus stimulus)
    {
        if (string.IsNullOrWhiteSpace(stimulus.SourceMetadataJson))
            return Array.Empty<DemoComment>();
        try
        {
            using var document = JsonDocument.Parse(stimulus.SourceMetadataJson);
            if (!document.RootElement.TryGetProperty("DemoComments", out var values) ||
                values.ValueKind != JsonValueKind.Array)
                return Array.Empty<DemoComment>();
            return values.EnumerateArray().Select(value => new DemoComment(
                value.TryGetProperty("Author", out var author) ? author.GetString() ?? "Demo account" : "Demo account",
                value.TryGetProperty("Text", out var text) ? text.GetString() ?? string.Empty : string.Empty))
                .Where(value => value.Text.Length > 0)
                .ToList();
        }
        catch
        {
            return Array.Empty<DemoComment>();
        }
    }

    private static Control CreateComment(DemoComment comment, bool rtl) => new Border
    {
        Padding = new Thickness(12, 10), Background = Brushes.White,
        BorderBrush = Brush("#E4E9F0"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Child = new StackPanel
        {
            Spacing = 3, FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            Children =
            {
                new TextBlock { Text = comment.Author, FontSize = 8.5, FontWeight = FontWeight.SemiBold, Foreground = Brush("#31435B"), TextAlignment = rtl ? TextAlignment.Right : TextAlignment.Left },
                new TextBlock { Text = comment.Text, FontSize = 9, Foreground = Brush("#53647B"), TextWrapping = TextWrapping.Wrap, TextAlignment = rtl ? TextAlignment.Right : TextAlignment.Left }
            }
        }
    };

    private static Button Action(string text, bool quiet = false) => new()
    {
        Content = text,
        Classes = { quiet ? "participantQuiet" : "participantAction" },
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static Task NotifyAsync(
        ParticipantFeedCardOptions options,
        string eventType,
        string target,
        bool? valueBoolean,
        string? valueText) =>
        options.InteractionAsync?.Invoke(eventType, target, valueBoolean, valueText)
        ?? Task.CompletedTask;

    private static string Localize(string arabic, string english) =>
        LocalizationService.IsArabic ? arabic : english;
    private static IBrush Brush(string value) => new SolidColorBrush(Color.Parse(value));
    private static T WithColumn<T>(this T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }
    private sealed record DemoComment(string Author, string Text);
}
