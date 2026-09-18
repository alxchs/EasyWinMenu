using System.IO;
using System.Runtime.InteropServices;

namespace QuickStacks.Infrastructure;

/// <summary>
/// Métodos nativos de integração com a Shell do Windows (Fase 18 - folha de propriedades nativa).
/// </summary>
public static class ShellHelper
{
    private const int SeeMaskInvokeIdList = 0x0000000C;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo lpExecInfo);

    /// <summary>
    /// Exibe a janela nativa de propriedades do Windows para o arquivo ou pasta especificado.
    /// </summary>
    public static void ShowProperties(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var info = new ShellExecuteInfo
            {
                cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
                fMask = SeeMaskInvokeIdList,
                lpVerb = "properties",
                lpFile = path,
                nShow = 1,
            };

            ShellExecuteEx(ref info);
        }
        catch
        {
            // ShellExecuteEx pode falhar se o arquivo não existir ou se a shell estiver ocupada
        }
    }
}

