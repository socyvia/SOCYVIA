using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SOCYVIA.Data;
using SOCYVIA.Models;

namespace SOCYVIA.Repositories;

public static class AnalysisRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task SaveSpecificationAsync(AnalysisSpecification specification)
    {
        specification.UpdatedAtUtc=DateTime.UtcNow;
        await using var connection=await DatabaseService.OpenConnectionAsync();await using var command=connection.CreateCommand();
        command.CommandText="""
            INSERT INTO AnalysisSpecifications
            (Id,StudyId,Name,ResearchQuestion,Classification,OutcomeVariableId,PredictorVariableId,
             CovariatesJson,AnalysisFamily,Method,AlternativeHypothesis,ConfidenceLevel,
             MissingDataHandling,MultipleComparisonMethod,ParametersJson,EngineVersion,IsDemo,
             CreatedAtUtc,UpdatedAtUtc)
            VALUES ($id,$study,$name,$question,$classification,$outcome,$predictor,$covariates,$family,
                    $method,$alternative,$confidence,$missing,$multiple,$parameters,$engine,$demo,$created,$updated)
            ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name,ResearchQuestion=excluded.ResearchQuestion,
                Classification=excluded.Classification,OutcomeVariableId=excluded.OutcomeVariableId,
                PredictorVariableId=excluded.PredictorVariableId,CovariatesJson=excluded.CovariatesJson,
                AnalysisFamily=excluded.AnalysisFamily,Method=excluded.Method,
                AlternativeHypothesis=excluded.AlternativeHypothesis,ConfidenceLevel=excluded.ConfidenceLevel,
                MissingDataHandling=excluded.MissingDataHandling,MultipleComparisonMethod=excluded.MultipleComparisonMethod,
                ParametersJson=excluded.ParametersJson,EngineVersion=excluded.EngineVersion,UpdatedAtUtc=excluded.UpdatedAtUtc;
            """;
        Add(command,"$id",specification.Id);Add(command,"$study",specification.StudyId);Add(command,"$name",specification.Name);Add(command,"$question",specification.ResearchQuestion);Add(command,"$classification",specification.Classification);Add(command,"$outcome",specification.OutcomeVariableId);Add(command,"$predictor",specification.PredictorVariableId);Add(command,"$covariates",JsonSerializer.Serialize(specification.Covariates,JsonOptions));Add(command,"$family",specification.AnalysisFamily);Add(command,"$method",specification.Method);Add(command,"$alternative",specification.AlternativeHypothesis);Add(command,"$confidence",specification.ConfidenceLevel);Add(command,"$missing",specification.MissingDataHandling);Add(command,"$multiple",specification.MultipleComparisonMethod);Add(command,"$parameters",specification.ParametersJson);Add(command,"$engine",specification.EngineVersion);Add(command,"$demo",specification.IsDemo);Add(command,"$created",specification.CreatedAtUtc);Add(command,"$updated",specification.UpdatedAtUtc);await command.ExecuteNonQueryAsync();
    }

    public static async Task SaveExecutionAsync(AnalysisExecution execution,IReadOnlyList<AnalysisExclusion>? exclusions=null)
    {
        await using var connection=await DatabaseService.OpenConnectionAsync();await using var transaction=connection.BeginTransaction();
        await using(var command=connection.CreateCommand())
        {
            command.Transaction=transaction;command.CommandText="""
                INSERT INTO AnalysisExecutions
                (Id,AnalysisSpecificationId,StudyId,Status,DatasetHash,DatasetDescriptorJson,
                 ResultJson,DiagnosticsJson,WarningJson,ErrorCode,ErrorDetail,EngineVersion,ExecutedAtUtc,IsDemo)
                VALUES ($id,$specification,$study,$status,$hash,$dataset,$result,$diagnostics,$warnings,
                        $errorCode,$errorDetail,$engine,$executed,$demo);
                """;
            Add(command,"$id",execution.Id);Add(command,"$specification",execution.AnalysisSpecificationId);Add(command,"$study",execution.StudyId);Add(command,"$status",execution.Status);Add(command,"$hash",execution.DatasetHash);Add(command,"$dataset",execution.DatasetDescriptorJson);Add(command,"$result",execution.Result is null?null:JsonSerializer.Serialize(execution.Result,JsonOptions));Add(command,"$diagnostics",JsonSerializer.Serialize(execution.Diagnostics,JsonOptions));Add(command,"$warnings",JsonSerializer.Serialize(execution.Warnings,JsonOptions));Add(command,"$errorCode",execution.ErrorCode);Add(command,"$errorDetail",execution.ErrorDetail);Add(command,"$engine",execution.EngineVersion);Add(command,"$executed",execution.ExecutedAtUtc);Add(command,"$demo",execution.IsDemo);await command.ExecuteNonQueryAsync();
        }
        if(exclusions is not null)foreach(var exclusion in exclusions){await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="INSERT INTO AnalysisExclusions (Id,AnalysisExecutionId,ParticipantId,SessionId,ReasonCode,ReasonDetail) VALUES ($id,$execution,$participant,$session,$code,$detail);";Add(command,"$id",Guid.NewGuid().ToString());Add(command,"$execution",execution.Id);Add(command,"$participant",exclusion.ParticipantId);Add(command,"$session",exclusion.SessionId);Add(command,"$code",exclusion.ReasonCode);Add(command,"$detail",exclusion.ReasonDetail);await command.ExecuteNonQueryAsync();}await transaction.CommitAsync();
    }

    public static async Task<List<AnalysisSpecification>> GetSpecificationsAsync(string studyId,bool? isDemo=null)
    {
        var result=new List<AnalysisSpecification>();await using var connection=await DatabaseService.OpenConnectionAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT Id,StudyId,Name,ResearchQuestion,Classification,OutcomeVariableId,PredictorVariableId,CovariatesJson,AnalysisFamily,Method,AlternativeHypothesis,ConfidenceLevel,MissingDataHandling,MultipleComparisonMethod,ParametersJson,EngineVersion,IsDemo,CreatedAtUtc,UpdatedAtUtc FROM AnalysisSpecifications WHERE StudyId=$study AND ($demo IS NULL OR IsDemo=$demo) ORDER BY CreatedAtUtc;";Add(command,"$study",studyId);Add(command,"$demo",isDemo.HasValue?(isDemo.Value?1:0):null);await using var reader=await command.ExecuteReaderAsync();while(await reader.ReadAsync())result.Add(new AnalysisSpecification{Id=reader.GetString(0),StudyId=reader.GetString(1),Name=reader.GetString(2),ResearchQuestion=Nullable(reader,3),Classification=reader.GetString(4),OutcomeVariableId=reader.GetString(5),PredictorVariableId=Nullable(reader,6),Covariates=JsonSerializer.Deserialize<string[]>(reader.GetString(7),JsonOptions)??[],AnalysisFamily=reader.GetString(8),Method=reader.GetString(9),AlternativeHypothesis=reader.GetString(10),ConfidenceLevel=reader.GetDouble(11),MissingDataHandling=reader.GetString(12),MultipleComparisonMethod=reader.GetString(13),ParametersJson=Nullable(reader,14),EngineVersion=reader.GetString(15),IsDemo=reader.GetInt32(16)!=0,CreatedAtUtc=Parse(reader.GetString(17)),UpdatedAtUtc=Parse(reader.GetString(18))});return result;
    }

    public static async Task<AnalysisExecution?> GetLatestExecutionAsync(string specificationId)
    {
        await using var connection=await DatabaseService.OpenConnectionAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT Id,AnalysisSpecificationId,StudyId,Status,DatasetHash,DatasetDescriptorJson,ResultJson,DiagnosticsJson,WarningJson,ErrorCode,ErrorDetail,EngineVersion,ExecutedAtUtc,IsDemo FROM AnalysisExecutions WHERE AnalysisSpecificationId=$id ORDER BY ExecutedAtUtc DESC LIMIT 1;";command.Parameters.AddWithValue("$id",specificationId);await using var reader=await command.ExecuteReaderAsync();if(!await reader.ReadAsync())return null;return new AnalysisExecution{Id=reader.GetString(0),AnalysisSpecificationId=reader.GetString(1),StudyId=reader.GetString(2),Status=reader.GetString(3),DatasetHash=reader.GetString(4),DatasetDescriptorJson=reader.GetString(5),Result=reader.IsDBNull(6)?null:JsonSerializer.Deserialize<StatisticalResult>(reader.GetString(6),JsonOptions),Diagnostics=reader.IsDBNull(7)?[]:JsonSerializer.Deserialize<AnalysisDiagnostic[]>(reader.GetString(7),JsonOptions)??[],Warnings=reader.IsDBNull(8)?[]:JsonSerializer.Deserialize<string[]>(reader.GetString(8),JsonOptions)??[],ErrorCode=Nullable(reader,9),ErrorDetail=Nullable(reader,10),EngineVersion=reader.GetString(11),ExecutedAtUtc=Parse(reader.GetString(12)),IsDemo=reader.GetInt32(13)!=0};
    }

    private static void Add(SqliteCommand command,string name,object? value)=>command.Parameters.AddWithValue(name,value switch{null=>DBNull.Value,DateTime date=>date.ToString("O"),bool boolean=>boolean?1:0,_=>value});
    private static string? Nullable(SqliteDataReader reader,int ordinal)=>reader.IsDBNull(ordinal)?null:reader.GetString(ordinal);
    private static DateTime Parse(string value)=>DateTime.Parse(value,null,System.Globalization.DateTimeStyles.RoundtripKind);
}
