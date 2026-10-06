namespace Haven.Application;
/// <summary>Whole actual current selection source lifetime, borrowed by the paired Home
/// READ owner. Retirement grants no READ; all pending/faulted originals remain retained.</summary>
public interface IDeveloperOriginalCurrentProjectSelectionRetirementSource
{
    void RequestOriginalCurrentProjectRetirement();
    void DemandExternalOriginalCurrentProjectJoin();
    Task CloseAndDrainOriginalCurrentProjectsAsync();
}
