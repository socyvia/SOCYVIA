using System;
using System.Collections.Generic;
using System.Linq;
using SOCYVIA.Models;
using SOCYVIA.Services;

var tests = new List<(string Name, Action Run)>
{
    ("Descriptive statistics", Descriptive),
    ("Likert scoring and reverse coding", LikertScoring),
    ("Missing scale items", MissingScaleItems),
    ("Independent t-test", IndependentT),
    ("Welch t-test", WelchT),
    ("Paired t-test", PairedT),
    ("Mann-Whitney U", MannWhitney),
    ("Wilcoxon signed-rank", Wilcoxon),
    ("One-way ANOVA", Anova),
    ("Kruskal-Wallis", KruskalWallis),
    ("Chi-square and Fisher exact", Categorical),
    ("Pearson and Spearman correlations", Correlations),
    ("Confidence interval", ConfidenceInterval),
    ("Multiple-comparison corrections", MultipleComparisons),
    ("Edge cases", EdgeCases),
    ("Analysis decision transparency", DecisionEngine),
    ("Synthetic generator reproducibility", DemoReproducibility)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS  {test.Name}"); }
    catch (Exception exception) { failures.Add($"{test.Name}: {exception.Message}"); Console.WriteLine($"FAIL  {test.Name}: {exception.Message}"); }
}
Console.WriteLine($"\n{tests.Count - failures.Count}/{tests.Count} scientific validation groups passed");
if (failures.Count > 0) { foreach (var failure in failures) Console.Error.WriteLine(failure); return 1; }
return 0;

static void Descriptive()
{
    var result=ScientificStatistics.Describe(new double?[]{1,2,3,4,5,null});
    Equal(5,result.N);Equal(1,result.Missing);Near(3,result.Mean);Near(Math.Sqrt(2.5),result.StandardDeviation);Near(3,result.Median);Near(2,result.Q1);Near(4,result.Q3);Near(2,result.Iqr);
}

static void LikertScoring()
{
    Near(5,QuestionnaireScoringService.ReverseScore(1,1,5));Near(1,QuestionnaireScoringService.ReverseScore(5,1,5));
    var scale=new QuestionnaireScale{Id="s",VariableName="score",ScoringMethod="MEAN",MinimumAnsweredItems=2,Items=[new QuestionnaireScaleItem{QuestionId="q1",IsReverseCoded=true,ReverseMinimum=1,ReverseMaximum=5},new QuestionnaireScaleItem{QuestionId="q2"}]};
    var result=QuestionnaireScoringService.Score(scale,new Dictionary<string,double?>{{"q1",1},{"q2",5}});Near(5,result.Score);True(result.IsComplete);
}

static void MissingScaleItems()
{
    var scale=new QuestionnaireScale{Id="s",VariableName="score",ScoringMethod="SUM",MinimumAnsweredItems=2,Items=[new QuestionnaireScaleItem{QuestionId="q1"},new QuestionnaireScaleItem{QuestionId="q2"}]};
    var result=QuestionnaireScoringService.Score(scale,new Dictionary<string,double?>{{"q1",3}});True(result.Score is null);True(!result.IsComplete);
}

static void IndependentT()
{
    var result=ScientificStatistics.IndependentTTest([1,2,3,4,5],[2,4,6,8,10]);Near(-1.897366596,result.Statistic,1e-8);Near(8,result.DegreesOfFreedom);Near(-1.2,result.EffectSize?.Value,1e-10);Near(0.09435,result.PValue,2e-4);
}

static void WelchT()
{
    var result=ScientificStatistics.WelchTTest([1,2,3,4,5],[2,4,6,8,10]);Near(-1.897366596,result.Statistic,1e-8);Near(5.882352941,result.DegreesOfFreedom,1e-8);Near(0.108,result.PValue,0.002);
}

static void PairedT()
{
    var result=ScientificStatistics.PairedTTest([10,12,14,16,18],[8,11,13,15,15]);Near(4,result.Statistic,1e-10);Near(4,result.DegreesOfFreedom);Near(0.01613,result.PValue,2e-4);Near(1.788854382,result.EffectSize?.Value,1e-8);
}

static void MannWhitney()
{
    var result=ScientificStatistics.MannWhitneyU([1,2,3],[4,5,6]);Near(0,result.Statistic);Near(1,result.EffectSize?.Value);Near(0.08086,result.PValue,3e-4);
}

static void Wilcoxon()
{
    var result=ScientificStatistics.WilcoxonSignedRank([1,2,3,4,5],[0,1,1,2,2]);Near(0,result.Statistic);Near(0,result.ResultData["WMinus"]);True(result.PValue is > 0 and < 0.1);
}

static void Anova()
{
    var result=ScientificStatistics.OneWayAnova([[1,2,3],[4,5,6],[7,8,9]]);Near(27,result.Statistic,1e-10);Near(2,result.DegreesOfFreedom);Near(6,result.SecondaryDegreesOfFreedom);Near(0.9,result.EffectSize?.Value);Near(0.001,result.PValue,0.0002);
}

static void KruskalWallis()
{
    var result=ScientificStatistics.KruskalWallis([[1,2,3],[4,5,6],[7,8,9]]);Near(7.2,result.Statistic,1e-10);Near(0.02732,result.PValue,2e-4);
}

static void Categorical()
{
    var table=new long[,]{{10,20},{20,10}};var chi=ScientificStatistics.ChiSquare(table);Near(6.666666667,chi.Statistic,1e-8);Near(0.009823,chi.PValue,2e-5);Near(1.0/3,chi.EffectSize?.Value,1e-10);
    var fisher=ScientificStatistics.FisherExact2X2(1,9,11,3);Near(0.002759,fisher.PValue,2e-5);
}

static void Correlations()
{
    var x=new double[]{1,2,3,4,5};var y=new double[]{2,4,5,4,5};var pearson=ScientificStatistics.PearsonCorrelation(x,y);Near(0.7745966692,pearson.Estimate,1e-9);True(pearson.ConfidenceInterval is not null);
    var spearman=ScientificStatistics.SpearmanCorrelation(x,y);Near(0.7378647874,spearman.Estimate,1e-9);
}

static void ConfidenceInterval()
{
    var result=ScientificStatistics.Describe(new double?[]{1,2,3,4,5});True(result.MeanConfidenceInterval is not null);Near(1.03676,result.MeanConfidenceInterval!.Lower,2e-4);Near(4.96324,result.MeanConfidenceInterval.Upper,2e-4);
}

static void MultipleComparisons()
{
    var holm=ScientificStatistics.AdjustPValues([0.01,0.04,0.03],"HOLM");Near(0.03,holm[0]);Near(0.06,holm[1]);Near(0.06,holm[2]);var bonf=ScientificStatistics.AdjustPValues([0.01,0.04,0.03],"BONFERRONI");Near(0.03,bonf[0]);Near(0.12,bonf[1]);Near(0.09,bonf[2]);
}

static void EdgeCases()
{
    var empty=ScientificStatistics.Describe(Array.Empty<double?>());Equal(0,empty.N);
    var constant=ScientificStatistics.PearsonCorrelation([1,1,1],[1,2,3]);Equal(AnalysisStatuses.InsufficientData,constant.Status);
    var tied=ScientificStatistics.MannWhitneyU([1,1],[1,1]);Equal(AnalysisStatuses.InsufficientData,tied.Status);
    var tooSmall=ScientificStatistics.IndependentTTest([1],[2]);Equal(AnalysisStatuses.InsufficientData,tooSmall.Status);
    var unequal=ScientificStatistics.WelchTTest([1,2,3],[2,3,4,5,6]);Equal(AnalysisStatuses.Computed,unequal.Status);
    Throws<ArgumentException>(()=>QuestionnaireScoringService.ReverseScore(3,5,1));Throws<ArgumentOutOfRangeException>(()=>QuestionnaireScoringService.ReverseScore(6,1,5));
}

static void DecisionEngine()
{
    var outcome=new AnalysisVariable{MeasurementLevel=MeasurementLevels.Continuous};var grouping=new AnalysisVariable{MeasurementLevel=MeasurementLevels.Nominal};var recommendation=AnalysisDecisionEngine.Recommend(outcome,grouping,3,false,60);Equal("ONE_WAY_ANOVA",recommendation.RecommendedMethod);var unsupported=AnalysisDecisionEngine.Recommend(outcome,grouping,3,true,60);Equal(AnalysisStatuses.UnsupportedDesign,unsupported.Status);
}

static void DemoReproducibility()
{
    var first=DemoScientificDataService.GenerateValidationSequence(DemoScientificDataService.GeneratorSeed,20);var second=DemoScientificDataService.GenerateValidationSequence(DemoScientificDataService.GeneratorSeed,20);True(first.SequenceEqual(second));True(first.Distinct().Count()>15);
}

static void Near(double expected,double? actual,double tolerance=1e-6){if(!actual.HasValue||double.IsNaN(actual.Value)||Math.Abs(expected-actual.Value)>tolerance)throw new Exception($"Expected {expected} ± {tolerance}, actual {actual}");}
static void Equal<T>(T expected,T actual){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"Expected {expected}, actual {actual}");}
static void True(bool value){if(!value)throw new Exception("Condition was false");}
static void Throws<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new Exception($"Expected {typeof(T).Name}");}
