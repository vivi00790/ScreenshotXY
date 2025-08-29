using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Media.Imaging;

namespace ScreenshotXY.Interop;

internal static class BitmapHelpers
{
    public static BitmapSource ToBitmapSource(Bitmap bitmap, double dpiX = 96, double dpiY = 96)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var bs = BitmapSource.Create(
                data.Width, data.Height, dpiX, dpiY,
                System.Windows.Media.PixelFormats.Pbgra32, null,
                data.Scan0, data.Stride * data.Height, data.Stride);
            bs.Freeze();
            return bs;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    
    }
}