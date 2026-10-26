using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SortiFileWpf.Core
{
    public static class ShellThumbnailHelper
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c07c4259")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(
                [In, MarshalAs(UnmanagedType.Struct)] SIZE size,
                [In] SIIGBF flags,
                [Out] out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
            public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; }
        }

        [Flags]
        private enum SIIGBF
        {
            SIIGBF_RESIZETOFIT = 0x00,
            SIIGBF_BIGGERSIZEOK = 0x01,
            SIIGBF_MEMORYONLY = 0x02,
            SIIGBF_ICONONLY = 0x04,
            SIIGBF_THUMBNAILONLY = 0x08,
            SIIGBF_INCACHEONLY = 0x10
        }

        private static readonly Guid IShellItemImageFactoryGuid = new Guid("bcc18b79-ba16-442f-80c4-8a59c07c4259");

        public static ImageSource? GetThumbnail(string filePath, int width = 200, int height = 200)
        {
            if (!File.Exists(filePath)) return null;

            string ext = Path.GetExtension(filePath).ToLowerInvariant().TrimStart('.');
            bool isImage = ext is "png" or "jpg" or "jpeg" or "gif" or "webp" or "bmp" or "tiff" or "ico";
            bool isVideo = ext is "mp4" or "mkv" or "avi" or "mov" or "wmv" or "webm" or "flv" or "m4v";

            if (isImage)
            {
                try
                {
                    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = width;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
                catch
                {
                    // Fall back to Shell API below
                }
            }

            // 1. Try Windows Shell thumbnail extraction (works when Windows has shell extensions registered)
            try
            {
                int hr = SHCreateItemFromParsingName(filePath, IntPtr.Zero, IShellItemImageFactoryGuid, out var factory);
                if (hr == 0 && factory != null)
                {
                    var size = new SIZE(width, height);
                    hr = factory.GetImage(size, SIIGBF.SIIGBF_BIGGERSIZEOK, out IntPtr hBitmap);
                    if (hr == 0 && hBitmap != IntPtr.Zero)
                    {
                        try
                        {
                            var bs = Imaging.CreateBitmapSourceFromHBitmap(
                                hBitmap,
                                IntPtr.Zero,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            bs.Freeze();
                            return bs;
                        }
                        finally
                        {
                            DeleteObject(hBitmap);
                        }
                    }
                }
            }
            catch
            {
                // Fallback below
            }

            // 2. Video frame extraction fallback via WPF DirectShow / Media Foundation
            // When media players like VLC override file associations, Windows Explorer shell thumbnail handlers
            // are missing from the registry. WPF MediaPlayer extracts the real frame directly.
            if (isVideo)
            {
                try
                {
                    var videoFrame = GrabVideoFrame(filePath, width, height);
                    if (videoFrame != null)
                    {
                        return videoFrame;
                    }
                }
                catch
                {
                    // Fallback to null
                }
            }

            return null;
        }

        /// <summary>
        /// Native WPF DirectShow/MediaFoundation frame grabber for videos.
        /// Runs on an isolated STA thread to safely seek and render a frame to RenderTargetBitmap.
        /// </summary>
        public static BitmapSource? GrabVideoFrame(string filePath, int targetWidth = 200, int targetHeight = 200)
        {
            if (!File.Exists(filePath)) return null;

            BitmapSource? result = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var player = new MediaPlayer();
                    player.ScrubbingEnabled = true;

                    player.MediaOpened += (s, e) =>
                    {
                        double totalSec = player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan.TotalSeconds : 2.0;
                        double seekPos = Math.Min(1.0, Math.Max(0.1, totalSec * 0.1));
                        player.Position = TimeSpan.FromSeconds(seekPos);
                    };

                    player.MediaFailed += (s, e) =>
                    {
                        player.Close();
                        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background);
                    };

                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(100)
                    };

                    int attempts = 0;
                    timer.Tick += (s, e) =>
                    {
                        attempts++;
                        if (player.NaturalVideoWidth > 0 && player.NaturalVideoHeight > 0)
                        {
                            timer.Stop();
                            int w = targetWidth;
                            int h = (int)(targetWidth * (double)player.NaturalVideoHeight / player.NaturalVideoWidth);
                            if (h <= 0) h = targetHeight;

                            var visual = new DrawingVisual();
                            using (var ctx = visual.RenderOpen())
                            {
                                ctx.DrawVideo(player, new Rect(0, 0, w, h));
                            }

                            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                            rtb.Render(visual);
                            rtb.Freeze();
                            result = rtb;
                            player.Close();
                            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background);
                        }
                        else if (attempts > 30) // 3 seconds timeout
                        {
                            timer.Stop();
                            player.Close();
                            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background);
                        }
                    };

                    player.Open(new Uri(filePath));
                    player.Pause();
                    timer.Start();

                    System.Windows.Threading.Dispatcher.Run();
                }
                catch
                {
                    // Ignore errors, return null
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            // Allow up to 3.5 seconds for video frame extraction
            if (!thread.Join(3500))
            {
                try { thread.Interrupt(); } catch { }
            }

            return result;
        }
    }
}
