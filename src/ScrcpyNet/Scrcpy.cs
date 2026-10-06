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
    public class Scrcpy : IDisposable
    {
        /// <summary>
        /// scrcpy-server version this client implements. The server refuses to start when the
        /// version passed as the first argument doesn't match exactly (Options.parse()).
        /// </summary>
        public const string ServerVersion = "4.1";

        /// <summary>"h264" in ASCII (sc_demuxer_to_avcodec_id).</summary>
        private const uint CodecIdH264 = 0x68323634;

        // Streamer.PACKET_FLAG_* (server) / SC_PACKET_FLAG_* (app/src/demuxer.c)
        private const ulong PacketFlagConfig = 1ul << 62;
        private const ulong PacketPtsMask = (1ul << 61) - 1;

        int port;
        public string DeviceName { get; private set; } = ""; // 设备名称
        // Written by the video thread (decoder), read by the UI. Plain ints are atomic
        // and the pair is only ever consumed directionally (aspect ratio), so no lock;
        // a torn w/h mix can only appear for one frame during a physical rotation.
        public int Width { get; internal set; } // 屏幕宽度
        public int Height { get; internal set; } // 屏幕高度
        public long Bitrate { get; set; } = 200000; // 视频流比特率（bit/s）
        public int MaxFps { get; set; } // 0 = 不限制
        public int MaxSize { get; set; } = 1920; // 0 = 不限制
        public string? VideoEncoder { get; set; }
        public ScrcpyCaptureOrientation CaptureOrientation { get; set; } = ScrcpyCaptureOrientation.Unlocked;
        public bool ShowTouches { get; set; }
        public bool StayAwake { get; set; }
        public string ScrcpyServerFile { get; set; } = "ScrcpyNet/scrcpy-server.jar"; // Scrcpy服务器文件路径

        public bool Connected { get; private set; } // 是否已连接
        public VideoStreamDecoder VideoStreamDecoder { get; } // 视频流解码器

        /// <summary>
        /// Raised whenever the video size changes (initial device info or a session meta
        /// packet, e.g. after the device was rotated). Fires on the video thread.
        /// </summary>
        public event Action? VideoSizeChanged;

        /// <summary>
        /// Raised when the connection was lost unexpectedly (the device disconnected or the
        /// server stopped streaming), after the internal sockets were released. Not raised
        /// when <see cref="Stop"/> was called explicitly. Fires on a threadpool thread.
        /// </summary>
        public event Action? Disconnected;

        /// <summary>Guards so the unexpected-disconnect handling only runs once per session.</summary>
        private int streamEndedHandled;

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

        public Scrcpy(DeviceData device, int port, VideoStreamDecoder? videoStreamDecoder = null)
        {
            this.port = port;
            DeviceName = device.Name;
            adb = new AdbClient();
            this.device = device;
            VideoStreamDecoder = videoStreamDecoder ?? new VideoStreamDecoder();
            VideoStreamDecoder.Scrcpy = this;
        }

        /// <summary>
        /// 启动Scrcpy服务
        /// </summary>
        /// <param name="timeoutMs">超时时间（毫秒）。默认 15 秒：握手热态约 4.5 秒，
        /// 但从休眠唤醒的设备（CPU 降频、doze）需要明显更久，5 秒会误判失败。</param>
        public void Start(long timeoutMs = 15000)
        {
            if (Connected)
                throw new Exception("Already connected.");

            Interlocked.Exchange(ref streamEndedHandled, 0);
            Interlocked.Exchange(ref controlGoneLogged, 0);

            try
            {
                MobileServerSetup();

                listener = new TcpListener(IPAddress.Loopback, port);
                // Allow immediate rebind while sockets from a previous session are still
                // in TIME_WAIT (quick disconnect->reconnect / batch restarts). The port is
                // only held until both sockets below are accepted, never for the whole
                // session, so this cannot silently shadow another listener for long.
                listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
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

                // With audio disabled the server connects two sockets in this order: video, control.
                videoClient = listener.AcceptTcpClient();
                log.Information("Video socket connected.");

                // The control connection arrives right after the video one, but "right
                // after" is not "instantly": poll instead of checking Pending() once.
                waitTimeMs = 0;
                while (!listener.Pending())
                {
                    Thread.Sleep(10);
                    waitTimeMs += 10;

                    if (waitTimeMs > timeoutMs)
                        throw new Exception("Server is not sending a second connection request. Is 'control' disabled?");
                }

                controlClient = listener.AcceptTcpClient();
                log.Information("Control socket connected.");

                // Both sockets are connected and the server never opens more, so stop
                // listening now. Otherwise the listener would keep the port bound (and
                // accept stray connections) until Stop() — or forever after an
                // unexpected disconnect.
                listener.Stop();
                listener = null;

                ReadDeviceInfo(timeoutMs);

                cts = new CancellationTokenSource();

                videoThread = new Thread(VideoMain) { Name = "ScrcpyNet Video" };
                controlThread = new Thread(ControllerMain) { Name = "ScrcpyNet Controller" };

                videoThread.Start();
                controlThread.Start();

                Connected = true;

                // ADB forward/reverse is not needed anymore.
                MobileServerCleanup();
            }
            catch
            {
                // On failure release everything, otherwise the loopback port stays
                // bound and the on-device server keeps running, which makes any
                // reconnect attempt fail.
                CleanupAfterStop();
                throw;
            }
        }

        /// <summary>
        /// Releases sockets, threads and ADB forwards. Safe to call when partially started.
        /// </summary>
        private void CleanupAfterStop()
        {
            Connected = false;

            try { cts?.Cancel(); } catch { /* already disposed */ }
            try { videoClient?.Close(); } catch { /* ignore */ }
            try { controlClient?.Close(); } catch { /* ignore */ }
            try { listener?.Stop(); } catch { /* ignore */ }
            try { MobileServerCleanup(); } catch (Exception ex) { log.Warning(ex, "ADB cleanup failed."); }

            videoClient = null;
            controlClient = null;
            listener = null;
        }

        /// <summary>
        /// 停止Scrcpy服务（幂等，重复调用不会抛异常）
        /// </summary>
        public void Stop()
        {
            if (!Connected)
                return;

            cts?.Cancel();

            // Closing the sockets aborts any blocking read, so the threads exit immediately.
            videoClient?.Close();
            controlClient?.Close();

            videoThread?.Join();
            controlThread?.Join();

            CleanupAfterStop();
        }

        /// <summary>
        /// 停止会话（若在进行中）并释放视频解码器的非托管资源。断开后应及时调用：
        /// 否则解码器只能靠 GC 终结器回收 FFmpeg 上下文（见 VideoStreamDecoder）。
        /// </summary>
        public void Dispose()
        {
            Stop();
            VideoStreamDecoder.Dispose();
        }

        /// <summary>Guards so the "control socket is gone" warning is logged only once per session.</summary>
        private int controlGoneLogged;

        /// <summary>
        /// 发送控制命令
        /// </summary>
        /// <param name="msg">控制消息</param>
        public void SendControlCommand(IControlMessage msg)
        {
            if (controlClient == null)
            {
                // The UI keeps sending input while a session is down; warn once
                // instead of flooding the log with every mouse move.
                if (Interlocked.Exchange(ref controlGoneLogged, 1) == 0)
                    log.Warning("SendControlCommand() called, but controlClient is null (further calls stay silent).");
                return;
            }

            controlChannel.Writer.TryWrite(msg);
        }

        /// <summary>
        /// 读取设备信息：64字节设备名 + 4字节编码器ID + 12字节会话元数据（视频尺寸）
        /// </summary>
        /// <param name="timeoutMs">读取预算，与 Start 的连接超时一致（唤醒中的设备第一包可能很慢）</param>
        private void ReadDeviceInfo(long timeoutMs)
        {
            // 检查videoClient是否为空
            if (videoClient == null)
                throw new Exception("Can't read device info when videoClient is null.");

            // 获取视频流的网络流
            var infoStream = videoClient.GetStream();
            infoStream.ReadTimeout = (int)Math.Clamp(timeoutMs, 2000, int.MaxValue);

            // 64字节设备名（send_device_meta=true）
            var nameBuf = new byte[64];
            if (ReadAll(infoStream, nameBuf, 0, 64) != 64)
                throw new Exception("Failed to read the 64 byte device name.");
            DeviceName = Encoding.UTF8.GetString(nameBuf).TrimEnd('\0');
            log.Information("Device name: " + DeviceName);

            // 4字节编码器ID（send_stream_meta=true），例如 "h264" = 0x68323634
            var codecBuf = new byte[4];
            if (ReadAll(infoStream, codecBuf, 0, 4) != 4)
                throw new Exception("Failed to read the 4 byte codec id.");
            uint codecId = BinaryPrimitives.ReadUInt32BigEndian(codecBuf);

            if (codecId == 0)
                throw new Exception("The device explicitly disabled the video stream.");
            if (codecId == 1)
                throw new Exception("A configuration error occurred on the device.");
            if (codecId != CodecIdH264)
                throw new NotSupportedException($"Unsupported video codec id 0x{codecId:X8}, only h264 is supported.");

            // 12字节会话元数据：首字节最高位为1，包含初始视频尺寸
            var sessionBuf = new byte[12];
            if (ReadAll(infoStream, sessionBuf, 0, 12) != 12)
                throw new Exception("Failed to read the 12 byte session meta.");
            ParseSessionMeta(sessionBuf);

            log.Information($"Initial texture: {Width}x{Height}");
        }

        /// <summary>
        /// 解析会话元数据包（12字节：0x80标志 + client_resized标志 + 宽 + 高）
        /// </summary>
        private void ParseSessionMeta(byte[] header)
        {
            Width = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4..));
            Height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(8..));
            VideoSizeChanged?.Invoke();
        }

        /// <summary>
        /// 视频线程主函数
        /// </summary>
        private void VideoMain()
        {
            try
            {
                VideoLoop();
            }
            catch (Exception ex)
            {
                // The video thread must never let an exception escape: an unhandled
                // exception on a thread takes down the whole process.
                log.Error(ex, "Video thread crashed.");
            }

            OnStreamEnded();
        }

        /// <summary>Video receive loop, split from <see cref="VideoMain"/> so its whole
        /// body — including the stream setup — is covered by the crash guard.</summary>
        private void VideoLoop()
        {
            // Both of these should never happen.
            if (videoClient == null) throw new Exception("videoClient is null.");
            if (cts == null) throw new Exception("cts is null.");

            var videoStream = videoClient.GetStream();
            videoStream.ReadTimeout = 2000;

            int bytesRead;
            var metaBuf = pool.Rent(12);

            Stopwatch sw = new();

            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    // Read the 12 byte frame header (pts+flags and packet size), this might
                    // require more than one .Read() call.
                    try
                    {
                        bytesRead = ReadAll(videoStream, metaBuf, 0, 12);
                    }
                    catch (IOException ex) when (ex.InnerException is SocketException x && x.SocketErrorCode == SocketError.TimedOut)
                    {
                        // Ignore timeout errors while waiting for the next frame.
                        continue;
                    }
                    catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                    {
                        // The socket was closed by Stop() (disconnect / orientation
                        // restart) or the device dropped the connection. Expected
                        // during shutdown, must never escape the thread.
                        log.Information("Video stream ended: {Message}", ex.Message);
                        break;
                    }

                    // 0 bytes = the server closed the connection.
                    if (bytesRead != 12)
                    {
                        log.Error("Video stream closed (read {BytesRead} of 12 header bytes).", bytesRead);
                        break;
                    }

                    // Session meta packets (first byte 0x80) only announce a new video size,
                    // no payload follows.
                    if ((metaBuf[0] & 0x80) != 0)
                    {
                        ParseSessionMeta(metaBuf);
                        log.Information("Video size changed to {Width}x{Height}.", Width, Height);
                        continue;
                    }

                    ulong ptsAndFlags = BinaryPrimitives.ReadUInt64BigEndian(metaBuf.AsSpan());
                    int packetSize = BinaryPrimitives.ReadInt32BigEndian(metaBuf.AsSpan(8..));

                    if (packetSize <= 0)
                    {
                        log.Error("Invalid packet size {PacketSize}.", packetSize);
                        break;
                    }

                    // The most significant bits of the pts carry flags: bit 62 marks a codec
                    // config packet (SPS/PPS), bit 61 marks a key frame.
                    bool isConfigPacket = (ptsAndFlags & PacketFlagConfig) != 0;
                    long presentationTimeUs = isConfigPacket ? -1 : (long)(ptsAndFlags & PacketPtsMask);

                    sw.Restart();

                    // Read the whole frame, this might require more than one .Read() call.
                    var packetBuf = pool.Rent(packetSize);
                    int pos = 0;

                    try
                    {
                        while (pos < packetSize && !cts.Token.IsCancellationRequested)
                        {
                            bytesRead = videoStream.Read(packetBuf, pos, packetSize - pos);

                            if (bytesRead == 0)
                                throw new EndOfStreamException("Unable to read any bytes.");

                            pos += bytesRead;
                        }

                        if (pos == packetSize && !cts.Token.IsCancellationRequested)
                        {
                            VideoStreamDecoder?.Decode(packetBuf, 0, packetSize, presentationTimeUs);
                            log.Verbose("Received and decoded a packet in {@ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
                        }
                    }
                    catch (IOException ex) when (ex.InnerException is SocketException x && x.SocketErrorCode == SocketError.TimedOut)
                    {
                        log.Error(ex, "Timeout in the middle of a video packet, stopping the video thread.");
                        break;
                    }
                    catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                    {
                        // Socket closed mid-frame (Stop()/device disconnect) — exit quietly.
                        log.Information("Video stream ended mid-frame: {Message}", ex.Message);
                        break;
                    }
                    finally
                    {
                        // Runs even when Decode threw, so the buffer never leaks.
                        pool.Return(packetBuf);
                    }
                }
            }
            finally
            {
                pool.Return(metaBuf);
            }
        }

        /// <summary>
        /// Called when the video thread ends, no matter why. Cleans up without joining the
        /// current thread (Stop() would deadlock there) and raises <see cref="Disconnected"/>
        /// once, unless the end was the normal shutdown path of <see cref="Stop"/>.
        /// </summary>
        private void OnStreamEnded()
        {
            // Stop() cancels the token before closing the sockets, so a cancelled token
            // means the normal shutdown path already did (or is doing) the cleanup.
            if (cts == null || cts.IsCancellationRequested)
                return;

            if (Interlocked.Exchange(ref streamEndedHandled, 1) != 0)
                return;

            log.Information("Connection lost, cleaning up.");

            // CleanupAfterStop() is idempotent and never joins threads, but the ADB calls
            // in it can block, so run it off the video thread.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                CleanupAfterStop();

                try { Disconnected?.Invoke(); }
                catch (Exception ex) { log.Error(ex, "A Disconnected event handler threw."); }
            });
        }

        /// <summary>
        /// 控制线程主函数
        /// </summary>
        private async void ControllerMain()
        {
            try
            {
                // Both of these should never happen.
                if (controlClient == null) throw new Exception("controlClient is null.");
                if (cts == null) throw new Exception("cts is null.");

                var stream = controlClient.GetStream();

                await foreach (var cmd in controlChannel.Reader.ReadAllAsync(cts.Token))
                {
                    ControllerSend(stream, cmd);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The socket was closed by Stop() or the device disconnected.
                log.Information("Control stream ended: {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                // This is async void — an escaping exception would kill the process.
                log.Error(ex, "Control thread crashed.");
            }
        }

        /// <summary>
        /// 发送控制命令
        /// </summary>
        /// <param name="stream">网络流</param>
        /// <param name="cmd">控制消息</param>
        private void ControllerSend(NetworkStream stream, IControlMessage cmd)
        {
            try
            {
                var bytes = cmd.ToBytes();
                stream.Write(bytes);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Socket gone (Stop() ran or the device disconnected); drop the message.
                log.Information("Control message dropped, socket closed: {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                // A message that fails to serialize (e.g. text over 300 UTF-8 bytes,
                // scroll values out of range) must not kill the control loop — that
                // would silently swallow every later command of the whole session.
                log.Error(ex, "Dropping a control message that failed to serialize or send.");
            }
        }

        /// <summary>
        /// 读取直到缓冲区填满，返回读取的总字节数（流关闭时可能小于 count）
        /// </summary>
        private static int ReadAll(NetworkStream stream, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int bytesRead = stream.Read(buffer, offset + total, count - total);
                if (bytesRead == 0)
                    break;
                total += bytesRead;
            }
            return total;
        }

        /// <summary>
        /// 设置Scrcpy服务
        /// </summary>
        private void MobileServerSetup()
        {
            MobileServerCleanup();

            // Push scrcpy-server.jar
            UploadMobileServer();

            // Create port reverse rule. The server uses the plain "scrcpy" socket name
            // because we don't pass a scid.
            adb.CreateReverseForward(device, "localabstract:scrcpy", "tcp:" + port, true);
        }

        /// <summary>
        /// 清理Scrcpy服务
        /// </summary>
        private void MobileServerCleanup()
        {
            // Remove only OUR tunnel: the blanket RemoveAll*(device) variants would
            // also tear down unrelated adb rules other tools installed on the device.
            // We never create forwards (tunnel_forward=false), so nothing to remove there.
            adb.RemoveReverseForward(device, "localabstract:scrcpy");
        }

        /// <summary>
        /// 启动Scrcpy服务
        /// </summary>
        private void MobileServerStart()
        {
            log.Information("Starting scrcpy server...");

            var cts = new CancellationTokenSource();
            var receiver = new SerilogOutputReceiver();

            var cmds = new List<string>
                    {
                        "CLASSPATH=/data/local/tmp/scrcpy-server.jar",
                        "app_process",

                        // Unused
                        "/",

                        // App entry point, or something like that.
                        "com.genymobile.scrcpy.Server",

                        // Must match the server version exactly.
                        ServerVersion,

                        "log_level=info",
                        "video=true",
                        "audio=false",
                        "video_codec=h264",
                        $"video_bit_rate={Bitrate}",
                        $"max_size={MaxSize}",
                        "tunnel_forward=false",
                        "control=true",
                        "display_id=0",
                        $"show_touches={ShowTouches}",
                        $"stay_awake={StayAwake}",
                        "power_off_on_close=false",
                        "downsize_on_error=true",
                        // We never read device messages from the control socket, so the
                        // server must not send any (otherwise its write would block eventually).
                        "clipboard_autosync=false",
                        "cleanup=true",
                    };

            if (!string.IsNullOrWhiteSpace(VideoEncoder))
                cmds.Add($"video_encoder={VideoEncoder}");

            if (MaxFps != 0)
                cmds.Add($"max_fps={MaxFps}");

            if (CaptureOrientation != ScrcpyCaptureOrientation.Unlocked)
            {
                cmds.Add("capture_orientation=" + CaptureOrientation switch
                {
                    ScrcpyCaptureOrientation.Initial => "@",
                    ScrcpyCaptureOrientation.Orientation0 => "@0",
                    ScrcpyCaptureOrientation.Orientation90 => "@90",
                    ScrcpyCaptureOrientation.Orientation180 => "@180",
                    ScrcpyCaptureOrientation.Orientation270 => "@270",
                    _ => throw new InvalidOperationException($"Unexpected {nameof(CaptureOrientation)} value."),
                });
            }

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
