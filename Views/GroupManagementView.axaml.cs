using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SOCYVIA.Models;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

public partial class GroupManagementView : UserControl
{
    public event EventHandler? GroupsChanged;

    private Study _study = new();
    private List<StudyGroup> _groups = new();
    private StudyGroup? _editingGroup;
    private bool _isSaving;

    private readonly FontFamily _englishFont =
        new("avares://SOCYVIA/Assets/Fonts#IBM Plex Sans");

    private readonly FontFamily _arabicFont =
        new("avares://SOCYVIA/Assets/Fonts#IBM Plex Sans Arabic");


    public GroupManagementView()
    {
        InitializeComponent();
        SetupEvents();
        ConfigureLanguage();
    }


    public GroupManagementView(
        Study study)
        : this()
    {
        _study = study;

        AttachedToVisualTree +=
            async (_, _) => await ReloadAsync();
    }


    public async Task ReloadAsync()
    {
        try
        {
            _groups =
                await GroupManagementService
                    .GetGroupsAsync(_study.Id);

            RenderGroups();
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Group workspace load error: {exception}");
        }
    }


    private void SetupEvents()
    {
        AddGroupButton.Click +=
            (_, _) => OpenEditor(null);

        CancelEditButton.Click +=
            (_, _) => CloseEditor();

        SaveGroupButton.Click +=
            async (_, _) => await SaveAsync();
    }


    private void OpenEditor(
        StudyGroup? group)
    {
        _editingGroup = group;

        EditorTitle.Text = group is null
            ? Text("مجموعة جديدة", "New group")
            : Text("تعديل المجموعة", "Edit group");

        NameBox.Text = group?.Name ?? string.Empty;
        DescriptionBox.Text = group?.Description ?? string.Empty;
        TargetSampleBox.Value = group?.TargetSampleSize;
        ControlGroupCheckBox.IsChecked =
            group?.IsControlGroup ?? false;
        ActiveGroupCheckBox.IsChecked =
            group?.IsActive ?? true;

        EditorErrorText.IsVisible = false;
        EditorPanel.IsVisible = true;
    }


    private void CloseEditor()
    {
        _editingGroup = null;
        EditorPanel.IsVisible = false;
        EditorErrorText.IsVisible = false;
    }


    private async Task SaveAsync()
    {
        if (_isSaving)
        {
            return;
        }

        _isSaving = true;
        SaveGroupButton.IsEnabled = false;
        EditorErrorText.IsVisible = false;

        try
        {
            int? targetSampleSize =
                TargetSampleBox.Value.HasValue
                    ? decimal.ToInt32(
                        TargetSampleBox.Value.Value)
                    : null;

            if (_editingGroup is null)
            {
                await GroupManagementService
                    .CreateGroupAsync(
                        _study.Id,
                        NameBox.Text ?? string.Empty,
                        DescriptionBox.Text,
                        targetSampleSize,
                        ControlGroupCheckBox.IsChecked == true,
                        ActiveGroupCheckBox.IsChecked == true);
            }
            else
            {
                var updated =
                    new StudyGroup
                    {
                        Id = _editingGroup.Id,
                        StudyId = _editingGroup.StudyId,
                        Name = NameBox.Text ?? string.Empty,
                        Description = DescriptionBox.Text,
                        ColorHex = _editingGroup.ColorHex,
                        IsControlGroup =
                            ControlGroupCheckBox.IsChecked == true,
                        SortOrder = _editingGroup.SortOrder,
                        TargetSampleSize = targetSampleSize,
                        IsActive =
                            ActiveGroupCheckBox.IsChecked == true,
                        CreatedAtUtc = _editingGroup.CreatedAtUtc,
                        UpdatedAtUtc = _editingGroup.UpdatedAtUtc
                    };

                await GroupManagementService
                    .UpdateGroupAsync(updated);
            }

            CloseEditor();
            await ReloadAsync();
            GroupsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Group save error: {exception}");

            EditorErrorText.Text = LocalizationService.IsArabic
                ? "تعذر حفظ المجموعة. تحقق من الاسم وحجم العينة."
                : exception.Message;

            EditorErrorText.IsVisible = true;
        }
        finally
        {
            _isSaving = false;
            SaveGroupButton.IsEnabled = true;
        }
    }


    private void RenderGroups()
    {
        GroupsContainer.Children.Clear();
        EmptyText.IsVisible = _groups.Count == 0;

        foreach (var group in _groups)
        {
            GroupsContainer.Children.Add(
                BuildGroupRow(group));
        }
    }


    private Control BuildGroupRow(
        StudyGroup group)
    {
        var name =
            new TextBlock
            {
                Text = group.Name,
                FontFamily = CurrentFont,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush("#203451")
            };

        var details =
            new TextBlock
            {
                Text = BuildDetails(group),
                FontFamily = CurrentFont,
                FontSize = 8.3,
                Foreground = Brush("#7E899A"),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520
            };

        ApplyReadingDirection(name);
        ApplyReadingDirection(details);

        var information =
            new StackPanel
            {
                Spacing = 3,
                VerticalAlignment = VerticalAlignment.Center
            };

        information.Children.Add(name);
        information.Children.Add(details);

        var actions =
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                VerticalAlignment = VerticalAlignment.Center
            };

        actions.Children.Add(
            ActionButton(
                Text("تعديل", "Edit"),
                (_, _) => OpenEditor(group)));

        actions.Children.Add(
            ActionButton(
                Text("لأعلى", "Move up"),
                async (_, _) =>
                    await MoveAsync(group, -1)));

        actions.Children.Add(
            ActionButton(
                Text("لأسفل", "Move down"),
                async (_, _) =>
                    await MoveAsync(group, 1)));

        actions.Children.Add(
            ActionButton(
                Text("حذف", "Delete"),
                async (_, _) =>
                    await DeleteAsync(group)));

        var color =
            new Border
            {
                Width = 8,
                MinHeight = 56,
                CornerRadius = new CornerRadius(4),
                Background = Brush(
                    group.ColorHex ?? "#2563EB"),
                VerticalAlignment = VerticalAlignment.Stretch
            };

        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("Auto,12,*,12,Auto")
            };

        Grid.SetColumn(color, 0);
        Grid.SetColumn(information, 2);
        Grid.SetColumn(actions, 4);

        grid.Children.Add(color);
        grid.Children.Add(information);
        grid.Children.Add(actions);

        return new Border
        {
            Padding = new Thickness(13, 10),
            Background = Brush(
                group.IsActive
                    ? "#FBFCFE"
                    : "#F4F5F8"),
            BorderBrush = Brush("#E3E9F3"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = grid,
            Opacity = group.IsActive ? 1 : 0.72
        };
    }


    private string BuildDetails(
        StudyGroup group)
    {
        var parts =
            new List<string>();

        if (group.IsControlGroup)
        {
            parts.Add(Text("مجموعة ضابطة", "Control group"));
        }

        if (!group.IsActive)
        {
            parts.Add(Text("غير نشطة", "Inactive"));
        }

        parts.Add(group.TargetSampleSize.HasValue
            ? Text(
                $"العينة المستهدفة: {group.TargetSampleSize}",
                $"Target sample: {group.TargetSampleSize}")
            : Text(
                "العينة المستهدفة: غير محددة",
                "Target sample: not set"));

        if (!string.IsNullOrWhiteSpace(group.Description))
        {
            parts.Add(group.Description);
        }

        return string.Join("  •  ", parts);
    }


    private async Task MoveAsync(
        StudyGroup group,
        int direction)
    {
        try
        {
            await GroupManagementService
                .MoveGroupAsync(
                    _study.Id,
                    group.Id,
                    direction);

            await ReloadAsync();
            GroupsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Group reorder error: {exception}");
        }
    }


    private async Task DeleteAsync(
        StudyGroup group)
    {
        try
        {
            var usage =
                await Repositories.GroupRepository
                    .GetUsageAsync(group.Id);

            if (usage.HasAnyUsage)
            {
                var deactivate =
                    await ConfirmAsync(
                        Text(
                            "تعطيل المجموعة",
                            "Deactivate group"),
                        Text(
                            "هذه المجموعة مرتبطة ببيانات بحثية أو إعدادات تجربة ولا يمكن حذفها بأمان. هل تريد تعطيلها بدلا من ذلك؟",
                            "This group is linked to research data or experiment configuration and cannot be deleted safely. Deactivate it instead?"),
                        Text("تعطيل", "Deactivate"));

                if (!deactivate)
                {
                    return;
                }

                group.IsActive = false;
                group.IsControlGroup = false;

                await GroupManagementService
                    .UpdateGroupAsync(group);
            }
            else
            {
                var confirmed =
                    await ConfirmAsync(
                        Text("حذف المجموعة", "Delete group"),
                        Text(
                            "هذه المجموعة غير مستخدمة. هل تريد حذفها نهائيا؟",
                            "This group is unused. Delete it permanently?"),
                        Text("حذف", "Delete"));

                if (!confirmed)
                {
                    return;
                }

                var result =
                    await GroupManagementService
                        .DeleteGroupIfUnusedAsync(group.Id);

                if (!result.WasDeleted)
                {
                    return;
                }
            }

            CloseEditor();
            await ReloadAsync();
            GroupsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Group delete error: {exception}");
        }
    }


    private async Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return false;
        }

        var dialog =
            new Window
            {
                Title = title,
                Background = Brush("#F7F9FD")
            };

        WindowAppearanceService.ConfigureCompactDialog(
            dialog,
            440,
            230);

        var messageBlock =
            new TextBlock
            {
                Text = message,
                FontFamily = CurrentFont,
                FontSize = 10,
                Foreground = Brush("#31435F"),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = LocalizationService.IsArabic
                    ? TextAlignment.Right
                    : TextAlignment.Left
            };

        var cancel = DialogButton(
            Text("إلغاء", "Cancel"));

        var confirm = DialogButton(confirmText);
        confirm.Background = Brush("#2563EB");
        confirm.Foreground = Brush("#FFFFFF");

        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);

        var actions =
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 8
            };

        actions.Children.Add(cancel);
        actions.Children.Add(confirm);

        var content =
            new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 22,
                FlowDirection = LocalizationService.IsArabic
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight
            };

        content.Children.Add(messageBlock);
        content.Children.Add(actions);
        dialog.Content = content;

        return await dialog.ShowDialog<bool>(owner);
    }


    private Button ActionButton(
        string text,
        EventHandler<Avalonia.Interactivity.RoutedEventArgs> handler)
    {
        var button =
            new Button
            {
                Content = text,
                FontFamily = CurrentFont,
                FontSize = 8,
                MinWidth = 38,
                MinHeight = 28,
                Padding = new Thickness(8, 4),
                Background = Brush("#F6F7FB"),
                Foreground = Brush("#31435F"),
                BorderBrush = Brush("#DCE3EE"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                HorizontalContentAlignment =
                    HorizontalAlignment.Center
            };

        button.Click += handler;
        return button;
    }


    private Button DialogButton(
        string text)
    {
        return new Button
        {
            Content = text,
            FontFamily = CurrentFont,
            FontSize = 9,
            MinWidth = 90,
            MinHeight = 34,
            Background = Brush("#FFFFFF"),
            Foreground = Brush("#31435F"),
            BorderBrush = Brush("#DCE3EE"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
    }


    private void ConfigureLanguage()
    {
        var isArabic =
            LocalizationService.IsArabic;

        RootGroupManagement.FlowDirection = isArabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        RootGroupManagement.FontFamily = CurrentFont;

        PageTitle.Text = Text("مجموعات الدراسة", "Study groups");
        PageSubtitle.Text = Text(
            "أنشئ مجموعات المشاركين وحدد العينة والمجموعة الضابطة. المجموعة ليست شرطا تجريبيا؛ تدار الشروط منفصلة في تصميم التجربة.",
            "Create participant groups and configure sample targets and control status. A group is not an experimental condition; conditions are managed separately in Experiment Design.");
        AddGroupButtonText.Text = Text("إضافة مجموعة", "Add group");
        NameLabel.Text = Text("اسم المجموعة", "Group name");
        TargetLabel.Text = Text("العينة المستهدفة", "Target sample");
        DescriptionLabel.Text = Text("الوصف", "Description");
        ControlGroupCheckBox.Content = Text(
            "مجموعة ضابطة",
            "Control group");
        ActiveGroupCheckBox.Content = Text("نشطة", "Active");
        CancelEditButtonText.Text = Text("إلغاء", "Cancel");
        SaveGroupButtonText.Text = Text("حفظ", "Save");
        EmptyText.Text = Text(
            "لا توجد مجموعات في هذه الدراسة.",
            "This study has no groups yet.");

        NameBox.TextAlignment = isArabic
            ? TextAlignment.Right
            : TextAlignment.Left;
        DescriptionBox.TextAlignment = isArabic
            ? TextAlignment.Right
            : TextAlignment.Left;
        TargetSampleBox.HorizontalContentAlignment =
            HorizontalAlignment.Center;
    }


    private void ApplyReadingDirection(
        TextBlock textBlock)
    {
        textBlock.FlowDirection = LocalizationService.IsArabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        textBlock.TextAlignment = LocalizationService.IsArabic
            ? TextAlignment.Right
            : TextAlignment.Left;
    }


    private FontFamily CurrentFont =>
        LocalizationService.IsArabic
            ? _arabicFont
            : _englishFont;


    private static string Text(
        string arabic,
        string english)
    {
        return UiTextService.Localized(arabic, english);
    }


    private static SolidColorBrush Brush(
        string value)
    {
        return new SolidColorBrush(
            Color.Parse(value));
    }
}
