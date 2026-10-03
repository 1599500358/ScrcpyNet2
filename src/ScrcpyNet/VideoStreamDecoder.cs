using FFmpeg.AutoGen.Abstractions;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ScrcpyNet
{
    public unsafe class FrameData : IDisposable
    {
        private bool disposed;

        /// <summary>
        /// Byte array with the frame data in BGRA32 format.
        /// WARNING: This field is only valid inside the OnFrame event. After this is finished the Data will be freed.
        /// If you need the Data field outside the OnFrame event then make sure to copy it somewhere.
        /// </summary>
        /// 
        public IntPtr Ptr=>(IntPtr)data;
        public int DataSize=>length;
        public ReadOnlySpan<byte> Data
        {
            get
            {
                // This line might not be needed?
                if (disposed) throw new ObjectDisposedException(nameof(FrameData));
                return new ReadOnlySpan<byte>(data, length);
            }
        }

        public int Width { get; }
        public int Height { get; }
        public long FrameNumber { get; }
        public AVPixelFormat PixelFormat { get; }

        private readonly byte* data;
        private readonly int length;

        public FrameData(byte* data, int length, int width, int height, long frameNumber, AVPixelFormat pixelFormat)
        {
            this.data = data;
            this.length = length;
            Width = width;
            Height = height;
            FrameNumber = frameNumber;
            PixelFormat = pixelFormat;
        }

        ~FrameData()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects)
                }

                // Free unmanaged resources (unmanaged objects) and override finalizer
                ffmpeg.av_free(data);

                disposed = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    public unsafe class VideoStreamDecoder : IDisposable
    {
        public Scrcpy? Scrcpy { get; set; }

        /// <summary>
        /// Number of the last decoded frame.
        /// </summary>
        public int FrameCount { get; private set; }

        public event EventHandler<FrameData>? OnFrame;

        private bool disposed;
        private int lastFrameRefCount;
        private readonly object lastFrameLock = new();
        private SwsContext* swsContext = null;
        private FrameData? lastFrame;

        private readonly AVCodec* codec;
        private readonly AVCodecParserContext* parser;
        private readonly AVCodecContext* ctx;
        private readonly AVFrame* _frame;
        private readonly AVFrame* _receivedFrame;
        private readonly AVPacket* packet;


        private static readonly ILogger log = Log.ForContext<VideoStreamDecoder>();

        public VideoStreamDecoder()
        {
            // This library calls FFmpeg through FFmpeg.AutoGen.Abstractions, whose function
            // vectors are only installed by DynamicallyLoadedBindings.Initialize(). The
            // native dlls (avcodec-63 & co) are shipped in a "ScrcpyNet" folder next to the
            // application, fall back to it when the bindings were not configured already.
            if (string.IsNullOrEmpty(DynamicallyLoadedBindings.LibrariesPath))
                DynamicallyLoadedBindings.LibrariesPath = Path.Combine(AppContext.BaseDirectory, "ScrcpyNet");
            DynamicallyLoadedBindings.Initialize();

            ConfigureHWDecoder(out var deviceType);
            
            codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
            if (codec == null) throw new Exception("Couldn't find AVCodec for AV_CODEC_ID_H264.");

            parser = ffmpeg.av_parser_init(codec->id);
            if (parser == null) throw new Exception("Couldn't initialize AVCodecParserContext.");

            ctx = ffmpeg.avcodec_alloc_context3(codec);
            if (ctx == null) throw new Exception("Couldn't allocate AVCodecContext.");
            if (deviceType != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
            {
                int retHw = ffmpeg.av_hwdevice_ctx_create(&ctx->hw_device_ctx, deviceType, null, null, 0);
                if (retHw < 0)
                {
                    // Not fatal: decoding continues in software.
                    log.Warning("Couldn't create the {DeviceType} hardware device, falling back to software decoding.", deviceType);
                    ctx->hw_device_ctx = null;
                }
            }
            int ret = ffmpeg.avcodec_open2(ctx, codec, null);
            if (ret < 0) throw new Exception("Couldn't open AVCodecContext.");
            _frame = ffmpeg.av_frame_alloc();
            _receivedFrame = ffmpeg.av_frame_alloc();

            if (_frame == null) throw new Exception("Couldn't allocate AVFrame.");

            packet = ffmpeg.av_packet_alloc();
            if (_frame == null) throw new Exception("Couldn't allocate AVPacket.");
        }

        ~VideoStreamDecoder()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public FrameData? GetLastFrame()
        {
            lock (lastFrameLock)
            {
                lastFrameRefCount++;
                return lastFrame;
            }
        }

        private static bool IsHardwarePixelFormat(AVPixelFormat format)
        {
            // Hardware formats (dxva2_vld, d3d11, vaapi, ...) have AV_PIX_FMT_FLAG_HWACCEL set
            // and contain a device buffer instead of raw pixels, so they can't be fed to
            // swscale directly.
            var desc = ffmpeg.av_pix_fmt_desc_get(format);
            return desc != null && (desc->flags & ffmpeg.AV_PIX_FMT_FLAG_HWACCEL) != 0;
        }

        private static void ConfigureHWDecoder(out AVHWDeviceType HWtype)
        {
            HWtype = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
            var availableHWDecoders = new Dictionary<int, AVHWDeviceType>();

       
                Console.WriteLine("Select hardware decoder:");
                var type = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
                var number = 0;

                while ((type = ffmpeg.av_hwdevice_iterate_types(type)) != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
                {
                    Console.WriteLine($"{++number}. {type}");
                    availableHWDecoders.Add(number, type);
                }

                if (availableHWDecoders.Count == 0)
                {
                    Console.WriteLine("Your system have no hardware decoders.");
                    HWtype = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
                    return;
                }

                var decoderNumber = availableHWDecoders
                    .SingleOrDefault(t => t.Value == AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2).Key;
                if (decoderNumber == 0)
                    decoderNumber = availableHWDecoders.First().Key;
                Console.WriteLine($"Selected [{decoderNumber}]");
                //int.TryParse(Console.ReadLine(), out var inputDecoderNumber);
                availableHWDecoders.TryGetValue(decoderNumber,
                    out HWtype);
            
        }

        public void Decode(byte[] data, long pts = -1)
        {
            Decode(data, 0, data.Length, pts);
        }

        public void Decode(byte[] data, int index, int count, long pts = -1)
        {
            fixed (byte* dataPtr = data)
            {
                byte* ptr = dataPtr + index;
                int dataSize = count;

                while (dataSize > 0)
                {
                    int ret = ffmpeg.av_parser_parse2(parser, ctx, &packet->data, &packet->size, ptr, dataSize, pts != -1 ? pts : ffmpeg.AV_NOPTS_VALUE, ffmpeg.AV_NOPTS_VALUE, 0);

                    if (ret < 0)
                        throw new Exception("Error while parsing.");

                    ptr += ret;
                    dataSize -= ret;

                    if (packet->size != 0)
                    {
                        DecodePacket();
                    }
                }
            }
        }

        private void DecodePacket()
        {
            int ret = ffmpeg.avcodec_send_packet(ctx, packet);

            if (ret != ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                if (ret < 0)
                {
                    byte[] errorMessageBytes = new byte[512];
                    fixed (byte* ptr = errorMessageBytes)
                    {
                        ffmpeg.av_strerror(ret, ptr, (ulong)errorMessageBytes.Length);
                        string errorMessage = new((sbyte*)ptr, 0, errorMessageBytes.Length - 1, Encoding.ASCII);
                        log.Error("Error sending a packet for decoding. {@ErrorMessage}", errorMessage);
                    }

                    return;
                }

                while (ret >= 0)
                {
                    ret = ffmpeg.avcodec_receive_frame(ctx, _frame);

                    // AVERROR and AVERROR_EOF are already negative error codes here —
                    // wrapping AVERROR_EOF in AVERROR() again would make it positive and
                    // the comparison would never match.
                    if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                        return;

                    if (ret < 0)
                    {
                        log.Error("Error receiving a frame from the decoder ({ErrorCode}).", ret);
                        return;
                    }

                    AVFrame frame;
                    if (IsHardwarePixelFormat((AVPixelFormat)_frame->format))
                    {
                        // The decoder produced a hardware surface, copy it to system memory.
                        ffmpeg.av_frame_unref(_receivedFrame);
                        int retTransfer = ffmpeg.av_hwframe_transfer_data(_receivedFrame, _frame, 0);
                        if (retTransfer < 0)
                        {
                            log.Error("av_hwframe_transfer_data failed ({ErrorCode}), dropping the frame.", retTransfer);
                            continue;
                        }
                        frame = *_receivedFrame;
                    }
                    else
                    {
                        // Software decoding (or the hardware pipeline failed and the decoder
                        // fell back to software, which we can't detect via hw_device_ctx).
                        frame = *_frame;
                    }

                    FrameCount++;

                    if (Scrcpy != null)
                    {
                        Scrcpy.Width = frame.width;
                        Scrcpy.Height = frame.height;
                    }

                    int destSize = 4 * frame.width * frame.height;
                    int[] destStride = new int[] { 4 * frame.width };

                    // In my tests the code crashed when we use a C# byte-array (new byte[])
                    byte* destBufferPtr = (byte*)ffmpeg.av_malloc((ulong)destSize);
                    byte*[] dest = { destBufferPtr };

                    // This `free`s the old context if needed, so there is no leak here.
                    swsContext = ffmpeg.sws_getCachedContext(swsContext, frame.width, frame.height, (AVPixelFormat)frame.format, frame.width, frame.height, AVPixelFormat.AV_PIX_FMT_BGRA, (int)SwsFlags.SWS_BICUBIC, null, null, null);

                    if (swsContext == null) throw new Exception("Couldn't allocate SwsContext.");

                    int outputSliceHeight = ffmpeg.sws_scale(swsContext, frame.data, frame.linesize, 0, frame.height, dest, destStride);

                    if (outputSliceHeight > 0)
                    {
                        // Poor man's reference counting.
                        lock (lastFrameLock)
                        {
                            //if (lastFrame != null && Interlocked.Read(ref lastFrameRefCount) == 0)
                            if (lastFrame != null && lastFrameRefCount == 0)
                            {
                                // We don't have to dispose it, but then the GC will remove all old frames after 'some time'.
                                // On my 32GB RAM computer the GC allowed the app to use up to 8GB before cleaning it up.
                                lastFrame.Dispose();
                            }
                            //Interlocked.Exchange(ref lastFrameRefCount, 0);
                            lastFrameRefCount = 0;
                        }

                        // FrameData takes ownership of the destBufferPtr and will free it when disposed!
                        lastFrame = new FrameData(destBufferPtr, destSize, frame.width, frame.height, ctx->frame_num, AVPixelFormat.AV_PIX_FMT_BGRA);
                        OnFrame?.Invoke(this, lastFrame);
                    }
                    //else
                    //{
                    //    log.Warning("outputSliceHeight == 0, not sure if this is bad?");

                    //    // Manually free the destBufferPtr when we don't create a FrameData object.
                    //    ffmpeg.av_free(destBufferPtr);
                    //}
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects)
                }

                // Free unmanaged resources (unmanaged objects) and override finalizer
                ffmpeg.av_parser_close(parser);
                ffmpeg.sws_freeContext(swsContext);

                fixed (AVCodecContext** ptr = &ctx)
                    ffmpeg.avcodec_free_context(ptr);

                fixed (AVFrame** ptr = &_frame)
                    ffmpeg.av_frame_free(ptr);

                fixed (AVPacket** ptr = &packet)
                    ffmpeg.av_packet_free(ptr);

                disposed = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
