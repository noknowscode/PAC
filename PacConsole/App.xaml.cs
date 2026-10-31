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

#if DEBUG && !NO_UNO_STUDIO
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
#if DEBUG
        await ApplyVerificationViewAsync();
#endif
    }

#if DEBUG
    /// <summary>
    /// Screenshot-matrix hooks (Debug only). <c>--pac-theme=light|graphite</c> sets the window content's theme, the
    /// switch IThemeService flips, without saving it. <c>--pac-system=&lt;PlantSystem&gt;</c>, <c>--pac-unit=&lt;code&gt;</c>
    /// and <c>--pac-tab=overview|condition|control</c> drive the systems filter, the selection and the inspector tab
    /// through the page's own commands. <c>--pac-section=inspector|trends|log|end</c> scrolls the page to that band and
    /// keeps it there across window resizes. Each step prints a <c>VERIFY</c> line to stderr.
    /// </summary>
    private async Task ApplyVerificationViewAsync()
    {
        static string? Arg(string name) => Environment.GetCommandLineArgs()
            .FirstOrDefault(a => a.StartsWith($"--pac-{name}=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        try
        {
            if (MainWindow?.Content is not FrameworkElement root) return;
            if (Arg("theme") is { } theme)
            {
                root.RequestedTheme = theme.Equals("graphite", StringComparison.OrdinalIgnoreCase) || theme.Equals("dark", StringComparison.OrdinalIgnoreCase)
                    ? ElementTheme.Dark
                    : ElementTheme.Light;
                Console.Error.WriteLine($"VERIFY theme={theme} requested={root.RequestedTheme} actual={root.ActualTheme}");
            }
            if (Arg("system") is null && Arg("unit") is null && Arg("tab") is null && Arg("section") is null) return;

            // The page shows its content once the first snapshot arrives: wait for the plant panel.
            Controls.PlantViewport? viewport = null;
            for (var i = 0; i < 150 && viewport is null; i++)
            {
                await Task.Delay(100);
                viewport = Find<Controls.PlantViewport>(root);
            }
            if (viewport is null)
            {
                Console.Error.WriteLine("VERIFY failed: the console never showed its content");
                return;
            }
            if (Arg("system") is { } system)
            {
                var chip = FindAll<RadioButton>(root).FirstOrDefault(r => r.GroupName == "Systems" && Equals(r.CommandParameter, system));
                for (var i = 0; i < 50 && chip?.Command is { } c && !c.CanExecute(chip.CommandParameter); i++) await Task.Delay(100);
                chip?.Command?.Execute(chip.CommandParameter);
                await Task.Delay(1000);
                Console.Error.WriteLine($"VERIFY system={system} found={chip is not null} shown={viewport.ShownSystem}");
            }
            if (Arg("unit") is { } code)
            {
                var ok = Enum.TryParse<Core.Plant.UnitId>(code, ignoreCase: true, out var unit);
                var command = viewport.SelectCommand;
                // An MVUX command can execute once its feed parameters (the system, the view) have values.
                for (var i = 0; i < 50 && ok && command is not null && !command.CanExecute(unit); i++) await Task.Delay(100);
                if (ok) command?.Execute(unit);
                await Task.Delay(1000);
                Console.Error.WriteLine($"VERIFY unit={code} parsed={ok} command={command is not null} selected={viewport.SelectedUnit}");
            }
            if (Arg("tab") is { } tab)
            {
                var radio = FindAll<RadioButton>(root).FirstOrDefault(r => r.GroupName == "InspectorTabs" && string.Equals(r.Content as string, tab, StringComparison.OrdinalIgnoreCase));
                if (radio is not null) radio.IsChecked = true;
                Console.Error.WriteLine($"VERIFY tab={tab} found={radio is not null}");
            }
            if (Arg("section") is { } section)
            {
                var page = Find<ConsolePage>(root);
                void Scroll()
                {
                    if (page is null || Find<ScrollViewer>(page) is not { Content: UIElement content } viewer) return;
                    var target = section.Equals("end", StringComparison.OrdinalIgnoreCase)
                        ? null
                        : FindAll<FrameworkElement>(page).FirstOrDefault(e => string.Equals(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(e), section switch
                        {
                            "inspector" => "Equipment inspector",
                            "trends" => "Synchronized trends",
                            "log" => "Operator log",
                            _ => section,
                        }, StringComparison.OrdinalIgnoreCase));
                    var offset = target is null ? viewer.ScrollableHeight : target.TransformToVisual(content).TransformPoint(default).Y;
                    viewer.ChangeView(null, offset, null, disableAnimation: true);
                    Console.Error.WriteLine($"VERIFY section={section} found={target is not null || section == "end"} offset={offset:0} extent={viewer.ExtentHeight:0} width={root.ActualWidth:0}");
                }
                await Task.Delay(500);
                Scroll();
                // The matrix resizes the window live; keep the band at the top after each relayout.
                root.SizeChanged += async (_, _) => { await Task.Delay(800); Scroll(); };
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"VERIFY failed: {ex}");
        }
    }

    private static T? Find<T>(DependencyObject parent) where T : DependencyObject => FindAll<T>(parent).FirstOrDefault();

    private static IEnumerable<T> FindAll<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T hit) yield return hit;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            foreach (var found in FindAll<T>(VisualTreeHelper.GetChild(parent, i))) yield return found;
        }
    }
#endif

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
