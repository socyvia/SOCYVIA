using System.Linq;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public static class ResearchResultPackageService
{
    public static async Task<ResearchResultPackage> CreateAsync(
        Study study,
        AnalysisDataset dataset,
        DataQualityResult dataQuality,
        AnalysisSpecification specification,
        AnalysisExecution execution)
    {
        var groupsTask=GroupRepository.GetByStudyAsync(study.Id);
        var conditionsTask=ExperimentalConditionRepository.GetByStudyAsync(study.Id);
        await Task.WhenAll(groupsTask,conditionsTask);
        return new ResearchResultPackage
        {
            StudyId=study.Id,
            StudyDesign=study.DesignType,
            Hypotheses=string.IsNullOrWhiteSpace(study.Hypothesis)?[]:[study.Hypothesis],
            Groups=groupsTask.Result.OrderBy(item=>item.SortOrder).Select(item=>item.Name).ToArray(),
            Conditions=conditionsTask.Result.OrderBy(item=>item.SortOrder).Select(item=>item.Name).ToArray(),
            Variables=dataset.Variables,
            DataQuality=dataQuality,
            Specification=specification,
            Execution=execution
        };
    }
}

public static class ReportingSectionContracts
{
    public static readonly string[] PublicationOrientedSections=
    [
        "Participants","Design","Measures","Data Quality","Descriptive Results",
        "Inferential Results","Effect Sizes","Confidence Intervals","Figures",
        "Tables","Limitations","Analysis Provenance"
    ];
}
