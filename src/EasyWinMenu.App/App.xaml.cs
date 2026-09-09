using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace EasyWinMenu.App;

public partial class App : Application
{
    private NotifyIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Bloco de Notas", null, (_, _) => LaunchNotepad());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => Shutdown());

        _trayIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "EasyWinMenu",
            ContextMenuStrip = menu
        };

        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                menu.Show(Cursor.Position);
            }
        };
    }

    private static void LaunchNotepad()
    {
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
        }
        catch (FileNotFoundException)
        {
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
