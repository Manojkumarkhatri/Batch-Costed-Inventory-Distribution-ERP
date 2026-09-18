using System;
using Velopack;

namespace ShopApp.UI;

/// <summary>
/// The real entry point.
///
/// Velopack has to run before anything else: during an install, an update or
/// an uninstall the app is launched with arguments it must handle and then
/// exit, without ever showing a window. Putting this in App.OnStartup would be
/// too late - WPF would already be up, and the update would apply against a
/// running application.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Hooks install, update and uninstall. Returns immediately on a normal
        // launch; exits the process on a lifecycle one.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
