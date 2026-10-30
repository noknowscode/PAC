using System.Diagnostics.CodeAnalysis;
using PacConsole.Core.Telemetry;
using Uno.Resizetizer;

namespace PacConsole;

public partial class App : Application
{
    /// <summary>
    /// Initializes the singleton application object. This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        this.InitializeComponent();
    }

    protected Window? MainWindow { get; private set; }

    /// <summary>The app window, for view glue that changes the window itself (full screen).</summary>
    internal static Window? Shell { get; private set; }
    protected IHost? Host { get; private set; }

    [SuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Uno.Extensions APIs are used in a way that is safe for trimming in this template context.")]
    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // async void: an exception that escapes here ends the process with no trace (silently, in a browser).
        // Log it and leave a readable message in the window instead.
        try
        {
            await LaunchAsync(args);
        }
        catch (Exception ex)
        {
            if (Host?.Services.GetService<ILogger<App>>() is { } log) log.LogCritical(ex, "PAC Control Console failed to start");
            else Console.Error.WriteLine($"PAC Control Console failed to start: {ex}");
            if (MainWindow is { } window)
            {
                window.Content = new TextBlock
                {
                    Text = $"PAC Control Console could not start.\n\n{ex.Message}",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(24),
                };
                window.Activate();
            }
        }
    }

    [SuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Uno.Extensions APIs are used in a way that is safe for trimming in this template context.")]
    private async Task LaunchAsync(LaunchActivatedEventArgs args)
    {
        var builder = this.CreateBuilder(args)
            .UseToolkitNavigation()
            .Configure(host => host
#if DEBUG
                .UseEnvironment(Environments.Development)
#endif
                .UseLogging(configure: (context, logBuilder) =>
                {
                    logBuilder
                        .SetMinimumLevel(
                            context.HostingEnvironment.IsDevelopment() ?
                                LogLevel.Information :
                                LogLevel.Warning)
                        .CoreLogLevel(LogLevel.Warning);
                }, enableUnoLogging: true)
                .UseThemeSwitching()
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton(TimeProvider.System);
                    services.AddSingleton<ITelemetryService>(sp => new SimulatedTelemetry(sp.GetRequiredService<TimeProvider>())
                    {
#if DEBUG
                        // PAC_TELEMETRY_FAULT=<n> drops the stream after n snapshots, once, to exercise the error state and Retry.
                        FaultAfter = int.TryParse(Environment.GetEnvironmentVariable("PAC_TELEMETRY_FAULT"), out var n) ? n : null,
#endif
                    });
                })
                .UseNavigation(ReactiveViewModelMappings.ViewModelMappings, RegisterRoutes)
            );
        MainWindow = builder.Window;
        Shell = MainWindow;
        MainWindow.Title = "PAC Control Console";

#if DEBUG
        // Hot Design needs a reachable DevServer; without one it can block a headless run.
        // APP_NO_HOTDESIGN=1 opts a verification run out.
        if (Environment.GetEnvironmentVariable("APP_NO_HOTDESIGN") != "1")
        {
            MainWindow.UseStudio();
        }
#endif
        MainWindow.SetWindowIcon();

#if __DESKTOP__ || WINDOWS
        // The console is designed at 1440×900. Only a desktop window can be sized; on a phone, a tablet or in a
        // browser the platform sets the size.
        MainWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1440, Height = 900 });
#endif

        Host = builder.Build();
        await MainWindow.InitializeNavigationAsync(
            () => Task.FromResult(Host),
            initialRoute: "Console"
        );
        // Brushes chosen in code (converters, tag plates, range bars) follow the root's theme.
        if (MainWindow.Content is FrameworkElement root) Styles.ThemeBrushes.Track(root);
    }

    private static void RegisterRoutes(IViewRegistry views, IRouteRegistry routes)
    {
        views.Register(
            new ViewMap<ConsolePage, ConsoleModel>()
        );

        routes.Register(
            new RouteMap("Console", View: views.FindByViewModel<ConsoleModel>(), IsDefault: true)
        );
    }

}
