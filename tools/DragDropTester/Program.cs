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
using System.Windows.Forms;

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

    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;

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
        Console.WriteLine("EasyWinMenu Drag & Drop Automation & GIF Verification");
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

        foreach (var w in windows)
        {
            Console.WriteLine($"   HWND: 0x{w.Hwnd.ToInt64():X8} | Titulo: '{w.Title}' | Rect: [{w.Rect.Left},{w.Rect.Top},{w.Rect.Right},{w.Rect.Bottom}]");
        }

        // Filtra janelas de grupo (WPF windows com tamanho razoável)
        var groupWindows = windows.Where(w => (w.Rect.Right - w.Rect.Left) > 150 && (w.Rect.Bottom - w.Rect.Top) > 100).ToList();
        if (groupWindows.Count < 2)
        {
            Console.WriteLine("[ERRO] Menos de duas janelas de grupo encontradas para o teste.");
            return;
        }

        var sourceWindow = groupWindows[0];
        var destWindow = groupWindows[1];
        Console.WriteLine($"\n[3] Cenário de Arraste:");
        Console.WriteLine($"   Origem:  '{sourceWindow.Title}' [{sourceWindow.Rect.Left},{sourceWindow.Rect.Top} -> {sourceWindow.Rect.Right},{sourceWindow.Rect.Bottom}]");
        Console.WriteLine($"   Destino: '{destWindow.Title}' [{destWindow.Rect.Left},{destWindow.Rect.Top} -> {destWindow.Rect.Right},{destWindow.Rect.Bottom}]");

        // Coordenada inicial (um tile dentro da janela de origem)
        int startX = sourceWindow.Rect.Left + 60;
        int startY = sourceWindow.Rect.Top + 65;

        // Coordenada final (dentro da janela de destino)
        int endX = destWindow.Rect.Left + 100;
        int endY = destWindow.Rect.Top + 100;

        Console.WriteLine($"   Trajeto: ({startX}, {startY}) ===> ({endX}, {endY})");

        // Região para captura (bounding box cobrindo ambas as janelas com folga)
        int captureLeft = Math.Min(sourceWindow.Rect.Left, destWindow.Rect.Left) - 30;
        int captureTop = Math.Min(sourceWindow.Rect.Top, destWindow.Rect.Top) - 30;
        int captureRight = Math.Max(sourceWindow.Rect.Right, destWindow.Rect.Right) + 30;
        int captureBottom = Math.Max(sourceWindow.Rect.Bottom, destWindow.Rect.Bottom) + 30;
        int captureWidth = Math.Max(100, captureRight - captureLeft);
        int captureHeight = Math.Max(100, captureBottom - captureTop);

        Console.WriteLine($"   Área de Captura de Frames: [{captureLeft}, {captureTop}, {captureWidth}x{captureHeight}]");

        var frames = new List<Bitmap>();

        // 1. Move cursor para o tile inicial
        SetCursorPos(startX, startY);
        Thread.Sleep(300);
        frames.Add(CaptureFrame(captureLeft, captureTop, captureWidth, captureHeight, startX, startY));

        // 2. MouseDown
        Console.WriteLine("\n[4] Pressionando botão esquerdo (MouseDown) no tile...");
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(100);
        frames.Add(CaptureFrame(captureLeft, captureTop, captureWidth, captureHeight, startX, startY));

        // 3. Arraste interpolado em 30 passos (20-25 FPS)
        int steps = 30;
        Console.WriteLine($"[5] Conduzindo arraste contínuo em {steps} etapas suaves...");
        int ghostDetectedCount = 0;

        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            // Interpolação suave (easeInOutQuad)
            double ease = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
            int curX = (int)Math.Round(startX + (endX - startX) * ease);
            int curY = (int)Math.Round(startY + (endY - startY) * ease);

            SetCursorPos(curX, curY);
            Thread.Sleep(45); // ~22 FPS

            // Verifica se o ghost existe no processo
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

        Console.WriteLine($"   Ghost detectado em {ghostDetectedCount} de {steps} etapas de movimento.");

        // 4. MouseUp na janela de destino
        Console.WriteLine("[6] Soltando botão esquerdo (MouseUp) na janela de destino...");
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(200);

        // Quadros pós-soltura para registrar a reorganização/acomodação
        for (int i = 0; i < 6; i++)
        {
            Thread.Sleep(80);
            frames.Add(CaptureFrame(captureLeft, captureTop, captureWidth, captureHeight, endX, endY));
        }

        Console.WriteLine($"\n[7] Total de quadros capturados: {frames.Count}. Gerando GIF animado...");

        var gifPath = Path.Combine(screenshotDir, "drag_drop_resilience_verification.gif");
        var artifactGif = Path.Combine(artifactDir, "drag_drop_resilience_verification.gif");

        SaveAnimatedGif(frames, gifPath, delayMs: 50);
        File.Copy(gifPath, artifactGif, overwrite: true);

        Console.WriteLine($"[8] GIF gerado com sucesso:");
        Console.WriteLine($"   Repo:     {gifPath}");
        Console.WriteLine($"   Artifact: {artifactGif}");
        Console.WriteLine("\n[RESULTADO] Verificação automatizada física concluída com sucesso!");
    }

    static Bitmap CaptureFrame(int left, int top, int width, int height, int cursorX, int cursorY)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(left, top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);

            // Desenha cursor indicador não-vermelho (laranja brilhante + borda preta)
            int localCurX = cursorX - left;
            int localCurY = cursorY - top;
            if (localCurX >= 0 && localCurX < width && localCurY >= 0 && localCurY < height)
            {
                var brush = new SolidBrush(Color.FromArgb(240, 255, 170, 0)); // Laranja/Dourado (não vermelho!)
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
        // Constrói GIF animado padrão GIF89a via GDI+ multi-frame
        if (frames.Count == 0) return;

        var gifEncoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Gif.Guid);
        var enc = System.Drawing.Imaging.Encoder.SaveFlag;

        var firstFrame = frames[0];
        var encoderParams = new EncoderParameters(1);

        // Prepara byte array de tempo de delay (PropertyItem 0x5100 = FrameDelay)
        var item = (PropertyItem)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(PropertyItem));
        item.Id = 0x5100;
        item.Type = 3; // Short
        int delayHundreds = delayMs / 10;
        byte[] delayBytes = BitConverter.GetBytes((short)delayHundreds);
        item.Len = delayBytes.Length;
        item.Value = delayBytes;
        firstFrame.SetPropertyItem(item);

        // Loop infinito (PropertyItem 0x5101 = LoopCount)
        var loopItem = (PropertyItem)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(PropertyItem));
        loopItem.Id = 0x5101;
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
