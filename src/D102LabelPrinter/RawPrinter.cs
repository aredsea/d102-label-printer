using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Text;

namespace D102LabelPrinter;

/// <summary>Win32 스풀러 raw 모드로 ZPL 바이트를 프린터에 직접 전송(드라이버 렌더 우회).</summary>
public static class RawPrinter
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private class DOCINFOW
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pDocName;
        [MarshalAs(UnmanagedType.LPWStr)] public string pOutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string pDataType;
    }

    [DllImport("winspool.Drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string src, out IntPtr hPrinter, IntPtr pd);
    [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);
    [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] DOCINFOW di);
    [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);
    [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);
    [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);
    [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    public static IEnumerable<string> GetPrinters()
    {
        foreach (string p in PrinterSettings.InstalledPrinters) yield return p;
    }

    public static string DefaultPrinter() => new PrinterSettings().PrinterName;

    /// <summary>printerName 이 비면 기본 프린터. ZPL은 ASCII(한글은 ^GF 비트맵이라 ASCII)이므로 Latin1로 전송.</summary>
    public static void SendZpl(string printerName, string zpl)
    {
        if (string.IsNullOrWhiteSpace(printerName)) printerName = DefaultPrinter();
        byte[] bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(zpl);
        SendBytes(printerName, bytes, "D102 Label");
    }

    public static void SendBytes(string printerName, byte[] bytes, string docName)
    {
        if (!OpenPrinter(printerName, out IntPtr hPrinter, IntPtr.Zero))
            throw new InvalidOperationException($"프린터 열기 실패: '{printerName}' (오류 {Marshal.GetLastWin32Error()})");
        try
        {
            var di = new DOCINFOW { pDocName = docName, pDataType = "RAW" };
            if (!StartDocPrinter(hPrinter, 1, di))
                throw new InvalidOperationException($"StartDocPrinter 실패 (오류 {Marshal.GetLastWin32Error()})");
            try
            {
                if (!StartPagePrinter(hPrinter))
                    throw new InvalidOperationException($"StartPagePrinter 실패 (오류 {Marshal.GetLastWin32Error()})");
                IntPtr p = Marshal.AllocHGlobal(bytes.Length);
                try
                {
                    Marshal.Copy(bytes, 0, p, bytes.Length);
                    if (!WritePrinter(hPrinter, p, bytes.Length, out int written) || written != bytes.Length)
                        throw new InvalidOperationException($"WritePrinter 실패 ({written}/{bytes.Length}, 오류 {Marshal.GetLastWin32Error()})");
                }
                finally { Marshal.FreeHGlobal(p); }
                EndPagePrinter(hPrinter);
            }
            finally { EndDocPrinter(hPrinter); }
        }
        finally { ClosePrinter(hPrinter); }
    }
}
