using System.Windows;
using System.Windows.Media;

namespace VenomDesktop;

public sealed class VenomCreatureView : FrameworkElement
{
    private readonly LoopbackAudioAnalyzer _audio;
    private AudioSnapshot _snapshot = new();
    private double _time;
    private double _breath;
    private double _mood;

    public Point CenterPosition { get; set; } = new Point(500, 500);

    public struct TentacleData
    {
        public Point EndPoint;
        public bool IsAttached;
        public double SagPhase;
    }

    private List<TentacleData> _tentacles = new();

    public void UpdateTentacles(List<TentacleData> tentacles)
    {
        _tentacles = tentacles;
    }

    public VenomCreatureView(LoopbackAudioAnalyzer audio)
    {
        _audio = audio;
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
    }

    public void Tick(double dt)
    {
        _time += dt;
        _snapshot = _audio.GetSnapshot();
        var targetBreath = 0.035 + _snapshot.Bass * 0.16 + _snapshot.Impact * 0.22;
        _breath += (targetBreath - _breath) * (1 - Math.Exp(-dt * 5.5));
        _mood += ((_snapshot.HasSignal ? 1 : 0) - _mood) * (1 - Math.Exp(-dt * 1.8));
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var center = CenterPosition;
        var radius = 35.0 + _breath * 15.0; // Smaller base radius
        var bands = _snapshot.Bands.Length > 0 ? _snapshot.Bands : new float[96];
        var contour = BuildContour(center, radius, bands, 240, 0.95);
        var shadow = BuildContour(center, radius * 1.03, bands, 180, 0.48);

        DrawTentacles(dc, center, true);
        DrawBodyShadow(dc, shadow);
        DrawTentacles(dc, center, false);
        DrawBody(dc, contour, center);
        DrawEyes(dc, center, radius);
    }

    private Point[] BuildContour(Point center, double radius, float[] bands, int points, double spikeScale)
    {
        var contour = new Point[points];
        for (var i = 0; i < points; i++)
        {
            var t = (double)i / points;
            var angle = t * Math.PI * 2;
            var dir = new Vector(Math.Cos(angle), Math.Sin(angle));
            var band = bands[Math.Min(bands.Length - 1, (int)(t * bands.Length))];
            var quiet = 1 - _mood;
            var lowOrganic =
                Math.Sin(angle * 1.35 + _time * 0.38) * 0.075 * quiet +
                Math.Sin(angle * 2.15 - _time * 0.52) * 0.048 * quiet +
                Math.Sin(angle * 3.6 + _time * 0.24) * 0.026;
            var pore = Math.Pow(Math.Max(0, band - 0.08), 1.65);
            var localNeedle = Math.Pow(Math.Max(0, band), 2.15) * spikeScale;
            var fine = Math.Sin(angle * 41.0 + _time * (2.5 + band * 5.0)) * 0.5 + 0.5;
            var sag = Math.Sin(angle - _time * 0.2) * 0.025 * quiet;
            var soundGrowth = pore * 0.095 + localNeedle * (0.03 + fine * 0.07);
            var r = radius * (1 + lowOrganic + sag + soundGrowth + _snapshot.Impact * 0.022);
            var p = Polar(center, angle, r);

            Vector pull = new Vector();
            foreach (var tent in _tentacles)
            {
                var toTent = tent.EndPoint - center;
                if (toTent.Length > radius * 0.3)
                {
                    var tentDir = toTent;
                    tentDir.Normalize();
                    var dot = Vector.Multiply(dir, tentDir);
                    if (dot > 0)
                    {
                        var influence = Math.Pow(dot, 5.0);
                        pull += toTent * (influence * 0.45);
                    }
                }
            }

            contour[i] = new Point(p.X + pull.X, p.Y + pull.Y);
        }

        return contour;
    }

    private static StreamGeometry SmoothClosedGeometry(Point[] points)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        if (points.Length == 0) return geometry;

        context.BeginFigure(points[0], true, true);
        for (var i = 0; i < points.Length; i++)
        {
            var current = points[i];
            var next = points[(i + 1) % points.Length];
            var control = new Point((current.X + next.X) * 0.5, (current.Y + next.Y) * 0.5);
            context.QuadraticBezierTo(current, control, true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static void DrawBodyShadow(DrawingContext dc, Point[] points)
    {
        var geometry = SmoothClosedGeometry(points);
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)), null, geometry);
    }

    private void DrawTentacles(DrawingContext dc, Point center, bool isShadow)
    {
        foreach (var tent in _tentacles)
        {
            var toTent = tent.EndPoint - center;
            var dist = toTent.Length;
            if (dist < 5) continue;

            var dir = toTent;
            dir.Normalize();
            var perp = new Vector(-dir.Y, dir.X);

            var wobble = tent.IsAttached ? 0.02 : 0.15;
            var sagDist = Math.Sin(_time * 12.0 + tent.SagPhase) * dist * wobble;
            var control = center + toTent * 0.5 + perp * sagDist;

            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                ctx.BeginFigure(center, false, false);
                ctx.QuadraticBezierTo(control, tent.EndPoint, true, false);
            }

            if (isShadow)
            {
                var shadowPen = new Pen(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), 16) 
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawGeometry(null, shadowPen, geom);
            }
            else
            {
                var penOuter = new Pen(new SolidColorBrush(Color.FromRgb(0, 0, 0)), 14) 
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                var penInner = new Pen(new SolidColorBrush(Color.FromRgb(20, 20, 20)), 6) 
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                var penCore = new Pen(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), 1) 
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

                dc.DrawGeometry(null, penOuter, geom);
                dc.DrawGeometry(null, penInner, geom);
                if (tent.IsAttached) 
                {
                    dc.DrawGeometry(null, penCore, geom);
                }
            }
        }
    }

    private void DrawBody(DrawingContext dc, Point[] contour, Point center)
    {
        var body = SmoothClosedGeometry(contour);
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.34 + Math.Sin(_time * 0.6) * 0.04, 0.25),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.72,
            RadiusY = 0.72,
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(40, 40, 40), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(5, 5, 5), 0.48));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0, 0, 0), 1));

        var rim = new Pen(new SolidColorBrush(Color.FromArgb(255, 0, 0, 0)), 4.2)
        {
            LineJoin = PenLineJoin.Round,
        };
        dc.DrawGeometry(brush, rim, body);

        var wetHighlight = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.36, 0.25),
            Center = new Point(0.36, 0.25),
            RadiusX = 0.32,
            RadiusY = 0.26,
        };
        wetHighlight.GradientStops.Add(new GradientStop(Color.FromArgb(80, 255, 255, 255), 0));
        wetHighlight.GradientStops.Add(new GradientStop(Color.FromArgb(15, 150, 150, 150), 0.46));
        wetHighlight.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
        dc.DrawGeometry(wetHighlight, null, body);
    }

    private void DrawEyes(DrawingContext dc, Point center, double radius)
    {
        var eyeScale = radius * 0.5;
        
        var leftEye = new StreamGeometry();
        using (var ctx = leftEye.Open())
        {
            ctx.BeginFigure(new Point(center.X - eyeScale * 0.2, center.Y + eyeScale * 0.1), true, true);
            ctx.QuadraticBezierTo(new Point(center.X - eyeScale * 0.5, center.Y - eyeScale * 0.8), new Point(center.X - eyeScale * 0.9, center.Y - eyeScale * 0.4), true, false);
            ctx.QuadraticBezierTo(new Point(center.X - eyeScale * 0.8, center.Y + eyeScale * 0.3), new Point(center.X - eyeScale * 0.2, center.Y + eyeScale * 0.1), true, false);
        }
        dc.DrawGeometry(Brushes.White, null, leftEye);

        var rightEye = new StreamGeometry();
        using (var ctx = rightEye.Open())
        {
            ctx.BeginFigure(new Point(center.X + eyeScale * 0.2, center.Y + eyeScale * 0.1), true, true);
            ctx.QuadraticBezierTo(new Point(center.X + eyeScale * 0.5, center.Y - eyeScale * 0.8), new Point(center.X + eyeScale * 0.9, center.Y - eyeScale * 0.4), true, false);
            ctx.QuadraticBezierTo(new Point(center.X + eyeScale * 0.8, center.Y + eyeScale * 0.3), new Point(center.X + eyeScale * 0.2, center.Y + eyeScale * 0.1), true, false);
        }
        dc.DrawGeometry(Brushes.White, null, rightEye);
    }

    private static Point Polar(Point center, double angle, double radius)
    {
        return new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
    }

}
