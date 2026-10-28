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
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton(TimeProvider.System);
                    services.AddSingleton<ITelemetryService, SimulatedTelemetry>();
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

        // The console is designed at 1440×900.
        MainWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1440, Height = 900 });

        Host = builder.Build();
        await MainWindow.InitializeNavigationAsync(
            () => Task.FromResult(Host),
            initialRoute: "Console"
        );
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
