namespace Haven.Application;

/// <summary>A display scope over the same canonical tasks. Selecting a scope never
/// changes task origin, ownership, execution, or authority to read another context.</summary>
public enum TaskShownScope
{
    TaskSpaceOnly = 0,
    AllTasks = 1
}
