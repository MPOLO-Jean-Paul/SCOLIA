using System;
using System.Text;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
public static class ITMSecurityCrypto {
    public static void CommitProtectedFile(string source, string destination) {
        source=System.IO.Path.GetFullPath(source);
        destination=System.IO.Path.GetFullPath(destination);
        // Literal C# null avoids PowerShell converting a null string argument to "".
        if(System.IO.File.Exists(destination)) System.IO.File.Replace(source,destination,null);
        else System.IO.File.Move(source,destination);
    }
    public static byte[] Random(int count) {
        byte[] bytes = new byte[count];
        using (var rng = RandomNumberGenerator.Create()) { rng.GetBytes(bytes); }
        return bytes;
    }
    // PBKDF2-HMAC-SHA256, one 32-byte output block; compatible with .NET 4.
    public static byte[] Derive(string password, byte[] salt, int iterations) {
        if (iterations < 1 || salt == null) throw new ArgumentException();
        byte[] key = Encoding.UTF8.GetBytes(password);
        try {
            using (var hmac = new HMACSHA256(key)) {
                byte[] block = new byte[salt.Length + 4];
                Buffer.BlockCopy(salt, 0, block, 0, salt.Length); block[block.Length - 1] = 1;
                byte[] u = hmac.ComputeHash(block); byte[] result = (byte[])u.Clone();
                for (int i = 1; i < iterations; i++) {
                    byte[] next = hmac.ComputeHash(u); Array.Clear(u, 0, u.Length); u = next;
                    for (int j = 0; j < result.Length; j++) result[j] ^= u[j];
                }
                Array.Clear(u, 0, u.Length); return result;
            }
        } finally { Array.Clear(key, 0, key.Length); }
    }
    public static bool Equal(byte[] first, byte[] second) {
        if (first == null || second == null || first.Length != second.Length) return false;
        int diff = 0; for (int i = 0; i < first.Length; i++) diff |= first[i] ^ second[i];
        return diff == 0;
    }
    [StructLayout(LayoutKind.Sequential)] private struct InputInfo { public uint Size; public uint Tick; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref InputInfo value);
    public static uint IdleMilliseconds() {
        InputInfo info = new InputInfo(); info.Size = (uint)Marshal.SizeOf(info);
        if (!GetLastInputInfo(ref info)) return 0;
        return unchecked((uint)Environment.TickCount - info.Tick);
    }
}
