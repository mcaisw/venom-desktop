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

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        var pt = hitTestParameters.HitPoint;
        var dist = (pt - CenterPosition).Length;
        if (dist < 180) // Generous grab radius around the creature
        {
            return new PointHitTestResult(this, pt);
        }
        return null;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var center = CenterPosition;
        var radius = 14.0 + _breath * 6.0; // Small!
        var bands = _snapshot.Bands.Length > 0 ? _snapshot.Bands : new float[96];
        var contour = BuildContour(center, radius, bands, 360, 1.0);
        var shadow = BuildContour(center, radius * 1.1, bands, 360, 0.4);

        DrawTentacles(dc, center, true);
        DrawBodyShadow(dc, shadow);
        DrawTentacles(dc, center, false);
        DrawBody(dc, contour, center, radius);
        DrawEyes(dc, center, radius);
    }

    private Point[] BuildContour(Point center, double radius, float[] bands, int points, double spikeScale)
    {
        var contour = new Point[points];
        for (var i = 0; i < points; i++)
        {
            var t = (double)i / points;
            var angle = t * Math.PI * 2 - Math.PI / 2;
            var dir = new Vector(Math.Cos(angle), Math.Sin(angle));
            
            var bumpCount = 24.0;
            var bumpFloat = t * bumpCount;
            var bumpIndex = (int)Math.Floor(bumpFloat);
            var localT = bumpFloat - bumpIndex; 
            
            var bumpShape = Math.Sin(localT * Math.PI); 
            var chaos1 = Math.Sin(localT * Math.PI * 3 + _time * 12.0) * 0.2;
            var chaos2 = Math.Sin(localT * Math.PI * 5 - _time * 17.0) * 0.1;
            
            var organicShape = Math.Max(0, bumpShape + (chaos1 + chaos2) * bumpShape);
            organicShape = Math.Pow(organicShape, 1.4);

            var symBump = bumpIndex < (bumpCount/2) ? bumpIndex : (int)bumpCount - bumpIndex;
            var bandIdx = symBump * 3;
            if (bandIdx >= bands.Length) bandIdx = bands.Length - 1;
            var band = bands[bandIdx];

            var quiet = 1 - _mood;
            var lowOrganic = Math.Sin(angle * 3 + _time * 1.5) * 0.02 +
                             Math.Cos(angle * 5 - _time * 2.1) * 0.015;
            lowOrganic *= quiet;
            
            var sag = (Math.Sin(angle) * 0.5 + 0.5) * 0.06 * quiet;

            var wiggle = Math.Sin(_time * 10.0 + bumpIndex * 2.3) * 0.2 + 0.8;
            var barHeight = Math.Pow(Math.Max(0, band), 1.3) * 55.0 * _mood * organicShape * wiggle * spikeScale;
            var crawlSpike = Math.Pow(Math.Max(0, band), 2.0) * 0.5 * quiet * spikeScale;

            var r = radius * (1 + lowOrganic + sag + crawlSpike + _snapshot.Impact * 0.05) + barHeight;
            var p = Polar(center, angle, r);

            Vector maxPull = new Vector();
            foreach (var tent in _tentacles)
            {
                var toTent = tent.EndPoint - center;
                if (toTent.Length > radius * 0.3)
                {
                    var dist = toTent.Length;
                    var tentDir = toTent;
                    tentDir.Normalize();
                    var perp = new Vector(-tentDir.Y, tentDir.X);
                    var wobble = tent.IsAttached ? 0.08 : 0.25;
                    var sagPhase = tent.SagPhase + _time * (tent.IsAttached ? 5.0 : 15.0);
                    var sagDist = Math.Sin(sagPhase) * dist * wobble;
                    
                    var ctrl1 = (toTent * 0.33) + (perp * sagDist);
                    var pullDir = ctrl1;
                    pullDir.Normalize();
                    
                    var dot = Vector.Multiply(dir, pullDir);
                    if (dot > 0)
                    {
                        var influence = Math.Pow(dot, 5.0);
                        var currentPull = ctrl1 * (influence * 0.6);
                        if (currentPull.LengthSquared > maxPull.LengthSquared)
                        {
                            maxPull = currentPull;
                        }
                    }
                }
            }

            contour[i] = new Point(p.X + maxPull.X, p.Y + maxPull.Y);
        }

        return contour;
    }

    private static StreamGeometry SmoothClosedGeometry(Point[] points)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        if (points.Length == 0) return geometry;

        context.BeginFigure(points[0], true, true);
        var pc = new PointCollection(points);
        context.PolyLineTo(pc, true, false);

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
        var tentacleFill = new SolidColorBrush(Color.FromRgb(20, 20, 20));
        var tentacleEdge = new Pen(new SolidColorBrush(Color.FromRgb(0, 0, 0)), 4.2) { LineJoin = PenLineJoin.Round };
        var shadowBrush = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));

        foreach (var tent in _tentacles)
        {
            var toTent = tent.EndPoint - center;
            var dist = toTent.Length;
            if (dist < 5) continue;

            var dir = toTent;
            dir.Normalize();
            var perp = new Vector(-dir.Y, dir.X);

            var wobble = tent.IsAttached ? 0.08 : 0.25;
            var sagPhase = tent.SagPhase + _time * (tent.IsAttached ? 5.0 : 15.0);
            var sagDist = Math.Sin(sagPhase) * dist * wobble;
            
            var ctrl1 = center + toTent * 0.33 + perp * sagDist;
            var ctrl2 = center + toTent * 0.66 - perp * (sagDist * 0.5);

            int segments = 20;
            var pts = new Point[segments + 1];
            for (int i = 0; i <= segments; i++) {
                double t = (double)i / segments;
                double u = 1 - t;
                pts[i] = new Point(
                    u*u*u*center.X + 3*u*u*t*ctrl1.X + 3*u*t*t*ctrl2.X + t*t*t*tent.EndPoint.X,
                    u*u*u*center.Y + 3*u*u*t*ctrl1.Y + 3*u*t*t*ctrl2.Y + t*t*t*tent.EndPoint.Y
                );
            }

            var leftPts = new Point[segments + 1];
            var rightPts = new Point[segments + 1];
            
            for (int i = 0; i <= segments; i++) {
                Vector tangent;
                if (i == 0) tangent = pts[1] - pts[0];
                else if (i == segments) tangent = pts[segments] - pts[segments - 1];
                else tangent = pts[i + 1] - pts[i - 1];
                tangent.Normalize();
                var normal = new Vector(-tangent.Y, tangent.X);
                
                var t = (double)i / segments;
                var thickness = 6.0 * Math.Pow(1 - t, 1.5) + 1.0; 
                if (isShadow) thickness += 2.0;
                
                leftPts[i] = pts[i] + normal * thickness;
                rightPts[i] = pts[i] - normal * thickness;
            }

            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                ctx.BeginFigure(leftPts[0], true, true);
                var pc = new PointCollection();
                for (int i = 1; i <= segments; i++) pc.Add(leftPts[i]);
                
                Vector finalTangent = pts[segments] - pts[segments - 1];
                finalTangent.Normalize();
                pc.Add(pts[segments] + finalTangent * 2.0); 
                
                for (int i = segments; i >= 0; i--) pc.Add(rightPts[i]);
                ctx.PolyLineTo(pc, true, false);
            }

            if (isShadow)
            {
                dc.DrawGeometry(shadowBrush, null, geom);
            }
            else
            {
                dc.DrawGeometry(tentacleFill, tentacleEdge, geom);
                
                var coreGeom = new StreamGeometry();
                using (var ctx = coreGeom.Open())
                {
                    ctx.BeginFigure(pts[0], false, false);
                    var corePc = new PointCollection();
                    for (int i = 1; i < segments - 3; i++) corePc.Add(pts[i] - new Vector(0, 1.5));
                    if (corePc.Count > 0) ctx.PolyLineTo(corePc, true, false);
                }
                var wetPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawGeometry(null, wetPen, coreGeom);
            }
        }
    }

    private void DrawBody(DrawingContext dc, Point[] contour, Point center, double radius)
    {
        var body = SmoothClosedGeometry(contour);
        
        var brush = new SolidColorBrush(Color.FromRgb(20, 20, 20));
        var rim = new Pen(new SolidColorBrush(Color.FromArgb(255, 0, 0, 0)), 4.2)
        {
            LineJoin = PenLineJoin.Round,
        };
        dc.DrawGeometry(brush, rim, body);

        var globalLight = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            GradientOrigin = new Point(0.35, 0.25),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.8,
            RadiusY = 0.8,
        };
        globalLight.GradientStops.Add(new GradientStop(Color.FromArgb(25, 255, 255, 255), 0));
        globalLight.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
        dc.DrawGeometry(globalLight, null, body);

        var wetHighlight = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            GradientOrigin = new Point(center.X - radius * 0.28, center.Y - radius * 0.5),
            Center = new Point(center.X - radius * 0.28, center.Y - radius * 0.5),
            RadiusX = radius * 0.9,
            RadiusY = radius * 0.7,
        };
        wetHighlight.GradientStops.Add(new GradientStop(Color.FromArgb(50, 255, 255, 255), 0));
        wetHighlight.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.7));
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
