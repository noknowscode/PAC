using Uno.UI.Hosting;

namespace PacConsole;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {

        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
#if DEBUG
            // Screenshot runs under Xvfb: PAC_X11_RENDERER=software forces Skia's CPU backend instead of Mesa's llvmpipe
            // OpenGL, which leaves the window black or part-painted after a resize there.
            .UseX11(x11 =>
            {
                if (Environment.GetEnvironmentVariable("PAC_X11_RENDERER") == "software")
                {
                    x11.ForceRenderingBackend(X11RenderingBackend.Software);
                }
            })
#else
            .UseX11()
#endif
            .UseLinuxFrameBuffer()
            .UseMacOS()
            .UseWin32()
            .Build();

        host.Run();
    }
}
