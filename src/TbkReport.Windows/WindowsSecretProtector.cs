using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using TbkReport.Services;

namespace TbkReport;

/// <summary>
/// Windows DPAPI (CryptProtectData) for the current user: the stored app password can be read only by
/// this Windows account on this computer. Called directly, so no extra NuGet package is needed.
/// </summary>
public sealed class WindowsSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "TBK Report mail"u8.ToArray();

    public byte[] Protect(byte[] data) => Transform(data, protect: true);
    public byte[] Unprotect(byte[] data) => Transform(data, protect: false);

    private static byte[] Transform(byte[] data, bool protect)
    {
        var input = Pin(data);
        var entropy = Pin(Entropy);
        var output = new DataBlob();
        try
        {
            var ok = protect
                ? CryptProtectData(ref input.Blob, "TBK Report", ref entropy.Blob, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output)
                : CryptUnprotectData(ref input.Blob, IntPtr.Zero, ref entropy.Blob, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        finally
        {
            input.Handle.Free();
            entropy.Handle.Free();
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }

    private const int UiForbidden = 0x1;

    private static (GCHandle Handle, DataBlob Blob) Pin(byte[] data)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        return (handle, new DataBlob { Size = data.Length, Data = handle.AddrOfPinnedObject() });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob input, string description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
