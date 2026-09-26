// 앱 아이콘(app.ico) 생성기: 파란 둥근 사각형 + 겹친 말풍선 2개(대량 발송) + 점 3개
// 사용: dotnet run --project tools/IconGen -- <출력 .ico 경로> [미리보기 .png 경로]
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

int[] sizes = [16, 24, 32, 48, 64, 128, 256];
var icoPath = args.Length > 0 ? args[0] : "app.ico";
var pngs = sizes.Select(s => (Size: s, Png: Render(s))).ToList();

using (var fs = File.Create(icoPath))
using (var w = new BinaryWriter(fs))
{
    w.Write((short)0); w.Write((short)1); w.Write((short)pngs.Count);
    int offset = 6 + 16 * pngs.Count;
    foreach (var (size, png) in pngs)
    {
        w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)0); w.Write((byte)0);
        w.Write((short)1); w.Write((short)32);
        w.Write(png.Length); w.Write(offset);
        offset += png.Length;
    }
    foreach (var (_, png) in pngs) w.Write(png);
}
if (args.Length > 1) File.WriteAllBytes(args[1], pngs.Last().Png);
Console.WriteLine($"{icoPath}: {string.Join(",", sizes)}");

static byte[] Render(int size)
{
    var visual = new DrawingVisual();
    using (var dc = visual.RenderOpen())
    {
        dc.PushTransform(new ScaleTransform(size / 256.0, size / 256.0));

        var bg = new LinearGradientBrush(Color.FromRgb(0x3B, 0x82, 0xF6), Color.FromRgb(0x1D, 0x4E, 0xD8), 45);
        dc.DrawRoundedRectangle(bg, null, new Rect(8, 8, 240, 240), 54, 54);

        // 뒤 말풍선 (반투명) — 여러 명에게 보내는 느낌
        var back = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF));
        dc.DrawGeometry(back, null, Bubble(new Rect(40, 46, 140, 100), 30, tailLeft: true));

        // 앞 말풍선
        dc.DrawGeometry(Brushes.White, null, Bubble(new Rect(72, 94, 148, 106), 32, tailLeft: false));

        var dot = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
        foreach (var x in new[] { 110.0, 146, 182 }) dc.DrawEllipse(dot, null, new Point(x, 147), 12, 12);
    }
    var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    bmp.Render(visual);
    var enc = new PngBitmapEncoder();
    enc.Frames.Add(BitmapFrame.Create(bmp));
    using var ms = new MemoryStream();
    enc.Save(ms);
    return ms.ToArray();
}

static Geometry Bubble(Rect r, double radius, bool tailLeft)
{
    var body = new RectangleGeometry(r, radius, radius);
    var tail = new StreamGeometry();
    using (var g = tail.Open())
    {
        if (tailLeft)
        {
            g.BeginFigure(new Point(r.Left + 26, r.Bottom - 20), true, true);
            g.LineTo(new Point(r.Left + 10, r.Bottom + 24), true, false);
            g.LineTo(new Point(r.Left + 62, r.Bottom - 4), true, false);
        }
        else
        {
            g.BeginFigure(new Point(r.Right - 26, r.Bottom - 20), true, true);
            g.LineTo(new Point(r.Right - 8, r.Bottom + 26), true, false);
            g.LineTo(new Point(r.Right - 64, r.Bottom - 4), true, false);
        }
    }
    return new CombinedGeometry(GeometryCombineMode.Union, body, tail);
}
