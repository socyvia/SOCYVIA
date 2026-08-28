using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using SOCYVIA.Models;
using SOCYVIA.Repositories;
using SOCYVIA.Services;

namespace SOCYVIA.Views;

public sealed class ParticipantQuestionnaireView : UserControl
{
    private readonly QuestionnaireAssignment _assignment;
    private readonly ExperimentSession? _session;
    private readonly bool _preview;
    private readonly Dictionary<Question,Control> _inputs=[];
    private readonly TextBlock _error=new(){Foreground=new SolidColorBrush(Color.Parse("#B8384F")),TextWrapping=TextWrapping.Wrap,IsVisible=false};
    private readonly Button _complete=new(){Classes={"participantPrimary"},HorizontalAlignment=HorizontalAlignment.Center};
    private readonly DateTime _started=DateTime.UtcNow;
    private readonly Stopwatch _elapsed=Stopwatch.StartNew();

    public ParticipantQuestionnaireView(QuestionnaireAssignment assignment,ExperimentSession? session,bool preview=false)
    {
        _assignment=assignment;_session=session;_preview=preview;
        var version=assignment.Version??throw new ArgumentException("Questionnaire version is required.");
        var questionnaire=assignment.Questionnaire??throw new ArgumentException("Questionnaire metadata is required.");
        var arabic=LocalizationService.IsArabic;
        FlowDirection=arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        FontFamily=new FontFamily(arabic?"avares://SOCYVIA/Assets/Fonts#IBM Plex Sans Arabic":"avares://SOCYVIA/Assets/Fonts#IBM Plex Sans");
        var panel=new StackPanel{Spacing=14,Margin=new Thickness(24),MaxWidth=720,HorizontalAlignment=HorizontalAlignment.Stretch};
        panel.Children.Add(new TextBlock{Text=questionnaire.Title,FontSize=20,FontWeight=FontWeight.SemiBold,Foreground=Brush("#1D2D46"),TextWrapping=TextWrapping.Wrap,TextAlignment=arabic?TextAlignment.Right:TextAlignment.Left});
        if(!string.IsNullOrWhiteSpace(questionnaire.Description))panel.Children.Add(new TextBlock{Text=questionnaire.Description,FontSize=10,Foreground=Brush("#647188"),TextWrapping=TextWrapping.Wrap,TextAlignment=arabic?TextAlignment.Right:TextAlignment.Left});
        if(preview)panel.Children.Add(new Border{Background=Brush("#EEF4FF"),Padding=new Thickness(10,6),CornerRadius=new CornerRadius(7),Child=new TextBlock{Text=T("معاينة فقط، ولا يتم حفظ الاستجابات","PREVIEW ONLY — responses are not saved"),Foreground=Brush("#2559B8"),FontWeight=FontWeight.SemiBold,TextAlignment=TextAlignment.Center}});
        foreach(var section in version.Sections.OrderBy(item=>item.SortOrder))
        {
            panel.Children.Add(new TextBlock{Text=section.Title,FontSize=13,FontWeight=FontWeight.SemiBold,Margin=new Thickness(0,8,0,0),TextAlignment=arabic?TextAlignment.Right:TextAlignment.Left});
            foreach(var question in version.Questions.Where(item=>item.SectionId==section.Id).OrderBy(item=>item.SortOrder))panel.Children.Add(CreateQuestion(question));
        }
        foreach(var question in version.Questions.Where(item=>item.SectionId is null||version.Sections.All(section=>section.Id!=item.SectionId)).OrderBy(item=>item.SortOrder))panel.Children.Add(CreateQuestion(question));
        panel.Children.Add(_error);_complete.Content=preview?T("إنهاء المعاينة","Complete preview"):T("حفظ ومتابعة","Save and continue");_complete.Click+=async(_,_)=>await CompleteAsync();panel.Children.Add(_complete);
        Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }

    public event Action<QuestionnaireResponse?>? Completed;

    private Control CreateQuestion(Question question)
    {
        var arabic=LocalizationService.IsArabic;var panel=new StackPanel{Spacing=7};
        panel.Children.Add(new TextBlock{Text=question.QuestionText+(question.IsRequired?" *":""),FontSize=10.5,FontWeight=FontWeight.Medium,Foreground=Brush("#273850"),TextWrapping=TextWrapping.Wrap,TextAlignment=arabic?TextAlignment.Right:TextAlignment.Left});
        Control input;
        if(question.QuestionType is QuestionnaireQuestionTypes.Likert or QuestionnaireQuestionTypes.SingleChoice or QuestionnaireQuestionTypes.YesNo)
        {
            var options=Options(question);var combo=new ComboBox{ItemsSource=options.Select(item=>item.DisplayLabel).ToArray(),MinWidth=220,HorizontalAlignment=HorizontalAlignment.Center,HorizontalContentAlignment=HorizontalAlignment.Center};input=combo;
        }
        else if(question.QuestionType==QuestionnaireQuestionTypes.MultipleChoice)
        {
            var choices=new StackPanel{Spacing=5};foreach(var option in Options(question))choices.Children.Add(new CheckBox{Content=option.DisplayLabel,Tag=option});input=choices;
        }
        else
        {
            input=new TextBox{MinWidth=260,MinHeight=question.QuestionType==QuestionnaireQuestionTypes.LongText?90:36,AcceptsReturn=question.QuestionType==QuestionnaireQuestionTypes.LongText,TextWrapping=TextWrapping.Wrap,TextAlignment=arabic?TextAlignment.Right:TextAlignment.Left};
        }
        _inputs[question]=input;panel.Children.Add(input);return new Border{Background=Brush("#FFFFFF"),BorderBrush=Brush("#DCE3ED"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(9),Padding=new Thickness(14,12),Child=panel};
    }

    private async Task CompleteAsync()
    {
        if(!_complete.IsEnabled)return;_error.IsVisible=false;var answers=new List<QuestionResponse>();
        foreach(var entry in _inputs)
        {
            var answer=Read(entry.Key,entry.Value);
            if(entry.Key.IsRequired&&answer is null){_error.Text=T("يرجى الإجابة عن جميع البنود المطلوبة","Please answer every required item.");_error.IsVisible=true;return;}
            if(answer is not null)answers.Add(answer);
        }
        if(_preview){Completed?.Invoke(null);return;}
        if(_session is null||_assignment.Questionnaire is null){_error.Text=T("تعذر ربط الاستبيان بالجلسة","The questionnaire could not be linked to the session.");_error.IsVisible=true;return;}
        _complete.IsEnabled=false;
        try
        {
            var response=new QuestionnaireResponse{AssignmentId=_assignment.Id,StudyId=_session.StudyId,SessionId=_session.Id,ParticipantId=_session.ParticipantId,QuestionnaireId=_assignment.Questionnaire.Id,QuestionnaireVersionId=_assignment.QuestionnaireVersionId,StartedAtUtc=_started,DurationMilliseconds=_elapsed.ElapsedMilliseconds,IsDemo=DemoAccessPolicy.IsDemoStudy(new Study{Id=_session.StudyId,MetadataJson=_session.MetadataJson}),MetadataJson=JsonSerializer.Serialize(new{Placement=_assignment.Placement,QuestionnaireStarted=_started,QuestionnaireCompleted=DateTime.UtcNow})};
            var saved=await QuestionnaireRepository.SaveCompletedResponseAsync(response,answers);Completed?.Invoke(saved);
        }
        catch(Exception exception){ApplicationDiagnosticsService.LogException(exception,"Complete participant questionnaire");_error.Text=T("تعذر حفظ الاستجابات بأمان","Responses could not be saved safely.");_error.IsVisible=true;_complete.IsEnabled=true;}
    }

    private QuestionResponse? Read(Question question,Control control)
    {
        if(control is ComboBox combo)
        {
            if(combo.SelectedIndex<0)return null;var option=Options(question)[combo.SelectedIndex];return new QuestionResponse{QuestionId=question.Id,RawValue=option.ValueCode,NumericValue=option.NumericCode,SelectedOptionIdsJson=JsonSerializer.Serialize(new[]{option.Id})};
        }
        if(control is StackPanel choices)
        {
            var selected=choices.Children.OfType<CheckBox>().Where(item=>item.IsChecked==true).Select(item=>(QuestionOption)item.Tag!).ToArray();if(selected.Length==0)return null;return new QuestionResponse{QuestionId=question.Id,RawValue=string.Join('|',selected.Select(item=>item.ValueCode)),SelectedOptionIdsJson=JsonSerializer.Serialize(selected.Select(item=>item.Id))};
        }
        if(control is TextBox textBox)
        {
            var value=textBox.Text?.Trim();if(string.IsNullOrWhiteSpace(value))return null;double? numeric=null;if(question.QuestionType==QuestionnaireQuestionTypes.Numeric){if(!double.TryParse(value,NumberStyles.Float,CultureInfo.CurrentCulture,out var parsed)&&!double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out parsed)){_error.Text=T("أدخل قيمة رقمية صحيحة","Enter a valid numeric value.");_error.IsVisible=true;return null;}numeric=parsed;}return new QuestionResponse{QuestionId=question.Id,RawValue=value,NumericValue=numeric};
        }
        return null;
    }

    private static List<QuestionOption> Options(Question question)=>question.Options.Count>0?question.Options.OrderBy(item=>item.SortOrder).ToList():question.QuestionType==QuestionnaireQuestionTypes.YesNo?[new QuestionOption{Id=$"{question.Id}-yes",QuestionId=question.Id,ValueCode="YES",NumericCode=1,DisplayLabel=T("نعم","Yes")},new QuestionOption{Id=$"{question.Id}-no",QuestionId=question.Id,ValueCode="NO",NumericCode=0,DisplayLabel=T("لا","No")}]:[];
    private static string T(string arabic,string english)=>UiTextService.Localized(arabic,english);
    private static SolidColorBrush Brush(string value)=>new(Color.Parse(value));
}
