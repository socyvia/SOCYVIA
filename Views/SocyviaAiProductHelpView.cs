using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SOCYVIA.Models;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

/// <summary>Product-wide SOCYVIA AI workspace used when no study is open.</summary>
public sealed class SocyviaAiProductHelpView : UserControl
{
    private const string ConversationKey = "socyvia-product-help";
    private readonly AiConversationService _store = new();
    private readonly StackPanel _messages = new() { Spacing = 8 };
    private readonly TextBox _input = new() { AcceptsReturn = true, MinHeight = 76, MaxHeight = 150, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _state = new() { Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap };
    private readonly Button _send = new() { Content = "Send", Classes = { "ai" } };
    private AiStudyConversation _conversation;
    private SocyviaAiServiceStatus _serviceStatus = new(SocyviaAiServiceState.Connecting, "Connecting");
    private bool Ar => LocalizationService.IsArabic;
    private string T(string ar, string en) => Ar ? ar : en;

    public SocyviaAiProductHelpView()
    {
        FlowDirection = Ar ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight;
        _conversation = _store.New(ConversationKey, "socyvia-product-help-v1");
        var root = new StackPanel { Name = "SocyviaAiProductHelpWorkspaceRoot", Spacing = 14, Margin = new Thickness(4) };
        root.Children.Add(new TextBlock { Text = "SOCYVIA AI", Classes = { "pageTitle" } });
        root.Children.Add(new TextBlock
        {
            Text = T("مساعد SOCYVIA لإرشادك داخل المنتج والإجابة عن أسئلتك بشأن خطوات العمل الفعلية.",
                "Your SOCYVIA assistant for guidance through the real product workflow."),
            Classes = { "metadata" }, TextWrapping = TextWrapping.Wrap
        });

        var prompts = new WrapPanel { Name = "SocyviaAiProductSuggestedPrompts", Orientation = Orientation.Horizontal };
        foreach (var prompt in SocyviaAiUiCopy.ProductHelpPrompts.Select(item => T(item.Arabic, item.English)))
        {
            var button = new Button { Content = prompt, Margin = new Thickness(0, 0, 8, 8), Classes = { "subtle" } };
            button.Click += (_, _) => { _input.Text = prompt; _input.Focus(); _input.CaretIndex = prompt.Length; };
            prompts.Children.Add(button);
        }
        root.Children.Add(new Border
        {
            Classes = { "aiContextCard" }, Padding = new Thickness(12),
            Child = new StackPanel { Spacing = 7, Children = { new TextBlock { Text = T(SocyviaAiUiCopy.ArabicSuggestedQuestionsTitle, "Suggested questions"), Classes = { "sectionTitle" } }, prompts } }
        });
        root.Children.Add(new Border
        {
            MinHeight = 300, Classes = { "workspaceCard" }, Padding = new Thickness(12),
            Child = new ScrollViewer { Content = _messages, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto }
        });
        var newConversation = new Button { Content = T("محادثة جديدة", "New Conversation") };
        var clear = new Button { Content = T("مسح المحادثة", "Clear Conversation") };
        newConversation.Click += async (_, _) =>
        {
            _conversation = _store.New(ConversationKey, "socyvia-product-help-v1");
            await _store.SaveAsync(_conversation); RenderMessages();
        };
        clear.Click += async (_, _) =>
        {
            await _store.ClearAsync(ConversationKey);
            _conversation = _store.New(ConversationKey, "socyvia-product-help-v1"); RenderMessages();
        };
        _send.Click += async (_, _) => await SendAsync();
        _input.KeyDown += async (_, eventArgs) =>
        {
            if (eventArgs.Key != Key.Enter || eventArgs.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
            eventArgs.Handled = true; await SendAsync();
        };
        root.Children.Add(new Border
        {
            Name = "SocyviaAiProductComposer", Classes = { "aiContextCard" }, Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 8,
                Children = { _input, new WrapPanel { Children = { _send, newConversation, clear } }, _state }
            }
        });
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        AttachedToVisualTree += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _conversation = await _store.GetOrCreateAsync(ConversationKey, "socyvia-product-help-v1");
        _serviceStatus = await SocyviaAiService.GetStatusAsync();
        var ready = _serviceStatus.State == SocyviaAiServiceState.Ready;
        _input.IsEnabled = ready; _send.IsEnabled = ready;
        _send.Content = T("إرسال", "Send");
        _input.PlaceholderText = ready ? T("اسأل عن SOCYVIA...", "Ask about SOCYVIA...") : SocyviaAiStatusPresentationService.Detail(_serviceStatus, Ar);
        _state.Text = ready ? T("SOCYVIA AI جاهز", "SOCYVIA AI is ready") : SocyviaAiStatusPresentationService.Detail(_serviceStatus, Ar);
        RenderMessages();
    }

    private async Task SendAsync()
    {
        var prompt = _input.Text?.Trim();
        if (string.IsNullOrWhiteSpace(prompt) || _serviceStatus.State != SocyviaAiServiceState.Ready) return;
        _send.IsEnabled = false; _input.IsEnabled = false;
        _state.Text = T("جار إعداد الإجابة...", "Preparing an answer...");
        var researcher = new AiConversationMessage("researcher", prompt, DateTime.UtcNow);
        var working = _conversation.Messages.Concat([researcher]).TakeLast(24).ToArray();
        try
        {
            var applicationState = await SocyviaAiApplicationContextService.WithoutStudyAsync("SOCYVIA AI");
            var request = ResearchInterpretationService.BuildProductHelpRequest(prompt, applicationState, working);
            if (!AiConversationService.IsAggregateSafe(request)) throw new InvalidOperationException("Unsafe AI context blocked.");
            var provider = await ResearchInterpretationProviderFactory.CreateConfiguredAsync();
            if (provider is null) { _state.Text = SocyviaAiStatusPresentationService.Detail(_serviceStatus, Ar); return; }
            var response = await ResearchInterpretationService.InterpretAsync(request, provider);
            _conversation = _conversation with
            {
                UpdatedAtUtc = DateTime.UtcNow, Provider = provider.ProviderName,
                Messages = working.Concat([new AiConversationMessage("assistant", response.Interpretation ?? T("لا توجد إجابة.", "No answer was returned."), DateTime.UtcNow)]).ToArray()
            };
            await _store.SaveAsync(_conversation); _input.Text = string.Empty; _state.Text = string.Empty; RenderMessages();
        }
        catch (SocyviaAiRateLimitException) { _state.Text = T("تم بلوغ حد السعة مؤقتا. حاول لاحقا.", "Temporary capacity reached. Try again later."); }
        catch (Exception exception)
        {
            ApplicationDiagnosticsService.LogException(exception, "SOCYVIA AI product help");
            _state.Text = T("تعذر إكمال الإجابة. لم يتم تعديل أي بيانات بحثية.", "The answer could not be completed. No research data was changed.");
        }
        finally { _send.IsEnabled = true; _input.IsEnabled = true; }
    }

    private void RenderMessages()
    {
        _messages.Children.Clear();
        if (_conversation.Messages.Count == 0)
        {
            _messages.Children.Add(new TextBlock { Text = T("اكتب أي سؤال عن استخدام SOCYVIA.", "Ask any question about using SOCYVIA."), Classes = { "metadata" } });
            return;
        }
        foreach (var message in _conversation.Messages)
            _messages.Children.Add(new Border
            {
                Classes = { message.Role == "researcher" ? "evidenceCard" : "aiContextCard" }, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 4),
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children = { new TextBlock { Text = message.Role == "researcher" ? T("الباحث", "Researcher") : "SOCYVIA AI", FontWeight = Avalonia.Media.FontWeight.SemiBold }, new TextBlock { Text = message.Content, TextWrapping = TextWrapping.Wrap } }
                }
            });
    }
}
