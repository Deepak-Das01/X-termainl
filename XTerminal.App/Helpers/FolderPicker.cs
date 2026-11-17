using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace XTerminal.App.Helpers;

internal static class FolderPicker
{
    public static string? PickFolder(Window? owner, string title)
    {
        var bi = new BROWSEINFO
        {
            hwndOwner = owner is null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle,
            lpszTitle = title,
            ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE
        };

        var pidl = SHBrowseForFolder(ref bi);
        if (pidl == IntPtr.Zero)
        {
            return null;
        }

        var sb = new StringBuilder(260);
        try
        {
            return SHGetPathFromIDList(pidl, sb) ? sb.ToString() : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pidl);
        }
    }

    private const uint BIF_RETURNONLYFSDIRS = 0x0001;
    private const uint BIF_NEWDIALOGSTYLE = 0x0040;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BROWSEINFO
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszTitle;
        public uint ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO lpbi);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder pszPath);
}
