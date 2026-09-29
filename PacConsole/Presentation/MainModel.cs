namespace PacConsole.Presentation;

public partial record MainModel
{
    public MainModel(
        INavigator navigator)
    {
        Title = "Main";
    }

    public string? Title { get; }


}
