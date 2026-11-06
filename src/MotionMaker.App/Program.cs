using Avalonia;
using Avalonia.OpenGL;

namespace MotionMaker.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.Wgl, Win32RenderingMode.AngleEgl]
            })
            .With(new X11PlatformOptions
            {
                RenderingMode = [X11RenderingMode.Glx, X11RenderingMode.Egl]
            })
            .WithInterFont()
            .LogToTrace();
}
