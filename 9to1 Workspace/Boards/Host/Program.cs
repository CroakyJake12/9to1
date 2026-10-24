namespace HavenOS.Apps.Boards;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => Haven.Desktop.App.RunOriginalBoardsDesktop(args);
}
