using Avalonia;
using Avalonia.ReactiveUI;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace DesktopUI2.Launcher
{
  class Program
  {
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {

      // to debug the VS previewer
      // 
      // 1. uncomment the lines below & rebuild
      // 2. (optional)open another instance of the project in VS & attach it to this process
      // 3. close and reopen the problematic XAML window

      //Debugger.Launch();
      //while (!Debugger.IsAttached)
      //  Thread.Sleep(100);

      // Avalonia 11.0.0-preview1 issue: CornerRadius not clipping,
      // Avalonia 11.0.0-preview1 issue: sometimes might crash by collection enumerate fail
      // TODO: change false to true when avaloniaUI compositor feature is fixed
      const bool useCompositor = false;

      string path = Path.GetDirectoryName(typeof(App).Assembly.Location);

      string nativeLib = Path.Combine(path, "Native", "libAvalonia.Native.OSX.dylib");
      return AppBuilder.Configure<App>()
      .UsePlatformDetect()
      .With(new X11PlatformOptions { UseGpu = false, UseCompositor = useCompositor })
      .With(new MacOSPlatformOptions { ShowInDock = false })
      .With(new AvaloniaNativePlatformOptions
      {
        AvaloniaNativeLibraryPath = nativeLib,
        UseCompositor = useCompositor
      })
      .With(new SkiaOptions { MaxGpuResourceSizeBytes = 8096000 })
      .With(new Win32PlatformOptions { AllowEglInitialization = true, UseCompositor = useCompositor })
      .LogToTrace()
      .UseReactiveUI();
    }

  }
}
