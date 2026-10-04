using System.Drawing.Imaging;
using System.IO;

public static class Program
{
    public static void Main(string[] args)
    {
        var dir = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(dir);
        NixxIcon.WriteIco(Path.Combine(dir, "nixx.ico"), new[] { 16, 24, 32, 48, 64, 128, 256 });
    }
}