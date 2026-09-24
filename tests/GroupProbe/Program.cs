using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Automation;

namespace EasyWinMenu.Probe;

/// <summary>
/// Records what the desktop-group feature actually does, as a diffable JSON report plus one PNG
/// per group window. Run it against two builds and compare the reports: anything the refactor
/// changed about groups shows up as a diff, and anything it did not leaves the report identical.
///
/// It never touches the real configuration except through a verified backup/restore cycle: the
/// user's own groups are copied aside before the fixture is installed and put back at the end,
/// and the run aborts before launching anything if that backup cannot be made.
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true };

    private static int Main(string[] args)
    {
        Win32.SetProcessDPIAware();

        var options = ProbeOptions.Parse(args);
        if (options is null)
        {
            Console.Error.WriteLine("usage: GroupProbe --exe <EasyWinMenu.App.exe> --fixture <config.json> --out <dir> --label <name>");
            return 2;
        }

        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EasyWinMenu",
            "config.json");
        var backupPath = Path.Combine(options.OutputDirectory, "user-config.backup.json");

        Directory.CreateDirectory(options.OutputDirectory);

        // Guard first, act second: if the user's configuration cannot be preserved, nothing runs.
        var hadUserConfig = File.Exists(configPath);
        if (hadUserConfig)
        {
            File.Copy(configPath, backupPath, overwrite: true);
            if (!FilesMatch(configPath, backupPath))
            {
                Console.Error.WriteLine("ABORT: the backup of the user's config does not match the original.");
                return 3;
            }

            Console.WriteLine($"user config backed up -> {backupPath}");
        }

        try
        {
            return Run(options, configPath);
        }
        finally
        {
            StopApp();

            if (hadUserConfig)
            {
                File.Copy(backupPath, configPath, overwrite: true);
                Console.WriteLine(FilesMatch(configPath, backupPath)
                    ? "user config restored and verified."
                    : "WARNING: the restored config does not match the backup.");
            }
            else if (File.Exists(configPath))
            {
                File.Delete(configPath);
            }
        }
    }

    private static int Run(ProbeOptions options, string configPath)
    {
        StopApp();

        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.Copy(options.FixturePath, configPath, overwrite: true);

        var fixture = JsonNode.Parse(File.ReadAllText(options.FixturePath))!.AsObject();
        var expectedGroups = fixture["Categories"]!.AsArray()
            .Select(node => new
            {
                Name = node!["Name"]!.GetValue<string>(),
                X = node["DesktopX"]!.GetValue<double>(),
                Y = node["DesktopY"]!.GetValue<double>()
            })
            .OrderBy(group => group.Y)
            .ThenBy(group => group.X)
            .ToList();

        Console.WriteLine($"launching {options.ExePath}");
        var app = Process.Start(new ProcessStartInfo(options.ExePath) { UseShellExecute = true })
            ?? throw new InvalidOperationException("the app did not start.");

        var live = Process.GetProcessesByName("EasyWinMenu.App").FirstOrDefault()
            ?? throw new InvalidOperationException("no EasyWinMenu.App process is running.");

        // Wait for the windows instead of guessing a delay: a cold start of a freshly copied build
        // needs far longer than a warm one, and a fixed sleep silently measured an empty desktop.
        var appeared = WaitForGroupWindows((uint)live.Id, expectedGroups.Count, TimeSpan.FromSeconds(40));
        Console.WriteLine($"group windows visible after {appeared.TotalSeconds:F1}s");

        // Let the icons finish resolving before anything is captured.
        Thread.Sleep(2500);

        var report = new JsonObject
        {
            ["label"] = options.Label,
            ["exeFileVersion"] = FileVersionInfo.GetVersionInfo(options.ExePath).FileVersion,
            ["fixture"] = Path.GetFileName(options.FixturePath)
        };

        var windows = CollectGroupWindows((uint)live.Id, expectedGroups.Count);
        report["groupWindowCount"] = windows.Count;
        report["windows"] = DescribeWindows(windows, expectedGroups.Select(g => g.Name).ToList(), options.OutputDirectory, options.Label);
        report["groupMenus"] = CaptureGroupMenus(windows, expectedGroups.Select(g => g.Name).ToList(), (uint)live.Id);
        report["configAfterRun"] = NormalizeConfig(File.ReadAllText(configPath));

        var reportPath = Path.Combine(options.OutputDirectory, $"report-{options.Label}.json");
        File.WriteAllText(reportPath, report.ToJsonString(ReportJson));
        Console.WriteLine($"report written -> {reportPath}");

        StopApp();
        return 0;
    }

    private static TimeSpan WaitForGroupWindows(uint processId, int expectedCount, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout)
        {
            if (CollectGroupWindows(processId, expectedCount, quiet: true).Count >= expectedCount)
            {
                return clock.Elapsed;
            }

            Thread.Sleep(400);
        }

        return clock.Elapsed;
    }

    /// <summary>
    /// The group windows, ordered the same way every run: top-to-bottom then left-to-right, which
    /// is the order the fixture's groups are sorted into as well. Ordering rather than absolute
    /// coordinates is what keeps the match stable across DPI scaling.
    /// </summary>
    private static List<IntPtr> CollectGroupWindows(uint processId, int expectedCount, bool quiet = false)
    {
        var candidates = new List<(IntPtr Handle, Win32.Rect Rect)>();
        foreach (var handle in Win32.VisibleWindowsOf(processId))
        {
            if (!Win32.GetWindowRect(handle, out var rect))
            {
                continue;
            }

            // The message-only menu host sits off-screen at -10000; real group windows have size.
            if (rect.Width < 120 || rect.Height < 80 || rect.Left < -5000)
            {
                continue;
            }

            candidates.Add((handle, rect));
        }

        var ordered = candidates
            .OrderBy(candidate => candidate.Rect.Top)
            .ThenBy(candidate => candidate.Rect.Left)
            .Select(candidate => candidate.Handle)
            .ToList();

        if (!quiet && ordered.Count != expectedCount)
        {
            Console.Error.WriteLine($"WARNING: expected {expectedCount} group windows, found {ordered.Count}.");
        }

        return ordered;
    }

    private static JsonArray DescribeWindows(List<IntPtr> windows, List<string> groupNames, string outputDirectory, string label)
    {
        var described = new JsonArray();
        for (var index = 0; index < windows.Count; index++)
        {
            var handle = windows[index];
            Win32.GetWindowRect(handle, out var rect);
            var exStyle = Win32.GetWindowLong(handle, Win32.GWL_EXSTYLE);

            var name = index < groupNames.Count ? groupNames[index] : $"window{index}";
            var shotPath = Path.Combine(outputDirectory, $"{label}-{Sanitize(name)}.png");
            var captured = Win32.TryCapture(handle, shotPath);

            described.Add(new JsonObject
            {
                ["assumedGroup"] = name,

                // WPF names every HwndSource "HwndWrapper[<app>;;<guid>]" with a fresh guid per
                // process, so the raw class name differs on every run and says nothing about
                // behaviour. The shape is what matters; the guid is dropped.
                ["class"] = StripWindowGuid(Win32.ClassOf(handle)),
                ["title"] = Win32.TitleOf(handle),
                ["rect"] = new JsonObject
                {
                    ["left"] = rect.Left,
                    ["top"] = rect.Top,
                    ["width"] = rect.Width,
                    ["height"] = rect.Height
                },
                ["exStyle"] = new JsonObject
                {
                    ["toolWindow"] = (exStyle & Win32.WS_EX_TOOLWINDOW) != 0,
                    ["appWindow"] = (exStyle & Win32.WS_EX_APPWINDOW) != 0,
                    ["layered"] = (exStyle & Win32.WS_EX_LAYERED) != 0,
                    ["transparent"] = (exStyle & Win32.WS_EX_TRANSPARENT) != 0
                },
                ["screenshotSha256"] = captured ? Sha256OfFile(shotPath) : null
            });
        }

        return described;
    }

    /// <summary>
    /// Right-clicks inside each group and reads the menu that opens through UI Automation. WPF
    /// menus publish a proper automation tree (unlike the hand-built icon tiles, which is why an
    /// earlier attempt to drive the icons this way failed), so the item names, their order and
    /// their checked state come back exactly as the user sees them.
    /// </summary>
    private static JsonArray CaptureGroupMenus(List<IntPtr> windows, List<string> groupNames, uint processId)
    {
        var menus = new JsonArray();

        for (var index = 0; index < windows.Count; index++)
        {
            var handle = windows[index];
            var name = index < groupNames.Count ? groupNames[index] : $"window{index}";

            // HWND_TOPMOST, then back: the groups live in the desktop's z-order band, so without
            // this the click can land on whatever window happens to cover them.
            Win32.SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
            Thread.Sleep(400);

            Win32.GetWindowRect(handle, out var rect);
            var before = new HashSet<IntPtr>(Win32.VisibleWindowsOf(processId));

            // Low in the window, away from the header and from any icon tile.
            Win32.RightClick(rect.Left + (rect.Width / 2), rect.Bottom - 24);
            Thread.Sleep(900);

            var popup = Win32.VisibleWindowsOf(processId).FirstOrDefault(candidate => !before.Contains(candidate));
            var items = new JsonArray();

            if (popup != IntPtr.Zero)
            {
                try
                {
                    var root = AutomationElement.FromHandle(popup);

                    // The popup hwnd wraps the ContextMenu; reading the menu's own children (not
                    // every descendant) is what keeps submenu entries from being listed twice.
                    var menuRoot = root.FindFirst(
                        TreeScope.Subtree,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu)) ?? root;

                    ReadMenuItems(menuRoot, items, depth: 0);
                }
                catch (Exception ex)
                {
                    items.Add(new JsonObject { ["error"] = ex.GetType().Name });
                }
            }

            Win32.PressEscape();
            Thread.Sleep(400);
            Win32.SetWindowPos(handle, new IntPtr(-2), 0, 0, 0, 0, 0x0013);

            menus.Add(new JsonObject
            {
                ["group"] = name,
                ["popupFound"] = popup != IntPtr.Zero,
                ["items"] = items
            });
        }

        return menus;
    }

    private static void ReadMenuItems(AutomationElement parent, JsonArray into, int depth)
    {
        if (depth > 2)
        {
            return;
        }

        var found = parent.FindAll(
            TreeScope.Children,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem));

        foreach (AutomationElement element in found)
        {
            var entry = new JsonObject
            {
                ["depth"] = depth,
                ["name"] = element.Current.Name,
                ["enabled"] = element.Current.IsEnabled
            };

            if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var togglePattern))
            {
                entry["toggle"] = ((TogglePattern)togglePattern).Current.ToggleState.ToString();
            }

            var children = ExpandAndRead(element, depth);

            if (children.Count > 0)
            {
                entry["children"] = children;
            }

            into.Add(entry);
        }
    }

    /// <summary>
    /// The saved configuration with everything run-specific replaced by a stable placeholder, so
    /// two runs of the same build produce byte-identical text. Group Ids are freshly generated
    /// GUIDs, which would otherwise make every report differ from every other.
    /// </summary>
    private static JsonNode NormalizeConfig(string configText)
    {
        var root = JsonNode.Parse(configText)!;
        var counter = 0;
        NormalizeIds(root, ref counter);
        return root;
    }

    private static void NormalizeIds(JsonNode? node, ref int counter)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.TryGetPropertyValue("Id", out var id) && id is not null)
                {
                    obj["Id"] = $"<id-{counter++}>";
                }

                foreach (var property in obj.ToList())
                {
                    var running = counter;
                    NormalizeIds(property.Value, ref running);
                    counter = running;
                }

                break;

            case JsonArray array:
                foreach (var element in array)
                {
                    var running = counter;
                    NormalizeIds(element, ref running);
                    counter = running;
                }

                break;
        }
    }

    /// <summary>
    /// Opens a submenu and reads it, waiting for the menu to report itself expanded instead of
    /// sleeping a guessed amount. A fixed sleep made this flaky: the same build produced a
    /// 283-field report on one run and 316 on the next, purely because the first group's
    /// submenus had not finished opening when they were read. A measurement that changes
    /// between runs cannot tell a refactor from noise, so it retries until the tree is stable.
    /// </summary>
    private static JsonArray ExpandAndRead(AutomationElement element, int depth)
    {
        var children = new JsonArray();

        if (!element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var pattern))
        {
            return children;
        }

        var expand = (ExpandCollapsePattern)pattern;
        if (expand.Current.ExpandCollapseState == ExpandCollapseState.LeafNode)
        {
            return children;
        }

        for (var attempt = 0; attempt < 3 && children.Count == 0; attempt++)
        {
            try
            {
                expand.Expand();

                // Wait for the state the menu reports, not for a duration.
                var clock = Stopwatch.StartNew();
                while (clock.Elapsed < TimeSpan.FromSeconds(3)
                    && expand.Current.ExpandCollapseState != ExpandCollapseState.Expanded)
                {
                    Thread.Sleep(80);
                }

                // The items still materialise a beat after the state flips; poll for them too.
                var settle = Stopwatch.StartNew();
                while (settle.Elapsed < TimeSpan.FromSeconds(3) && children.Count == 0)
                {
                    Thread.Sleep(120);
                    children = new JsonArray();
                    ReadMenuItems(element, children, depth + 1);
                }

                expand.Collapse();
                Thread.Sleep(150);
            }
            catch (Exception ex)
            {
                children = new JsonArray { new JsonObject { ["error"] = ex.GetType().Name } };
            }
        }

        return children;
    }

    private static string StripWindowGuid(string className)
    {
        var separator = className.IndexOf(";;", StringComparison.Ordinal);
        return separator < 0 ? className : $"{className[..separator]};;<guid>]";
    }

    private static void StopApp()
    {
        foreach (var process in Process.GetProcessesByName("EasyWinMenu.App"))
        {
            try
            {
                process.Kill();
                process.WaitForExit(4000);
            }
            catch (Exception)
            {
                // Already gone between the enumeration and the kill; nothing to do.
            }
        }

        Thread.Sleep(600);
    }

    private static bool FilesMatch(string left, string right) =>
        Sha256OfFile(left) == Sha256OfFile(right);

    private static string Sha256OfFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(Path.GetInvalidFileNameChars().Contains(character) || character == ' ' ? '_' : character);
        }

        return builder.ToString();
    }

    private sealed record ProbeOptions(string ExePath, string FixturePath, string OutputDirectory, string Label)
    {
        public static ProbeOptions? Parse(string[] args)
        {
            string? exe = null, fixture = null, output = null, label = null;
            for (var index = 0; index + 1 < args.Length; index += 2)
            {
                var value = args[index + 1];
                switch (args[index].ToLower(CultureInfo.InvariantCulture))
                {
                    case "--exe": exe = value; break;
                    case "--fixture": fixture = value; break;
                    case "--out": output = value; break;
                    case "--label": label = value; break;
                }
            }

            if (exe is null || fixture is null || output is null || label is null)
            {
                return null;
            }

            return new ProbeOptions(Path.GetFullPath(exe), Path.GetFullPath(fixture), Path.GetFullPath(output), label);
        }
    }
}
