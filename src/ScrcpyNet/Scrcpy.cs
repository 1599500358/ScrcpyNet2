using Serilog;
using SharpAdbClient;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;

namespace ScrcpyNet
{
    public class Scrcpy
    {
        int port;
        public string DeviceName { get; private set; } = ""; // 设备名称
        public int Width { get; internal set; } // 屏幕宽度
        public int Height { get; internal set; } // 屏幕高度
        public long Bitrate { get; set; } = 200000; // 视频流比特率
        public string ScrcpyServerFile { get; set; } = "ScrcpyNet/scrcpy-server.jar"; // Scrcpy服务器文件路径

        public bool Connected { get; private set; } // 是否已连接
        public VideoStreamDecoder VideoStreamDecoder { get; } // 视频流解码器

        private Thread? videoThread; // 视频线程
        private Thread? controlThread; // 控制线程
        private TcpClient? videoClient; // 视频客户端
        private TcpClient? controlClient; // 控制客户端
        private TcpListener? listener; // TCP监听器
        private CancellationTokenSource? cts; // 取消标记源

        private readonly AdbClient adb; // ADB客户端
        private readonly DeviceData device; // 设备数据
        private readonly Channel<IControlMessage> controlChannel = Channel.CreateUnbounded<IControlMessage>(); // 控制消息通道
        private static readonly ArrayPool<byte> pool = ArrayPool<byte>.Shared; // 字节数组池
        private static readonly ILogger log = Log.ForContext<VideoStreamDecoder>(); // 日志记录器

        public Scrcpy(DeviceData device,int port, VideoStreamDecoder? videoStreamDecoder = null)
        {
            this.port = port;
            DeviceName = device.Name;
            adb = new AdbClient();
            this.device = device;
            VideoStreamDecoder = videoStreamDecoder ?? new VideoStreamDecoder();
            VideoStreamDecoder.Scrcpy = this;
        }

        //public void SetDecoder(VideoStreamDecoder videoStreamDecoder)
        //{
        //    this.videoStreamDecoder = videoStreamDecoder;
        //    this.videoStreamDecoder.Scrcpy = this;
        //}

        /// <summary>
        /// 启动Scrcpy服务
        /// </summary>
        /// <param name="timeoutMs">超时时间（毫秒）</param>
        public void Start(long timeoutMs = 5000)
        {
            if (Connected)
                throw new Exception("Already connected.");

            MobileServerSetup();

            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();

            MobileServerStart();

            int waitTimeMs = 0;
            while (!listener.Pending())
            {
                Thread.Sleep(10);
                waitTimeMs += 10;

                if (waitTimeMs > timeoutMs)
                    throw new Exception("Timeout while waiting for server to connect.");
            }


            videoClient = listener.AcceptTcpClient();
            log.Information("Video socket connected.");


 

            if (!listener.Pending())
                throw new Exception("Server is not sending a second connection request. Is 'control' disabled?");

            controlClient = listener.AcceptTcpClient();
            log.Information("Control socket connected.");

            ReadDeviceInfo();

            cts = new CancellationTokenSource();

            videoThread = new Thread(VideoMain) { Name = "ScrcpyNet Video" };
            controlThread = new Thread(ControllerMain) { Name = "ScrcpyNet Controller" };

            videoThread.Start();
            controlThread.Start();

            Connected = true;

            // ADB forward/reverse is not needed anymore.
            MobileServerCleanup();
        }

        /// <summary>
        /// 停止Scrcpy服务
        /// </summary>
        public void Stop()
        {
            if (!Connected)
                throw new Exception("Not connected.");

            cts?.Cancel();

            videoThread?.Join();
            controlThread?.Join();
            listener?.Stop();
        }

        /// <summary>
        /// 发送控制命令
        /// </summary>
        /// <param name="msg">控制消息</param>
        public void SendControlCommand(IControlMessage msg)
        {
            if (controlClient == null)
                log.Warning("SendControlCommand() called, but controlClient is null.");
            else
                controlChannel.Writer.TryWrite(msg);
        }

        /// <summary>
        /// 读取设备信息
        /// </summary>
        private void ReadDeviceInfo()
        {
            // 检查videoClient是否为空
            if (videoClient == null)
                throw new Exception("Can't read device info when videoClient is null.");

            // 获取视频流的网络流
            var infoStream = videoClient.GetStream();
            infoStream.ReadTimeout = 2000;

            // 读取68字节的头部信息
            var deviceInfoBuf = pool.Rent(68);
            int bytesRead = infoStream.Read(deviceInfoBuf, 0, 64);

            // 检查读取的字节数是否为68
            if (bytesRead != 64)
                throw new Exception($"Expected to read exactly 68 bytes, but got {bytesRead} bytes.");

            // 从头部信息中解码设备名称
            var deviceInfoSpan = deviceInfoBuf.AsSpan();
            //DeviceName = Encoding.UTF8.GetString(deviceInfoSpan[..64]).TrimEnd(new[] { '\0' });
            //log.Information("Device name: " + DeviceName);

            // 从头部信息中读取屏幕宽度和高度
            //Width = BinaryPrimitives.ReadInt16BigEndian(deviceInfoSpan[64..]);
            //Height = BinaryPrimitives.ReadInt16BigEndian(deviceInfoSpan[66..]);
            //log.Information($"Initial texture: {Width}x{Height}");

            var video = infoStream.Read(deviceInfoBuf, 0, 12);

            // 将设备信息的缓冲区返回到池中
            pool.Return(deviceInfoBuf);
        }

        /// <summary>
        /// 视频线程主函数
        /// </summary>
        private void VideoMain()
        {
            // Both of these should never happen.
            if (videoClient == null) throw new Exception("videoClient is null.");
            if (cts == null) throw new Exception("cts is null.");

            var videoStream = videoClient.GetStream();
            videoStream.ReadTimeout = 2000;

            int bytesRead;
            var metaBuf = pool.Rent(12);

            Stopwatch sw = new();

            while (!cts.Token.IsCancellationRequested)
            {
                // Read metadata (each packet starts with some metadata)
                try
                {
                    bytesRead = videoStream.Read(metaBuf, 0, 12);
                }
                catch (IOException ex)
                {
                    // Ignore timeout errors.
                    if (ex.InnerException is SocketException x && x.SocketErrorCode == SocketError.TimedOut)
                        continue;
                    throw ex;
                }

                if (bytesRead != 12)
                    this.Stop();
                    //throw new Exception($"Expected to read exactly 12 bytes, but got {bytesRead} bytes.");

                sw.Restart();

                // Decode metadata
                var metaSpan = metaBuf.AsSpan();
                var presentationTimeUs = BinaryPrimitives.ReadInt64BigEndian(metaSpan);
                var packetSize = BinaryPrimitives.ReadInt32BigEndian(metaSpan[8..]);

                // Read the whole frame, this might require more than one .Read() call.
                var packetBuf = pool.Rent(packetSize);
                var pos = 0;
                var bytesToRead = packetSize;

                while (bytesToRead != 0 && !cts.Token.IsCancellationRequested)
                {
                    bytesRead = videoStream.Read(packetBuf, pos, bytesToRead);

                    if (bytesRead == 0)
                        throw new Exception("Unable to read any bytes.");

                    pos += bytesRead;
                    bytesToRead -= bytesRead;
                }

                if (!cts.Token.IsCancellationRequested)
                {
                    //Log.Verbose($"Presentation Time: {presentationTimeUs}us, PacketSize: {packetSize} bytes");
                    VideoStreamDecoder?.Decode(packetBuf, presentationTimeUs);
                    log.Verbose("Received and decoded a packet in {@ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
                }

                sw.Stop();

                pool.Return(packetBuf);
            }
        }

        /// <summary>
        /// 控制线程主函数
        /// </summary>
        private async void ControllerMain()
        {
            // Both of these should never happen.
            if (controlClient == null) throw new Exception("controlClient is null.");
            if (cts == null) throw new Exception("cts is null.");

            var stream = controlClient.GetStream();

            try
            {
                await foreach (var cmd in controlChannel.Reader.ReadAllAsync(cts.Token))
                {
                    ControllerSend(stream, cmd);
                }
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>
        /// 发送控制命令
        /// </summary>
        /// <param name="stream">网络流</param>
        /// <param name="cmd">控制消息</param>
        private void ControllerSend(NetworkStream stream, IControlMessage cmd)
        {
            
            var bytes = cmd.ToBytes();
            stream.Write(bytes);
        }

        /// <summary>
        /// 设置Scrcpy服务
        /// </summary>
        private void MobileServerSetup()
        {
            MobileServerCleanup();

            // Push scrcpy-server.jar
            UploadMobileServer();

            // Create port reverse rule
            adb.CreateReverseForward(device, "localabstract:scrcpy", "tcp:"+ port, true);
        }

        /// <summary>
        /// 清理Scrcpy服务
        /// </summary>
        private void MobileServerCleanup()
        {
            // Remove any existing network stuff.
            adb.RemoveAllForwards(device);
            adb.RemoveAllReverseForwards(device);
        }

        /// <summary>
        /// 启动Scrcpy服务
        /// </summary>
        private void MobileServerStart()
        {
            log.Information("Starting scrcpy server...");

            var cts = new CancellationTokenSource();
            var receiver = new SerilogOutputReceiver();

            //string version = "1.23";
            string version = "2.4";
            int maxFramerate = 0;
            ScrcpyLockVideoOrientation orientation = ScrcpyLockVideoOrientation.Orientation1; // -1 means allow rotate
            bool control = true;
            bool showTouches = false;
            bool stayAwake = false;

            var cmds = new List<string>
                    {
                        "CLASSPATH=/data/local/tmp/scrcpy-server.jar",
                        "app_process",

                        // Unused
                        "/",

                        // App entry point, or something like that.
                        "com.genymobile.scrcpy.Server",

                        version,
                        "log_level=debug",
                        $"bit_rate={Bitrate}"
                    };

            if (maxFramerate != 0)
                cmds.Add($"max_fps={maxFramerate}");

            if (orientation != ScrcpyLockVideoOrientation.Unlocked)
                cmds.Add($"lock_video_orientation={(int)orientation}");

            cmds.Add("tunnel_forward=false");
            //cmds.Add("crop=-");
            cmds.Add($"control={control}");
            cmds.Add("display_id=0");
            cmds.Add($"show_touches={showTouches}");
            cmds.Add($"stay_awake={stayAwake}");
            cmds.Add("power_off_on_close=false");
            cmds.Add("downsize_on_error=true");
            cmds.Add("max_size=1920");
            cmds.Add("audio=false");
            cmds.Add("cleanup=true");

            //cmds.Add("raw_stream=true");

            string command = string.Join(" ", cmds);

            log.Information("Start command: " + command);
            _ = adb.ExecuteRemoteCommandAsync(command, device, receiver, cts.Token);
        }

        /// <summary>
        /// 上传Scrcpy服务
        /// </summary>
        private void UploadMobileServer()
        {
            using SyncService service = new(new AdbSocket(new IPEndPoint(IPAddress.Loopback, AdbClient.AdbServerPort)), device);
            using Stream stream = File.OpenRead(ScrcpyServerFile);
            service.Push(stream, "/data/local/tmp/scrcpy-server.jar", 444, DateTime.Now, null, CancellationToken.None);
        }
    }
}
