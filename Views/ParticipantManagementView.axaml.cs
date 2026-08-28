using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SOCYVIA.Models;
using SOCYVIA.Repositories;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

public partial class ParticipantManagementView : UserControl
{
    private readonly Study _study;
    private List<StudyGroup> _groups = new();
    private List<ExperimentalCondition> _conditions = new();

    public ParticipantManagementView()
        : this(new Study())
    {
    }

    public ParticipantManagementView(Study study)
    {
        _study = study;
        InitializeComponent();
        ConfigureLanguage();
        AddParticipantButton.Click += (_, _) => ShowEditor();
        EmptyAddButton.Click += (_, _) => ShowEditor();
        CancelButton.Click += (_, _) => EditorPanel.IsVisible = false;
        SaveButton.Click += async (_, _) => await SaveAsync();
        RemoteStatusFilter.SelectionChanged += async (_, _) => await ReloadRemoteSessionsAsync();
        RemoteGroupFilter.SelectionChanged += async (_, _) => await ReloadRemoteSessionsAsync();
        RemoteRunTypeFilter.SelectionChanged += async (_, _) => await ReloadRemoteSessionsAsync();
        SyncRemoteButton.Click += async (_, _) => await SyncRemoteAsync();
    }

    public async Task ReloadAsync()
    {
        _groups = await GroupRepository.GetByStudyAsync(_study.Id);
        _conditions = await ExperimentalConditionRepository.GetByStudyAsync(_study.Id);
        ConfigureRemoteFilters();
        GroupComboBox.ItemsSource = new[] { new Choice(null, Text("غير معين", "Not assigned")) }
            .Concat(_groups.Where(item => item.IsActive)
                .Select(item => new Choice(item.Id, item.Name)))
            .ToList();
        GroupComboBox.SelectedIndex = 0;
        StatusComboBox.ItemsSource = new[]
        {
            new Choice("Active", Text("نشط", "Active")),
            new Choice("Ready", Text("جاهز", "Ready"))
        };
        StatusComboBox.SelectedIndex = 0;

        var participants = await ParticipantRepository.GetByStudyAsync(_study.Id);
        ParticipantsContainer.Children.Clear();
        foreach (var participant in participants.OrderBy(item => item.ParticipantCode))
        {
            ParticipantsContainer.Children.Add(await CreateRowAsync(participant));
        }
        EmptyPanel.IsVisible = participants.Count == 0;
        await ReloadRemoteSessionsAsync();
        CountText.Text = Text(
            $"عدد المشاركين: {participants.Count}",
            $"{participants.Count} participants");
    }

    private async Task SyncRemoteAsync()
    {
        SyncRemoteButton.IsEnabled = false;
        SyncRemoteButton.Content = Text("تجري مزامنة البيانات...", "Syncing...");
        try
        {
            var configuration = await new CloudflareProviderConfigurationStore().LoadAsync();
            if (configuration is null) throw new InvalidOperationException("Cloud connection is not configured.");
            var token = await new CloudflareOAuthConnectionService().GetAccessTokenAsync(
                configuration, CloudflareOAuthClientConfiguration.LoadReleaseConfiguration());
            if (string.IsNullOrWhiteSpace(token) || !configuration.HasRequiredTextRuntimeIdentity) throw new InvalidOperationException("Cloud connection is not configured.");
            var cursor = await RemoteResearchRepository.GetCursorAsync();
            await new RemoteResearchSynchronizationService().SynchronizeAsync(configuration, token, cursor);
            await ReloadRemoteSessionsAsync();
            RemoteSessionsHint.Text = Text("البيانات محدثة", "Up to date");
        }
        catch
        {
            RemoteSessionsHint.Text = Text("تعذرت المزامنة. تحقق من اتصال السحابة وأعد المحاولة.", "Synchronization failed. Check the cloud connection and retry.");
        }
        finally { SyncRemoteButton.IsEnabled = true; SyncRemoteButton.Content = Text("مزامنة البيانات البعيدة", "Sync remote data"); }
    }

    private void ConfigureRemoteFilters()
    {
        if (RemoteStatusFilter.ItemsSource is null)
        {
            RemoteStatusFilter.ItemsSource = new[]
            {
                new Choice(null, Text("الكل", "All")),
                new Choice("Completed", Text("مكتمل", "Completed")),
                new Choice("Incomplete", Text("غير مكتمل", "Incomplete"))
            };
            RemoteStatusFilter.SelectedIndex = 0;
        }
        if (RemoteGroupFilter.ItemsSource is null)
        {
            RemoteGroupFilter.ItemsSource = new[] { new Choice(null, Text("كل المجموعات", "All groups")) }.Concat(_groups.OrderBy(item => item.SortOrder).Select(item => new Choice(item.Id, item.Name))).ToList();
            RemoteGroupFilter.SelectedIndex = 0;
        }
        if (RemoteRunTypeFilter.ItemsSource is null)
        {
            RemoteRunTypeFilter.ItemsSource = new[]
            {
                new Choice(null, Text("الكل", "All")),
                new Choice("Main", Text("الدراسة الرئيسية", "Main Study")),
                new Choice("Pilot", Text("استطلاعي", "Pilot"))
            };
            RemoteRunTypeFilter.SelectedIndex = 0;
        }
    }

    private async Task ReloadRemoteSessionsAsync()
    {
        if (RemoteStatusFilter.SelectedItem is null || RemoteGroupFilter.SelectedItem is null || RemoteRunTypeFilter.SelectedItem is null) return;
        var groupId = (RemoteGroupFilter.SelectedItem as Choice)?.Value;
        var status = (RemoteStatusFilter.SelectedItem as Choice)?.Value;
        var runType = (RemoteRunTypeFilter.SelectedItem as Choice)?.Value switch
        {
            "Main" => ExperimentRunType.Main,
            "Pilot" => ExperimentRunType.Pilot,
            _ => (ExperimentRunType?)null
        };
        var sessions = await RemoteResearchRepository.GetSessionsAsync(null, status == "Completed", _study.Id, runType);
        if (!string.IsNullOrWhiteSpace(groupId)) sessions = sessions.Where(item => item.GroupId == groupId).ToArray();
        if (status == "Incomplete") sessions = sessions.Where(item => item.CompletionState != RemoteParticipantCompletionState.CompletedEligible).ToArray();
        RemoteSessionsContainer.Children.Clear();
        foreach (var session in sessions) RemoteSessionsContainer.Children.Add(RemoteRow(session));
        RemoteSessionsEmpty.IsVisible = sessions.Count == 0;
    }

    private Control RemoteRow(RemoteParticipantSessionContract session)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("1.15*,1*,.75*,1*,1*,1*,1*") };
        grid.Children.Add(Cell(ShortId(session.ParticipantId), true));
        AddCell(grid, 1, _groups.FirstOrDefault(item => item.Id == session.GroupId)?.Name ?? session.GroupId ?? "—");
        AddRunTypeCell(grid, 2, session.RunType);
        AddCell(grid, 3, DateLabel(session.StartedAtUtc)); AddCell(grid, 4, DateLabel(session.FeedEndedAtUtc)); AddCell(grid, 5, DateLabel(session.PostQuestionnaireCompletedAtUtc));
        AddStatusCell(grid, 6, session.CompletionState == RemoteParticipantCompletionState.CompletedEligible ? "Completed" : "Incomplete");
        return new Border { Padding = new Thickness(14, 10), Background = new SolidColorBrush(Color.Parse("#F7FFFFFF")), BorderBrush = new SolidColorBrush(Color.Parse("#80AAB9CF")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Child = grid };
    }

    private static string ShortId(string value) => value.Length <= 10 ? value : value[..8] + "…";
    private static string DateLabel(DateTime? value) => value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—";

    /// <summary>Publish routes pilot inspection to this existing normalized-record surface.</summary>
    public async Task ShowPilotRemoteSessionsAsync()
    {
        ConfigureRemoteFilters();
        RemoteRunTypeFilter.SelectedItem = (RemoteRunTypeFilter.ItemsSource as IEnumerable<Choice>)?.FirstOrDefault(item => item.Value == "Pilot");
        await ReloadRemoteSessionsAsync();
    }

    private void ShowEditor()
    {
        CodeBox.Text = string.Empty;
        GroupComboBox.SelectedIndex = 0;
        StatusComboBox.SelectedIndex = 0;
        EligibleCheckBox.IsChecked = true;
        ConsentCheckBox.IsChecked = false;
        EditorError.IsVisible = false;
        EditorPanel.IsVisible = true;
        CodeBox.Focus();
    }

    private async Task SaveAsync()
    {
        var code = CodeBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            ShowError(Text("رمز المشارك مطلوب.", "Participant code is required."));
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            await ParticipantRepository.CreateAsync(new Participant
            {
                Id = Guid.NewGuid().ToString(),
                StudyId = _study.Id,
                ParticipantCode = code,
                GroupId = (GroupComboBox.SelectedItem as Choice)?.Value,
                Status = (StatusComboBox.SelectedItem as Choice)?.Value ?? "Active",
                IsEligible = EligibleCheckBox.IsChecked == true,
                ConsentAccepted = ConsentCheckBox.IsChecked == true,
                ConsentAcceptedAtUtc = ConsentCheckBox.IsChecked == true ? now : null,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            EditorPanel.IsVisible = false;
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Participant create error: {exception}");
            ShowError(Text(
                "تعذر حفظ المشارك. تأكد من أن الرمز غير مستخدم.",
                "The participant could not be saved. Ensure the code is unique."));
        }
    }

    private async Task<Control> CreateRowAsync(Participant participant)
    {
        var groupName = _groups.FirstOrDefault(item => item.Id == participant.GroupId)?.Name;
        var assignment = await ParticipantConditionAssignmentRepository
            .GetActiveForParticipantAsync(participant.Id);
        var conditionName = assignment is null
            ? null
            : _conditions.FirstOrDefault(item => item.Id == assignment.ConditionId)?.Name;
        var sessions = await ExperimentSessionRepository.GetByParticipantAsync(participant.Id);
        var sessionState = sessions.FirstOrDefault()?.Status ?? Text("لا توجد جلسة", "No session");

        var details = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.1*,1*,1*,1*,1.1*,1*")
        };
        details.Children.Add(Cell(participant.ParticipantCode, true));
        AddCell(details, 1, groupName ?? Text("غير معين", "Unassigned"));
        AddCell(details, 2, conditionName ?? Text("غير معين", "Unassigned"));
        AddStatusCell(details, 3, participant.Status);
        AddResearchStateCell(details, 4, participant);
        AddStatusCell(details, 5, sessionState);
        return new Border
        {
            Padding = new Thickness(14, 12),
            Background = new SolidColorBrush(Color.Parse("#F7FFFFFF")),
            BorderBrush = new SolidColorBrush(Color.Parse("#80AAB9CF")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Child = details
        };
    }

    private static void AddCell(Grid grid, int column, string value)
    {
        var cell = Cell(value, false);
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private static void AddStatusCell(Grid grid, int column, string value)
    {
        var badge = new Border
        {
            MinWidth = 72,
            Padding = new Thickness(8, 3),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = Cell(LocalizeStatus(value), false)
        };
        badge.Classes.Add("badge");
        badge.Classes.Add(StatusClass(value));
        Grid.SetColumn(badge, column);
        grid.Children.Add(badge);
    }

    private static void AddRunTypeCell(Grid grid, int column, ExperimentRunType value)
    {
        var isPilot = value == ExperimentRunType.Pilot;
        var badge = new Border
        {
            MinWidth = 58,
            Padding = new Thickness(8, 3),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = Cell(isPilot ? Text("استطلاعي", "Pilot") : Text("الرئيسية", "Main"), false)
        };
        badge.Classes.Add("badge");
        badge.Classes.Add(isPilot ? "warning" : "ready");
        Grid.SetColumn(badge, column);
        grid.Children.Add(badge);
    }

    private static void AddResearchStateCell(Grid grid, int column, Participant participant)
    {
        var value = !participant.IsEligible
            ? Text("غير مؤهل", "Ineligible")
            : participant.ConsentAccepted
                ? Text("مؤهل · الموافقة موثقة", "Eligible · consent")
                : Text("مؤهل · الموافقة معلقة", "Eligible · consent pending");
        var badge = new Border
        {
            Padding = new Thickness(8, 3),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = Cell(value, false)
        };
        badge.Classes.Add("badge");
        badge.Classes.Add(!participant.IsEligible
            ? "error"
            : participant.ConsentAccepted ? "success" : "warning");
        Grid.SetColumn(badge, column);
        grid.Children.Add(badge);
    }

    private static TextBlock Cell(string value, bool strong) => new()
    {
        Text = value,
        FontSize = 8.8,
        FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal,
        Foreground = new SolidColorBrush(Color.Parse(strong ? "#1E304A" : "#53627B")),
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    private void ConfigureLanguage()
    {
        var arabic = LocalizationService.IsArabic;
        RemoteRunTypeColumn.Text = Text("نوع التشغيل", "Run");
        ParticipantManagementRoot.FlowDirection = arabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        ParticipantManagementRoot.FontFamily = new FontFamily(arabic
            ? "avares://SOCYVIA/Assets/Fonts#IBM Plex Sans Arabic"
            : "avares://SOCYVIA/Assets/Fonts#IBM Plex Sans");
        RemoteSessionsTitle.Text = Text("جلسات المشاركين عن بعد", "Remote participant sessions");
        RemoteSessionsHint.Text = Text("تظهر البيانات بعد مزامنة بيانات التجربة عن بعد.", "Records appear after remote experiment data is synchronized.");
        RemoteParticipantColumn.Text = Text("المشارك", "Participant"); RemoteGroupColumn.Text = Text("المجموعة", "Group"); RemoteStartedColumn.Text = Text("البدء", "Started"); RemoteFeedEndColumn.Text = Text("نهاية الخلاصة", "Feed end"); RemotePostColumn.Text = Text("بعد التجربة", "Post"); RemoteStateColumn.Text = Text("الحالة", "Status");
        RemoteSessionsEmpty.Text = Text("لا توجد جلسات لمشاركين عن بعد حتى الآن", "No remote participant sessions yet.");
        SyncRemoteButton.Content = Text("مزامنة البيانات البعيدة", "Sync remote data");
        PageTitle.Text = Text("المشاركون", "Participants");
        PageSubtitle.Text = Text(
            "إدارة رموز المشاركين والأهلية والموافقة والتعيين دون عرض بيانات حساسة غير لازمة.",
            "Manage participant codes, eligibility, consent, and assignment without exposing unnecessary sensitive data.");
        AddParticipantButton.Content = Text("إضافة مشارك", "Add participant");
        EditorTitle.Text = Text("مشارك جديد", "New participant");
        CodeLabel.Text = Text("رمز المشارك", "Participant code");
        GroupLabel.Text = Text("المجموعة", "Group");
        StatusLabel.Text = Text("الحالة", "Status");
        EligibleCheckBox.Content = Text("مؤهل", "Eligible");
        ConsentCheckBox.Content = Text("الموافقة موثقة", "Consent recorded");
        CancelButton.Content = Text("إلغاء", "Cancel");
        SaveButton.Content = Text("حفظ المشارك", "Save participant");
        ListTitle.Text = Text("سجل المشاركين", "Participant register");
        ParticipantCodeColumnText.Text = Text("رمز المشارك", "Participant code");
        ParticipantGroupColumnText.Text = Text("المجموعة", "Group");
        ParticipantConditionColumnText.Text = Text("الشرط", "Condition");
        ParticipantStatusColumnText.Text = Text("حالة المشارك", "Participant status");
        ParticipantEligibilityColumnText.Text = Text("الأهلية والموافقة", "Eligibility & consent");
        ParticipantSessionColumnText.Text = Text("حالة الجلسة", "Session state");
        EmptyTitle.Text = Text("لا يوجد مشاركون بعد", "No participants yet");
        EmptyBody.Text = Text(
            "أضف رمز مشارك لتبدأ التعيين وتحضير الجلسات.",
            "Add a participant code to begin assignment and session preparation.");
        EmptyAddButton.Content = Text("إضافة أول مشارك", "Add first participant");
    }

    private void ShowError(string message)
    {
        EditorError.Text = message;
        EditorError.IsVisible = true;
    }

    private static string LocalizeStatus(string value)
    {
        if (!LocalizationService.IsArabic) return value;
        return value switch
        {
            "Active" => "نشط",
            "Ready" => "جاهز",
            "InProgress" => "قيد التنفيذ",
            "Completed" => "مكتمل",
            "Running" => "قيد التشغيل",
            "Paused" => "متوقف مؤقتا",
            "Interrupted" => "متوقف",
            "Cancelled" => "ملغى",
            _ => value
        };
    }

    private static string StatusClass(string value) => value switch
    {
        "Active" or "Running" or "Completed" => "success",
        "Ready" => "ready",
        "Paused" => "paused",
        "Interrupted" => "interrupted",
        "Cancelled" => "cancelled",
        _ => "draft"
    };

    private static string Text(string arabic, string english) =>
        UiTextService.Localized(arabic, english);

    private sealed record Choice(string? Value, string Display)
    {
        public override string ToString() => Display;
    }
}
