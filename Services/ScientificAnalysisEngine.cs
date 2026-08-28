using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

public static class AnalysisDecisionEngine
{
    public static AnalysisRecommendation Recommend(
        AnalysisVariable outcome,
        AnalysisVariable? predictor,
        int groupCount,
        bool repeatedMeasures,
        int availableN)
    {
        if (availableN < 2)
            return new AnalysisRecommendation { Status=AnalysisStatuses.InsufficientData,Warnings=["At least two usable observations are required."] };
        if (repeatedMeasures && groupCount > 2)
            return new AnalysisRecommendation { Status=AnalysisStatuses.UnsupportedDesign,Warnings=["Repeated-measures designs with more than two measurements require a future mixed/repeated-measures engine."] };
        if (predictor is null)
            return new AnalysisRecommendation{RecommendedFamily="DESCRIPTIVE",RecommendedMethod="DESCRIPTIVE",Rationale=["No predictor or grouping variable is selected."],Requirements=["A defined measurement level and transparent missing-data handling."]};
        if (outcome.MeasurementLevel is MeasurementLevels.Nominal or MeasurementLevels.Binary)
            return new AnalysisRecommendation{RecommendedFamily="CATEGORICAL_ASSOCIATION",RecommendedMethod="CHI_SQUARE",Alternatives=["FISHER_EXACT for a sparse 2x2 table"],Rationale=["Both outcome and predictor are categorical."],Requirements=["Independent observations and inspection of expected cell counts."]};
        if (predictor.MeasurementLevel==MeasurementLevels.Continuous)
            return new AnalysisRecommendation{RecommendedFamily="ASSOCIATION",RecommendedMethod=outcome.MeasurementLevel==MeasurementLevels.Ordinal?"SPEARMAN_RHO":"PEARSON_R",Alternatives=["SPEARMAN_RHO"],Rationale=["The predictor is numeric and the objective is association."],Requirements=["Paired observations; inspect form, extreme values, and measurement level."]};
        if (groupCount==2&&repeatedMeasures)
            return new AnalysisRecommendation{RecommendedFamily="PAIRED_COMPARISON",RecommendedMethod=outcome.MeasurementLevel==MeasurementLevels.Ordinal?"WILCOXON_SIGNED_RANK":"PAIRED_T",Alternatives=["WILCOXON_SIGNED_RANK","PAIRED_T"],Rationale=["Two measurements are paired within the same unit."],Requirements=["Correct pair linkage and inspection of difference scores."]};
        if(groupCount==2)
            return new AnalysisRecommendation{RecommendedFamily="TWO_INDEPENDENT_GROUPS",RecommendedMethod=outcome.MeasurementLevel==MeasurementLevels.Ordinal?"MANN_WHITNEY_U":"WELCH_T",Alternatives=["INDEPENDENT_T","MANN_WHITNEY_U"],Rationale=["Two independent groups are present.","Welch's method is the default numeric comparison because it does not require equal variances."],Requirements=["Independent observations and sufficient variation in each group."],Warnings=availableN<10?["Very small samples produce unstable estimates and uncertainty."]:[]};
        if(groupCount>2)
            return new AnalysisRecommendation{RecommendedFamily="MULTIPLE_INDEPENDENT_GROUPS",RecommendedMethod=outcome.MeasurementLevel==MeasurementLevels.Ordinal?"KRUSKAL_WALLIS":"ONE_WAY_ANOVA",Alternatives=["KRUSKAL_WALLIS"],Rationale=["More than two independent groups are present."],Requirements=["Inspect group distributions, sample sizes, variance structure, and any planned multiplicity correction."]};
        return new AnalysisRecommendation{Status=AnalysisStatuses.InvalidConfiguration,Warnings=["The selected design does not define a supported comparison."]};
    }
}

public static class ScientificAnalysisEngine
{
    public const string EngineVersion = ScientificEngineMetadata.Version;

    public static Task<AnalysisExecution> ExecuteAsync(
        AnalysisDataset dataset,
        AnalysisSpecification specification,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Execute(dataset, specification), cancellationToken);

    public static AnalysisExecution Execute(AnalysisDataset dataset, AnalysisSpecification specification)
    {
        var execution = new AnalysisExecution
        {
            AnalysisSpecificationId=specification.Id,StudyId=specification.StudyId,
            DatasetHash=dataset.DatasetHash,DatasetDescriptorJson=JsonSerializer.Serialize(new{dataset.StudyId,Rows=dataset.Rows.Count,Variables=dataset.Variables.Select(item=>item.Id),dataset.IsDemo}),
            IsDemo=dataset.IsDemo,EngineVersion=EngineVersion
        };
        try
        {
            if(dataset.StudyId!=specification.StudyId)
                return Fail(execution,AnalysisStatuses.InvalidConfiguration,"DATASET_STUDY_MISMATCH","The dataset and specification belong to different studies.");
            var outcome=dataset.Variables.FirstOrDefault(item=>item.Id==specification.OutcomeVariableId);
            if(outcome is null)return Fail(execution,AnalysisStatuses.InvalidConfiguration,"OUTCOME_NOT_FOUND","The outcome variable does not exist in the frozen dataset.");
            var result=ExecuteMethod(dataset,specification,outcome);
            execution.Result=result;execution.Status=result.Status;execution.Diagnostics=result.Diagnostics;execution.Warnings=result.Warnings;
            return execution;
        }
        catch(Exception exception)
        {
            return Fail(execution,AnalysisStatuses.ComputationError,"COMPUTATION_FAILURE",exception.Message);
        }
    }

    private static StatisticalResult ExecuteMethod(AnalysisDataset dataset,AnalysisSpecification specification,AnalysisVariable outcome)
    {
        var method=specification.Method.ToUpperInvariant();
        if(method=="DESCRIPTIVE")
        {
            var values=NumericValues(dataset,specification.OutcomeVariableId);
            var descriptive=ScientificStatistics.Describe(values.Cast<double?>(),specification.ConfidenceLevel);
            return new StatisticalResult{Method="DESCRIPTIVE",N=descriptive.N,Estimate=descriptive.Mean,ConfidenceInterval=descriptive.MeanConfidenceInterval,ResultData=DescriptiveData(descriptive),Warnings=descriptive.N==0?["No usable values are available."]:[],Status=descriptive.N==0?AnalysisStatuses.InsufficientData:AnalysisStatuses.Computed,CanonicalSummary=outcome.MeasurementLevel==MeasurementLevels.Ordinal?"Ordinal item: emphasize frequencies, median, and IQR; the mean is retained only as an optional numeric descriptor.":"Numeric descriptive summary with explicit missingness and uncertainty."};
        }
        if(method is "PEARSON_R" or "SPEARMAN_RHO")
        {
            if(string.IsNullOrWhiteSpace(specification.PredictorVariableId))return Failure(method,"A numeric predictor is required.");
            var pairs=dataset.Rows.Select(row=>(X:row.NumericValues.GetValueOrDefault(specification.PredictorVariableId),Y:row.NumericValues.GetValueOrDefault(specification.OutcomeVariableId))).Where(pair=>pair.X.HasValue&&pair.Y.HasValue&&double.IsFinite(pair.X.Value)&&double.IsFinite(pair.Y.Value)).ToArray();
            return method=="PEARSON_R"?ScientificStatistics.PearsonCorrelation(pairs.Select(pair=>pair.X!.Value).ToArray(),pairs.Select(pair=>pair.Y!.Value).ToArray(),specification.ConfidenceLevel):ScientificStatistics.SpearmanCorrelation(pairs.Select(pair=>pair.X!.Value).ToArray(),pairs.Select(pair=>pair.Y!.Value).ToArray());
        }
        if(method=="PAIRED_T"||method=="WILCOXON_SIGNED_RANK")
        {
            if(string.IsNullOrWhiteSpace(specification.PredictorVariableId))return Failure(method,"A second paired measurement variable is required.");
            var pairs=dataset.Rows.Select(row=>(A:row.NumericValues.GetValueOrDefault(specification.OutcomeVariableId),B:row.NumericValues.GetValueOrDefault(specification.PredictorVariableId))).Where(pair=>pair.A.HasValue&&pair.B.HasValue).ToArray();
            return method=="PAIRED_T"?ScientificStatistics.PairedTTest(pairs.Select(pair=>pair.A!.Value).ToArray(),pairs.Select(pair=>pair.B!.Value).ToArray(),specification.ConfidenceLevel):ScientificStatistics.WilcoxonSignedRank(pairs.Select(pair=>pair.A!.Value).ToArray(),pairs.Select(pair=>pair.B!.Value).ToArray());
        }
        if(method is "CHI_SQUARE" or "FISHER_EXACT")return Categorical(dataset,specification,method);

        var grouped=GroupValues(dataset,specification);
        if(grouped.Count<2)return Failure(method,"At least two non-empty groups are required.");
        return method switch
        {
            "INDEPENDENT_T" when grouped.Count==2=>ScientificStatistics.IndependentTTest(grouped[0].Values,grouped[1].Values,specification.ConfidenceLevel),
            "WELCH_T" when grouped.Count==2=>ScientificStatistics.WelchTTest(grouped[0].Values,grouped[1].Values,specification.ConfidenceLevel),
            "MANN_WHITNEY_U" when grouped.Count==2=>ScientificStatistics.MannWhitneyU(grouped[0].Values,grouped[1].Values),
            "ONE_WAY_ANOVA"=>ScientificStatistics.OneWayAnova(grouped.Select(item=>(IReadOnlyList<double>)item.Values).ToArray()),
            "KRUSKAL_WALLIS"=>ScientificStatistics.KruskalWallis(grouped.Select(item=>(IReadOnlyList<double>)item.Values).ToArray()),
            _=>new StatisticalResult{Method=method,Status=AnalysisStatuses.UnsupportedDesign,Warnings=[$"Method '{method}' is not supported for {grouped.Count} groups."],CanonicalSummary="The requested design is not supported by the current engine."}
        };
    }

    private static StatisticalResult Categorical(AnalysisDataset dataset,AnalysisSpecification specification,string method)
    {
        if(string.IsNullOrWhiteSpace(specification.PredictorVariableId))return Failure(method,"A categorical predictor is required.");
        var pairs=dataset.Rows.Select(row=>(Outcome:CategoricalValue(row,specification.OutcomeVariableId),Predictor:CategoricalValue(row,specification.PredictorVariableId))).Where(pair=>pair.Outcome is not null&&pair.Predictor is not null).ToArray();
        var outcomes=pairs.Select(pair=>pair.Outcome!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var predictors=pairs.Select(pair=>pair.Predictor!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var table=new long[predictors.Length,outcomes.Length];foreach(var pair in pairs)table[Array.IndexOf(predictors,pair.Predictor!),Array.IndexOf(outcomes,pair.Outcome!)]++;
        if(method=="FISHER_EXACT")return predictors.Length==2&&outcomes.Length==2?ScientificStatistics.FisherExact2X2(table[0,0],table[0,1],table[1,0],table[1,1]):new StatisticalResult{Method=method,Status=AnalysisStatuses.UnsupportedDesign,Warnings=["Fisher exact is currently supported only for 2x2 tables."]};
        return ScientificStatistics.ChiSquare(table);
    }

    private static List<(string Group,List<double> Values)> GroupValues(AnalysisDataset dataset,AnalysisSpecification specification)
    {
        var predictor=specification.PredictorVariableId??"group";
        return dataset.Rows.Select(row=>(Group:CategoricalValue(row,predictor),Value:row.NumericValues.GetValueOrDefault(specification.OutcomeVariableId))).Where(item=>item.Group is not null&&item.Value.HasValue&&double.IsFinite(item.Value.Value)).GroupBy(item=>item.Group!,StringComparer.Ordinal).OrderBy(group=>group.Key,StringComparer.Ordinal).Select(group=>(group.Key,group.Select(item=>item.Value!.Value).ToList())).ToList();
    }

    private static string? CategoricalValue(AnalysisRow row,string id)=>id switch{"group"=>row.GroupName,"condition"=>row.ConditionName,_=>row.CategoricalValues.GetValueOrDefault(id)};
    private static double[] NumericValues(AnalysisDataset dataset,string id)=>dataset.Rows.Select(row=>row.NumericValues.GetValueOrDefault(id)).Where(value=>value.HasValue&&double.IsFinite(value.Value)).Select(value=>value!.Value).ToArray();
    private static IReadOnlyDictionary<string,double> DescriptiveData(NumericDescriptiveResult result)=>new Dictionary<string,double>{{"N",result.N},{"Missing",result.Missing},{"Mean",result.Mean??double.NaN},{"StandardDeviation",result.StandardDeviation??double.NaN},{"Median",result.Median??double.NaN},{"Minimum",result.Minimum??double.NaN},{"Maximum",result.Maximum??double.NaN},{"Q1",result.Q1??double.NaN},{"Q3",result.Q3??double.NaN},{"IQR",result.Iqr??double.NaN}};
    private static StatisticalResult Failure(string method,string warning)=>new(){Method=method,Status=AnalysisStatuses.InsufficientData,Warnings=[warning],CanonicalSummary=warning};
    private static AnalysisExecution Fail(AnalysisExecution execution,string status,string code,string detail){execution.Status=status;execution.ErrorCode=code;execution.ErrorDetail=detail;execution.Warnings=[detail];return execution;}
}
