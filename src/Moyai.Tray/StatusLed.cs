using System.Drawing.Drawing2D;

namespace Moyai.Tray;

/// <summary>4色のピル型LEDを生成・共有し、終了時に破棄します。</summary>
internal sealed class StatusLed : IDisposable
{
    private readonly Bitmap _running = Draw(Color.FromArgb(40, 190, 70));
    private readonly Bitmap _notResponding = Draw(Color.FromArgb(240, 170, 30));
    private readonly Bitmap _error = Draw(Color.FromArgb(215, 50, 45));
    private readonly Bitmap _stopped = Draw(Color.FromArgb(150, 150, 150));

    internal Image For(ServerStatus? status) => status?.State switch
    { ServerState.Running => _running, ServerState.NotResponding => _notResponding, ServerState.Error => _error, _ => _stopped };

    private static Color Light(Color color) => Color.FromArgb(
        color.R + (int)((255 - color.R) * .6), color.G + (int)((255 - color.G) * .6), color.B + (int)((255 - color.B) * .6));
    private static Color Dark(Color color, double amount) => Color.FromArgb((int)(color.R * (1 - amount)), (int)(color.G * (1 - amount)), (int)(color.B * (1 - amount)));

    private static Bitmap Draw(Color color)
    {
        var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        var body = new RectangleF(.5f, 4.5f, 15, 7);
        using var path = Pill(body);
        using var fill = new LinearGradientBrush(body, Light(color), Dark(color, .1), LinearGradientMode.Vertical);
        using var rim = new Pen(Dark(color, .45), 1);
        graphics.FillPath(fill, path);
        graphics.DrawPath(rim, path);
        using var highlight = Pill(new RectangleF(body.X + 3, body.Y + 1.2f, body.Width - 6, 2));
        using var white = new SolidBrush(Color.FromArgb(160, Color.White));
        graphics.FillPath(white, highlight);
        return bitmap;
    }

    private static GraphicsPath Pill(RectangleF bounds)
    {
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, bounds.Height, bounds.Height, 90, 180);
        path.AddArc(bounds.Right - bounds.Height, bounds.Y, bounds.Height, bounds.Height, 270, 180);
        path.CloseFigure();
        return path;
    }

    public void Dispose() { _running.Dispose(); _notResponding.Dispose(); _error.Dispose(); _stopped.Dispose(); }
}
