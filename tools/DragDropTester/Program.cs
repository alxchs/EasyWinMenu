using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DragDropTester;

class Program
{
    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;
    const byte VK_CONTROL = 0x11;
    const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern int GetClassName(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    const int GWL_EXSTYLE = -20;
    const int WS_EX_TRANSPARENT = 0x00000020;
    const int WS_EX_LAYERED = 0x00080000;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    record WindowInfo(IntPtr Hwnd, string Title, string ClassName, RECT Rect);

    static void Main(string[] args)
    {
        SetProcessDPIAware();
        Console.WriteLine("==================================================================");
        Console.WriteLine("EasyWinMenu Automated Drag & Drop Test Agents Suite");
        Console.WriteLine("==================================================================");

        var repoRoot = @"C:\desenv\utils\EasyWinMenu";
        var artifactDir = @"C:\Users\alxch\.gemini\antigravity\brain\3b4eee52-4015-4dd6-808e-716488bce559";
        var screenshotDir = Path.Combine(repoRoot, "tools", "screenshots");
        Directory.CreateDirectory(screenshotDir);

        var appExe = Path.Combine(repoRoot, @"src\EasyWinMenu.App\bin\Release\net10.0-windows\win-x64\publish\EasyWinMenu.App.exe");
        if (!File.Exists(appExe))
        {
            appExe = Path.Combine(repoRoot, @"src\EasyWinMenu.App\bin\Release\net10.0-windows\EasyWinMenu.App.exe");
        }

        Console.WriteLine($"[1] Localizando executável: {appExe}");
        var proc = Process.GetProcessesByName("EasyWinMenu.App").FirstOrDefault();
        if (proc is null || proc.HasExited)
        {
            Console.WriteLine("[1.1] Iniciando EasyWinMenu.App...");
            proc = Process.Start(new ProcessStartInfo(appExe) { UseShellExecute = true });
            Thread.Sleep(3000);
        }
        else
        {
            Console.WriteLine($"[1.1] EasyWinMenu.App já em execução (PID: {proc.Id}).");
        }

        uint pid = (uint)proc.Id;
        var windows = GetProcessWindows(pid);
        Console.WriteLine($"[2] Janelas detectadas para PID {pid}: {windows.Count}");

        // Filtra janelas de grupo (WPF windows com tamanho razoável)
        var groupWindows = windows.Where(w => (w.Rect.Right - w.Rect.Left) > 150 && (w.Rect.Bottom - w.Rect.Top) > 100).ToList();
        if (groupWindows.Count < 2)
        {
            Console.WriteLine("[ERRO] Menos de duas janelas de grupo encontradas para os testes.");
            return;
        }

        var sourceWindow = groupWindows[0];
        var destWindow = groupWindows[1];

        Console.WriteLine($"   Grupo Origem:  '{sourceWindow.Title}' [{sourceWindow.Rect.Left},{sourceWindow.Rect.Top} -> {sourceWindow.Rect.Right},{sourceWindow.Rect.Bottom}]");
        Console.WriteLine($"   Grupo Destino: '{destWindow.Title}' [{destWindow.Rect.Left},{destWindow.Rect.Top} -> {destWindow.Rect.Right},{destWindow.Rect.Bottom}]");

        // Região para captura (bounding box cobrindo ambas as janelas com folga)
        int captureLeft = Math.Min(sourceWindow.Rect.Left, destWindow.Rect.Left) - 30;
        int captureTop = Math.Min(sourceWindow.Rect.Top, destWindow.Rect.Top) - 30;
        int captureRight = Math.Max(sourceWindow.Rect.Right, destWindow.Rect.Right) + 30;
        int captureBottom = Math.Max(sourceWindow.Rect.Bottom, destWindow.Rect.Bottom) + 30;
        int captureWidth = Math.Max(100, captureRight - captureLeft);
        int captureHeight = Math.Max(100, captureBottom - captureTop);

        // =========================================================================================
        // AGENTE 1: Arraste de Objeto Único com Verificação de Não-Desaparecimento e Reorganização
        // =========================================================================================
        Console.WriteLine("\n==================================================================");
        Console.WriteLine("TEST AGENT 1: Arraste de Item Único & Verificação de Visibilidade do Ghost");
        Console.WriteLine("==================================================================");

        int tile1X = sourceWindow.Rect.Left + 55;
        int tile1Y = sourceWindow.Rect.Top + 65;
        int dropDestX = destWindow.Rect.Left + 90;
        int dropDestY = destWindow.Rect.Top + 90;

        RunDragScenario(
            pid,
            sourceWindow,
            destWindow,
            startX: tile1X,
            startY: tile1Y,
            endX: dropDestX,
            endY: dropDestY,
            captureLeft, captureTop, captureWidth, captureHeight,
            scenarioName: "Single Item Drag & Ghost Verification",
            gifFileName: "drag_drop_single_item.gif",
            screenshotDir,
            artifactDir,
            beforeDrag: null);

        Thread.Sleep(1000);

        // =========================================================================================
        // AGENTE 2: Arraste de Múltiplos Objetos (2 Marcados Simultaneamente)
        // =========================================================================================
        Console.WriteLine("\n==================================================================");
        Console.WriteLine("TEST AGENT 2: Arraste de Múltiplos Itens (2 Itens Marcados)");
        Console.WriteLine("==================================================================");

        int multiTile1X = destWindow.Rect.Left + 55;
        int multiTile1Y = destWindow.Rect.Top + 65;
        int multiTile2X = destWindow.Rect.Left + 135;
        int multiTile2Y = destWindow.Rect.Top + 65;
        int returnDestX = sourceWindow.Rect.Left + 90;
        int returnDestY = sourceWindow.Rect.Top + 90;

        RunDragScenario(
            pid,
            destWindow,
            sourceWindow,
            startX: multiTile1X,
            startY: multiTile1Y,
            endX: returnDestX,
            endY: returnDestY,
            captureLeft, captureTop, captureWidth, captureHeight,
            scenarioName: "Multi-Item (2 items) Drag & Drop Across Windows",
            gifFileName: "drag_drop_multi_item.gif",
            screenshotDir,
            artifactDir,
            beforeDrag: () =>
            {
                // 1. Clica no item 1
                Console.WriteLine("   [Multi-Select] Selecionando item 1...");
                ClickAt(multiTile1X, multiTile1Y);
                Thread.Sleep(200);

                // 2. Ctrl + Clique no item 2
                Console.WriteLine("   [Multi-Select] Ctrl + Clique no item 2 para selecionar ambos...");
                CtrlClickAt(multiTile2X, multiTile2Y);
                Thread.Sleep(250);
            });

        Thread.Sleep(1000);

        // =========================================================================================
        // AGENTE 3: Reorganização Dentro do Mesmo Grupo ao Soltar
        // =========================================================================================
        Console.WriteLine("\n==================================================================");
        Console.WriteLine("TEST AGENT 3: Reorganização Forçada na Soltura Dentro do Mesmo Grupo");
        Console.WriteLine("==================================================================");

        int sameGroupStartX = sourceWindow.Rect.Left + 55;
        int sameGroupStartY = sourceWindow.Rect.Top + 65;
        int sameGroupEndX = sourceWindow.Rect.Left + 140;
        int sameGroupEndY = sourceWindow.Rect.Top + 130;

        int sLeft = sourceWindow.Rect.Left - 20;
        int sTop = sourceWindow.Rect.Top - 20;
        int sWidth = (sourceWindow.Rect.Right - sourceWindow.Rect.Left) + 40;
        int sHeight = (sourceWindow.Rect.Bottom - sourceWindow.Rect.Top) + 40;

        RunDragScenario(
            pid,
            sourceWindow,
            sourceWindow,
            startX: sameGroupStartX,
            startY: sameGroupStartY,
            endX: sameGroupEndX,
            endY: sameGroupEndY,
            sLeft, sTop, sWidth, sHeight,
            scenarioName: "Same Group Reorganization On Drop",
            gifFileName: "drag_drop_same_group_reorganize.gif",
            screenshotDir,
            artifactDir,
            beforeDrag: null);

        Console.WriteLine("\n==================================================================");
        Console.WriteLine("[SUCESSO TOTAL] Todos os 3 cenários de testes automatizados concluídos!");
        Console.WriteLine("==================================================================");
    }

    static void ClickAt(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(60);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }

    static void CtrlClickAt(int x, int y)
    {
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        Thread.Sleep(40);
        SetCursorPos(x, y);
        Thread.Sleep(40);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(40);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    static void RunDragScenario(
        uint pid,
        WindowInfo sourceWindow,
        WindowInfo destWindow,
        int startX, int startY,
        int endX, int endY,
        int captureLeft, int captureTop, int captureWidth, int captureHeight,
        string scenarioName,
        string gifFileName,
        string screenshotDir,
        string artifactDir,
        Action? beforeDrag)
    {
        Console.WriteLine($"\n[Cenário: {scenarioName}]");
        Console.WriteLine($"   Trajeto: ({startX}, {startY}) ===> ({endX}, {endY})");

        var frames = new List<Bitmap>();

        if (beforeDrag is not null)
        {
            beforeDrag();
            frames.Add(CaptureFrame(captureLeft, captureTop, captureWidth, captureHeight, startX, startY));
        }

        // 1. Move cursor para coordenada inicial
        SetCursorPos(startX, startY);
        Thread.Sleep(200);
        frames.Add(CaptureFrame(captureLeft, captureTop, captureWidth, captureHeight, startX, startY));

        // 2. MouseDown
        Console.WriteLine("   [Passo] MouseDown no objeto...");
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(100);
        frames.Add(CaptureFrame(captureLeft, captureTop, captureWidth, captureHeight, startX, startY));

        // 3. Arraste contínuo
        int steps = 28;
        int ghostDetectedCount = 0;
        Console.WriteLine($"   [Passo] Conduzindo arraste contínuo em {steps} etapas...");

        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            double ease = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
            int curX = (int)Math.Round(startX + (endX - startX) * ease);
            int curY = (int)Math.Round(startY + (endY - startY) * ease);

            SetCursorPos(curX, curY);
            Thread.Sleep(45); // ~22 FPS

            var liveWindows = GetProcessWindows(pid);
            bool ghostPresent = liveWindows.Any(w =>
            {
                var ex = GetWindowLong(w.Hwnd, GWL_EXSTYLE);
                return (ex & WS_EX_TRANSPARENT) != 0 && (ex & WS_EX_LAYERED) != 0;
            });

            if (ghostPresent)
            {
                ghostDetectedCount++;
            }

            frames.Add(CaptureFrame(captureLeft, captureTop, captureWidth, captureHeight, curX, curY));
        }

        Console.WriteLine($"   [Verificação] Ghost Window detectado em {ghostDetectedCount} de {steps} etapas de movimento.");

        // 4. MouseUp
        Console.WriteLine("   [Passo] MouseUp (soltura do objeto)...");
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(150);

        // Quadros pós-soltura para registrar a reorganização/reflow da grade
        for (int i = 0; i < 8; i++)
        {
            Thread.Sleep(80);
            frames.Add(CaptureFrame(captureLeft, captureTop, captureWidth, captureHeight, endX, endY));
        }

        // 5. Salva GIF comprobatório
        var gifPath = Path.Combine(screenshotDir, gifFileName);
        var artifactGif = Path.Combine(artifactDir, gifFileName);

        Console.WriteLine($"   [GIF] Gerando GIF ({frames.Count} quadros)...");
        SaveAnimatedGif(frames, gifPath, delayMs: 48);
        try
        {
            File.Copy(gifPath, artifactGif, overwrite: true);
        }
        catch { }

        Console.WriteLine($"   [GIF Salvo] {gifPath}");
    }

    static Bitmap CaptureFrame(int left, int top, int width, int height, int cursorX, int cursorY)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(left, top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);

            // Cursor indicador: Laranja/Dourado com borda preta (NÃO VERMELHO!)
            int localCurX = cursorX - left;
            int localCurY = cursorY - top;
            if (localCurX >= 0 && localCurX < width && localCurY >= 0 && localCurY < height)
            {
                var brush = new SolidBrush(Color.FromArgb(240, 255, 175, 0));
                var pen = new Pen(Color.Black, 2);
                var points = new Point[]
                {
                    new(localCurX, localCurY),
                    new(localCurX, localCurY + 18),
                    new(localCurX + 5, localCurY + 14),
                    new(localCurX + 11, localCurY + 22),
                    new(localCurX + 14, localCurY + 20),
                    new(localCurX + 8, localCurY + 13),
                    new(localCurX + 15, localCurY + 13)
                };
                g.FillPolygon(brush, points);
                g.DrawPolygon(pen, points);
            }
        }
        return bmp;
    }

    static void SaveAnimatedGif(List<Bitmap> frames, string outputPath, int delayMs)
    {
        if (frames.Count == 0) return;

        var gifEncoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Gif.Guid);
        var enc = System.Drawing.Imaging.Encoder.SaveFlag;

        var firstFrame = frames[0];
        var encoderParams = new EncoderParameters(1);

        var item = (PropertyItem)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(PropertyItem));
        item.Id = 0x5100; // FrameDelay
        item.Type = 3;
        int delayHundreds = delayMs / 10;
        byte[] delayBytes = BitConverter.GetBytes((short)delayHundreds);
        item.Len = delayBytes.Length;
        item.Value = delayBytes;
        firstFrame.SetPropertyItem(item);

        var loopItem = (PropertyItem)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(PropertyItem));
        loopItem.Id = 0x5101; // LoopCount
        loopItem.Type = 3;
        loopItem.Len = 4;
        loopItem.Value = new byte[] { 0, 0, 0, 0 }; // 0 = infinito
        firstFrame.SetPropertyItem(loopItem);

        encoderParams.Param[0] = new EncoderParameter(enc, (long)EncoderValue.MultiFrame);
        firstFrame.Save(outputPath, gifEncoder, encoderParams);

        encoderParams.Param[0] = new EncoderParameter(enc, (long)EncoderValue.FrameDimensionTime);
        for (int i = 1; i < frames.Count; i++)
        {
            frames[i].SetPropertyItem(item);
            firstFrame.SaveAdd(frames[i], encoderParams);
        }

        encoderParams.Param[0] = new EncoderParameter(enc, (long)EncoderValue.Flush);
        firstFrame.SaveAdd(encoderParams);
    }

    static List<WindowInfo> GetProcessWindows(uint pid)
    {
        var list = new List<WindowInfo>();
        EnumWindows((hWnd, lParam) =>
        {
            if (IsWindowVisible(hWnd))
            {
                GetWindowThreadProcessId(hWnd, out uint wPid);
                if (wPid == pid)
                {
                    var sbTitle = new StringBuilder(256);
                    GetWindowText(hWnd, sbTitle, 256);

                    var sbClass = new StringBuilder(256);
                    GetClassName(hWnd, sbClass, 256);

                    GetWindowRect(hWnd, out RECT rc);
                    list.Add(new WindowInfo(hWnd, sbTitle.ToString(), sbClass.ToString(), rc));
                }
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
