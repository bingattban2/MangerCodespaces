using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace AlTamam.Infrastructure.Security;

/// <summary>One random SQLCipher key per installation, protected for the current Windows user.</summary>
public sealed class WindowsDatabaseKeyStore
{
    private readonly string _keyPath;

    public WindowsDatabaseKeyStore(string keyPath) => _keyPath = Path.GetFullPath(keyPath);

    public string LoadOrCreate(string databasePath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows DPAPI is required for the installation key.");
        Directory.CreateDirectory(Path.GetDirectoryName(_keyPath)!);
        // Exclusive file handle serializes first run across independently started processes.
        using var lease = new FileStream(_keyPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        byte[] raw;
        if (File.Exists(_keyPath))
        {
            var protectedBytes = File.ReadAllBytes(_keyPath);
            if (protectedBytes.Length is < 32 or > 16384)
                throw new CryptographicException("The protected database key is invalid; recovery is required.");
            raw = Transform(protectedBytes, protect: false);
        }
        else
        {
            // Never generate a replacement key for existing encrypted data.
            if (File.Exists(databasePath) || File.Exists(databasePath + "-wal"))
                throw new CryptographicException("Database key missing. Restore the recovery package; existing data was not changed.");
            raw = RandomNumberGenerator.GetBytes(32);
            var temporary = _keyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var encrypted = Transform(raw, protect: true);
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(encrypted);
                    file.Flush(flushToDisk: true);
                }
                File.Move(temporary, _keyPath, overwrite: false);
            }
            catch
            {
                CryptographicOperations.ZeroMemory(raw);
                throw;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        try
        {
            if (raw.Length != 32) throw new CryptographicException("Invalid SQLCipher key length.");
            return Convert.ToHexString(raw);
        }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        var blob = new DataBlob { Length = input.Length, Data = Marshal.AllocHGlobal(input.Length) };
        var output = new DataBlob();
        try
        {
            Marshal.Copy(input, 0, blob.Data, input.Length);
            var success = protect
                ? CryptProtectData(ref blob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref blob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new CryptographicException("Windows could not protect or recover the installation key.", new Win32Exception(Marshal.GetLastWin32Error()));
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            for (var i = 0; i < input.Length; i++) Marshal.WriteByte(blob.Data, i, 0);
            Marshal.FreeHGlobal(blob.Data);
            if (output.Data != IntPtr.Zero)
            {
                for (var i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0);
                LocalFree(output.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Length; public IntPtr Data; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
