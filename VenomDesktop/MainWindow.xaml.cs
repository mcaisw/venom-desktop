using System.Windows;
using System.Windows.Media;

namespace VenomDesktop;

public partial class MainWindow : Window
{
    private readonly LoopbackAudioAnalyzer _audio = new();
    private readonly Random _random = new();
    private VenomCreatureView? _creature;
    private DateTime _lastFrame = DateTime.UtcNow;
    private Point _target;
    private Vector _velocity;
    private double _nextTargetIn;
    private Point _creatureCenter;

    public class TentacleAnchor
    {
        public Point ScreenPos;
        public double Extension; 
        public double Speed;
        public bool IsAttached => Extension >= 1.0;
        public bool IsDetaching;
        public double SagPhase;
        public double LifeTime;
    }

    private readonly List<TentacleAnchor> _anchors = new();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += (_, _) => _audio.Dispose();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLong(hwnd, -20);
        SetWindowLong(hwnd, -20, extendedStyle | 0x00000020); // WS_EX_TRANSPARENT
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _creature = new VenomCreatureView(_audio);
        Root.Children.Add(_creature);

        var work = SystemParameters.WorkArea;
        _creatureCenter = new Point(work.Left + work.Width / 2, work.Top + work.Height / 2);

        PickNewTarget();
        _audio.Start();

        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = Math.Clamp((now - _lastFrame).TotalSeconds, 0.001, 0.05);
        _lastFrame = now;

        Roam(dt);
        _creature?.Tick(dt);
    }

    private void Roam(double dt)
    {
        var work = SystemParameters.WorkArea;
        _nextTargetIn -= dt;
        var toTarget = _target - _creatureCenter;

        if (_nextTargetIn <= 0 || toTarget.Length < 100)
        {
            PickNewTarget();
            toTarget = _target - _creatureCenter;
        }

        for (int i = _anchors.Count - 1; i >= 0; i--)
        {
            var a = _anchors[i];
            if (!a.IsAttached && !a.IsDetaching)
            {
                a.Extension += a.Speed * dt;
                if (a.Extension >= 1.0) a.Extension = 1.0;
            }
            
            a.LifeTime -= dt;
            if (a.IsAttached && ((a.ScreenPos - _creatureCenter).Length < 80 || a.LifeTime <= 0))
            {
                a.IsDetaching = true;
            }

            if (a.IsDetaching)
            {
                a.Extension -= 5.0 * dt; 
                if (a.Extension <= 0) _anchors.RemoveAt(i);
            }
        }

        if (_anchors.Count < 3 && toTarget.Length > 50)
        {
            if (_random.NextDouble() < 6.0 * dt) 
            {
                var dir = toTarget;
                dir.Normalize();
                var angle = Math.Atan2(dir.Y, dir.X) + (_random.NextDouble() - 0.5) * 1.5;
                var dist = 180 + _random.NextDouble() * 200; // Smaller distance
                var anchorPos = _creatureCenter + new Vector(Math.Cos(angle), Math.Sin(angle)) * dist;
                
                anchorPos.X = Math.Clamp(anchorPos.X, work.Left + 10, work.Right - 10);
                anchorPos.Y = Math.Clamp(anchorPos.Y, work.Top + 10, work.Bottom - 10);

                _anchors.Add(new TentacleAnchor {
                    ScreenPos = anchorPos,
                    Extension = 0.0,
                    Speed = 5.0 + _random.NextDouble() * 3.0,
                    SagPhase = _random.NextDouble() * Math.PI * 2,
                    LifeTime = 1.0 + _random.NextDouble() * 2.5
                });
            }
        }

        var force = new Vector(0, 0);
        foreach (var a in _anchors)
        {
            if (a.IsAttached && !a.IsDetaching)
            {
                var pull = a.ScreenPos - _creatureCenter;
                force += pull * 14.0; 
            }
        }

        force.Y += 120.0 * Math.Sin(DateTime.UtcNow.TimeOfDay.TotalSeconds * 1.5);

        _velocity += force * dt;
        _velocity -= _velocity * 6.0 * dt; 

        _creatureCenter.X = Math.Clamp(_creatureCenter.X + _velocity.X * dt, work.Left + 50, work.Right - 50);
        _creatureCenter.Y = Math.Clamp(_creatureCenter.Y + _velocity.Y * dt, work.Top + 50, work.Bottom - 50);

        if (_creature != null)
        {
            _creature.CenterPosition = _creatureCenter;
            var tentacles = new List<VenomCreatureView.TentacleData>();
            foreach (var a in _anchors)
            {
                var currentEnd = new Point(
                    _creatureCenter.X + (a.ScreenPos.X - _creatureCenter.X) * a.Extension,
                    _creatureCenter.Y + (a.ScreenPos.Y - _creatureCenter.Y) * a.Extension);
                
                tentacles.Add(new VenomCreatureView.TentacleData {
                    EndPoint = currentEnd,
                    IsAttached = a.IsAttached,
                    SagPhase = a.SagPhase
                });
            }
            _creature.UpdateTentacles(tentacles);
        }
    }

    private void PickNewTarget()
    {
        var work = SystemParameters.WorkArea;
        var margin = 100.0;
        _target = new Point(
            _random.NextDouble() * Math.Max(1, work.Width - margin * 2) + work.Left + margin,
            _random.NextDouble() * Math.Max(1, work.Height - margin * 2) + work.Top + margin);
        _nextTargetIn = 3.0 + _random.NextDouble() * 5.0;
    }

}
