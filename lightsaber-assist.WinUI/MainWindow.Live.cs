using lightsaber_assist.Core.Analysis;
using lightsaber_assist.Vision;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace lightsaber_assist.WinUI
{
    internal sealed record LiveFrame(TimeSpan Timestamp, int Width, int Height, byte[] Bgra);

    internal sealed class LiveCameraSession : IDisposable
    {
        private const int MaxBuffered = 12;
        private readonly MediaCapture capture;
        private readonly MediaFrameReader reader;
        private readonly object gate = new();
        private readonly List<LiveFrame> frames = [];

        private LiveCameraSession(MediaCapture capture, MediaFrameReader reader, string name)
        {
            this.capture = capture;
            this.reader = reader;
            Name = name;
        }

        public string Name { get; }

        public FrameAnalyzer? Analyzer { get; set; }

        public static async Task<LiveCameraSession> StartAsync(MediaFrameSourceGroup group)
        {
            var capture = new MediaCapture();
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = group,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            });
            var source = capture.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color)
                ?? throw new InvalidOperationException("Aucun flux vidéo disponible sur " + group.DisplayName + ".");
            var reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            var session = new LiveCameraSession(capture, reader, group.DisplayName);
            reader.FrameArrived += session.OnFrameArrived;
            var status = await reader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
            {
                session.Dispose();
                throw new InvalidOperationException($"Démarrage impossible pour {group.DisplayName} ({status}).");
            }

            return session;
        }

        public TimeSpan? LatestTimestamp
        {
            get
            {
                lock (gate)
                {
                    return frames.Count == 0 ? null : frames[^1].Timestamp;
                }
            }
        }

        public LiveFrame? PickClosest(TimeSpan target)
        {
            lock (gate)
            {
                return frames.Count == 0
                    ? null
                    : frames.MinBy(f => Math.Abs((f.Timestamp - target).Ticks));
            }
        }

        private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            using var mediaFrame = sender.TryAcquireLatestFrame();
            var bitmap = mediaFrame?.VideoMediaFrame?.SoftwareBitmap;
            if (mediaFrame is null || bitmap is null)
            {
                return;
            }

            SoftwareBitmap? converted = null;
            try
            {
                if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || bitmap.BitmapAlphaMode == BitmapAlphaMode.Straight)
                {
                    converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                    bitmap = converted;
                }

                var data = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyToBuffer(data.AsBuffer());
                var frame = new LiveFrame(mediaFrame.SystemRelativeTime ?? TimeSpan.Zero, bitmap.PixelWidth, bitmap.PixelHeight, data);
                lock (gate)
                {
                    frames.Add(frame);
                    if (frames.Count > MaxBuffered)
                    {
                        frames.RemoveAt(0);
                    }
                }
            }
            finally
            {
                converted?.Dispose();
            }
        }

        public void Dispose()
        {
            reader.FrameArrived -= OnFrameArrived;
            reader.Dispose();
            capture.Dispose();
        }
    }
}
