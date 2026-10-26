using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NSEdge.App.Services;

internal static class UiPreview
{
    public static void Save(FrameworkElement visual, string path, int minWidth = 250, int minHeight = 180)
    {
        visual.UpdateLayout();
        DpiScale dpi = VisualTreeHelper.GetDpi(visual);
        int width = (int)Math.Ceiling(visual.ActualWidth * dpi.DpiScaleX);
        int height = (int)Math.Ceiling(visual.ActualHeight * dpi.DpiScaleY);
        if (width < minWidth || height < minHeight)
            throw new InvalidOperationException("UI preview has invalid dimensions.");

        var bitmap = new RenderTargetBitmap(width, height,
            96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var output = new PngBitmapEncoder();
        output.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using FileStream stream = File.Create(path);
        output.Save(stream);
    }
}
