// The app types the linked renderer files read, so the product's own code runs unchanged outside the app.
// Program.cs fills them from Styles/Palette.xaml and Styles/Tokens.xaml, as the app's resources would hold them.

namespace Microsoft.UI.Xaml
{
    public enum ElementTheme
    {
        Default,
        Light,
        Dark,
    }

    /// <summary>Stands in for the app's resources: the theme-invariant numbers of Styles/Tokens.xaml.</summary>
    public sealed class Application
    {
        public static Application Current { get; } = new();

        public Dictionary<string, object> Resources { get; } = [];
    }
}

namespace PacConsole.Styles
{
    /// <summary>Stands in for the app's ThemeBrushes: the colours of Styles/Palette.xaml, per theme.</summary>
    public static class ThemeBrushes
    {
        public static Dictionary<string, Windows.UI.Color> Light { get; } = [];

        public static Dictionary<string, Windows.UI.Color> Dark { get; } = [];

        public static Windows.UI.Color Color(string key, Microsoft.UI.Xaml.ElementTheme theme) =>
            (theme == Microsoft.UI.Xaml.ElementTheme.Dark ? Dark : Light)[key];
    }
}

namespace Windows.UI
{
    public readonly record struct Color(byte A, byte R, byte G, byte B);
}

namespace Uno.Extensions
{
    /// <summary>Stands in for Uno's ambient logger: the renderer's faults go to stderr.</summary>
    public static class LogShim
    {
        public static Microsoft.Extensions.Logging.ILogger Log(this Type type) => new StderrLogger(type.Name);

        private sealed class StderrLogger(string name) : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                Console.Error.WriteLine($"{logLevel} {name}: {formatter(state, exception)} {exception}");
        }
    }
}
