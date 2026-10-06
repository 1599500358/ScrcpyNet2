using FFmpeg.AutoGen;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;
using SharpAdbClient;
using System;
using System.IO;
using System.Linq;

namespace ScrcpyNet.Test
{
    [TestClass]
    public class UnitTest1
    {
        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext _)
        {
            ffmpeg.RootPath = Path.Combine(AppContext.BaseDirectory, "ScrcpyNet");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console()
                .CreateLogger();
        }

        [TestMethod]
        public void VideoStreamDecoderConstruction()
        {
            Console.WriteLine($"CWD: {Environment.CurrentDirectory}");
            Console.WriteLine($"RootPath: {ffmpeg.RootPath}");
            Console.WriteLine($"avcodec-63.dll exists: {File.Exists(Path.Combine(ffmpeg.RootPath ?? "", "avcodec-63.dll"))}");

            // Loads the native FFmpeg dlls from deps (avcodec-63 & co) and opens the
            // h264 decoder + parser.
            using var dec = new VideoStreamDecoder();
        }

        [TestMethod]
        public void TestMethod1()
        {
            var adb = new AdbClient();
            var device = adb.GetDevices().FirstOrDefault();

            if (device == null)
            {
                Assert.Inconclusive("No device connected.");
                return;
            }

            // A fixed port (e.g. the default 27183) collides with other local scrcpy
            // clients that may be running (the sample app binds one port per device), so
            // grab a free one instead.
            // using: also releases the decoder's FFmpeg contexts when the test ends.
            using var adc = new Scrcpy(device, GetFreePort());
            // Live hardware: the handshake takes ~4.5s warm and much longer when the
            // phone wakes from idle/doze, hence a generous budget here.
            adc.Start(30000);
        }

        private static int GetFreePort()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        [TestMethod]
        public void StreamDecoder()
        {
            const string FrameFile = @"L:\Repos\LupoCV\LupoCV.CLI\bin\Debug\netcoreapp3.1\frames.avc";
            if (!File.Exists(FrameFile))
            {
                Assert.Inconclusive($"Frame dump not found: {FrameFile}");
                return;
            }

            VideoStreamDecoder dec = new VideoStreamDecoder();
            using FileStream fs = File.OpenRead(FrameFile);

            byte[] buffer = new byte[1024 * 16];
            int bytesRead;

            while ((bytesRead = fs.Read(buffer)) > 0)
                dec.Decode(buffer, 0, bytesRead);
        }
    }
}
