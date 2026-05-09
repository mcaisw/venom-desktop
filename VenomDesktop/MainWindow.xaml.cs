using System.Windows;
using System.Windows.Media;

namespace VenomDesktop;

public partial class MainWindow : Window
{
    private readonly LoopbackAudioAnalyzer _audio = new();
    private readonly Random _random = new();
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private VenomCreatureView? _creature;
    private DateTime _lastFrame = DateTime.UtcNow;
    private Point _target;
    private Vector _velocity;
    private double _nextTargetIn;
    private Point _creatureCenter;
    private double _wanderPhase;
    private bool _isSearchingAnchor;
    private double _listenState;
    private bool _isDragging;
    private Point _dragOffset;
    private bool _isClickThrough = true;

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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

    private bool IsGrabbable(IntPtr hdc, int x, int y)
    {
        uint c1 = GetPixel(hdc, x, y);
        uint c2 = GetPixel(hdc, x + 4, y);
        uint c3 = GetPixel(hdc, x, y + 4);
        
        if (c1 == 0xFFFFFFFF || c2 == 0xFFFFFFFF || c3 == 0xFFFFFFFF) return false;

        int r1 = (int)(c1 & 0xFF), g1 = (int)((c1 >> 8) & 0xFF), b1 = (int)((c1 >> 16) & 0xFF);
        int r2 = (int)(c2 & 0xFF), g2 = (int)((c2 >> 8) & 0xFF), b2 = (int)((c2 >> 16) & 0xFF);
        int r3 = (int)(c3 & 0xFF), g3 = (int)((c3 >> 8) & 0xFF), b3 = (int)((c3 >> 16) & 0xFF);

        int diff1 = Math.Abs(r1 - r2) + Math.Abs(g1 - g2) + Math.Abs(b1 - b2);
        int diff2 = Math.Abs(r1 - r3) + Math.Abs(g1 - g3) + Math.Abs(b1 - b3);
        
        return diff1 > 25 || diff2 > 25; 
    }

    public MainWindow()
    {
        InitializeComponent();
        SetupTrayIcon();
        Loaded += OnLoaded;
        Closed += (_, _) => 
        {
            _audio.Dispose();
            _trayIcon?.Dispose();
        };
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new System.Windows.Forms.NotifyIcon();
        var exePath = System.Environment.ProcessPath;
        try
        {
            if (!string.IsNullOrEmpty(exePath))
                _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
        }
        catch { }

        if (_trayIcon.Icon == null)
        {
            _trayIcon.Icon = System.Drawing.SystemIcons.Application;
        }

        _trayIcon.Text = "Venom Desktop";
        _trayIcon.Visible = true;

        var menu = new System.Windows.Forms.ContextMenuStrip();
        
        var startupItem = new System.Windows.Forms.ToolStripMenuItem("开机启动 (Run on Startup)")
        {
            CheckOnClick = true,
            Checked = CheckStartup()
        };
        startupItem.CheckedChanged += (s, e) => ToggleStartup(startupItem.Checked);

        var exitItem = new System.Windows.Forms.ToolStripMenuItem("退出 (Exit)");
        exitItem.Click += (s, e) => Application.Current.Shutdown();

        menu.Items.Add(startupItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenuStrip = menu;
    }

    private bool CheckStartup()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue("VenomDesktop") != null;
        }
        catch { return false; }
    }

    private void ToggleStartup(bool enable)
    {
        try
        {
            var path = System.Environment.ProcessPath;
            if (string.IsNullOrEmpty(path)) return;
            
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                if (enable)
                    key.SetValue("VenomDesktop", $"\"{path}\"");
                else
                    key.DeleteValue("VenomDesktop", false);
            }
        }
        catch { }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        System.Windows.Interop.HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        var extendedStyle = GetWindowLong(hwnd, -20);
        SetWindowLong(hwnd, -20, extendedStyle | 0x00000020); // WS_EX_TRANSPARENT
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmNchittest = 0x0084;
        const int htClient = 1;
        const int htTransparent = -1;

        if (msg != wmNchittest || _isClickThrough)
        {
            return IntPtr.Zero;
        }

        var x = (short)((long)lParam & 0xffff);
        var y = (short)(((long)lParam >> 16) & 0xffff);
        var distance = (new Point(x, y) - _creatureCenter).Length;

        handled = true;
        return distance <= GetGrabRadius() || _isDragging
            ? new IntPtr(htClient)
            : new IntPtr(htTransparent);
    }

    private double GetGrabRadius()
    {
        return 96 + Math.Min(90, _listenState * 80);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _creature = new VenomCreatureView(_audio);
        _creature.IsHitTestVisible = true;
        _creature.MouseLeftButtonDown += Creature_MouseLeftButtonDown;
        _creature.MouseMove += Creature_MouseMove;
        _creature.MouseLeftButtonUp += Creature_MouseLeftButtonUp;
        Root.Children.Add(_creature);

        var work = SystemParameters.WorkArea;
        _creatureCenter = new Point(work.Left + work.Width / 2, work.Top + work.Height / 2);

        PickNewTarget();
        _audio.Start();

        CompositionTarget.Rendering += OnRendering;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private void SetClickThrough(bool clickThrough)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var extendedStyle = GetWindowLong(hwnd, -20);
        int newStyle = clickThrough ? (extendedStyle | 0x00000020) : (extendedStyle & ~0x00000020);
        
        if (extendedStyle != newStyle)
        {
            SetWindowLong(hwnd, -20, newStyle);
            // SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0004 | 0x0010 | 0x0020);
        }
    }

    private void Creature_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_listenState >= 0.4) 
        {
            _isDragging = true;
            var pos = e.GetPosition(this);
            _dragOffset = new Point(pos.X - _creatureCenter.X, pos.Y - _creatureCenter.Y);
            _creature?.CaptureMouse();
        }
    }

    private void Creature_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDragging)
        {
            var pos = e.GetPosition(this);
            _creatureCenter = new Point(pos.X - _dragOffset.X, pos.Y - _dragOffset.Y);
            _velocity = new Vector(0, 0); 
        }
    }

    private void Creature_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            _creature?.ReleaseMouseCapture();
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = Math.Clamp((now - _lastFrame).TotalSeconds, 0.001, 0.05);
        _lastFrame = now;

        if (!_isDragging)
        {
            Roam(dt);
        }
        else
        {
            var snapshot = _audio.GetSnapshot();
            _listenState += ((snapshot.HasSignal ? 1 : 0) - _listenState) * (1 - Math.Exp(-dt * 2.0));
            if (_creature != null)
            {
                _creature.CenterPosition = _creatureCenter;
            }
        }

        bool shouldBeClickThrough = (_listenState < 0.4) && !_isDragging;
        if (_isClickThrough != shouldBeClickThrough)
        {
            _isClickThrough = shouldBeClickThrough;
            SetClickThrough(_isClickThrough);
        }

        _creature?.Tick(dt);
    }

    private void Roam(double dt)
    {
        var work = SystemParameters.WorkArea;
        _nextTargetIn -= dt;
        var toTarget = _target - _creatureCenter;

        var snapshot = _audio.GetSnapshot();
        _listenState += ((snapshot.HasSignal ? 1 : 0) - _listenState) * (1 - Math.Exp(-dt * 2.0));

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
            if (a.IsAttached && ((a.ScreenPos - _creatureCenter).Length < 80 || a.LifeTime <= 0 || _listenState > 0.4))
            {
                a.IsDetaching = true;
            }

            if (a.IsDetaching)
            {
                a.Extension -= 5.0 * dt; 
                if (_listenState > 0.4) a.Extension -= 10.0 * dt; // retract faster when listening
                if (a.Extension <= 0) _anchors.RemoveAt(i);
            }
        }

        if (_listenState < 0.4)
        {
            if (!_isSearchingAnchor && _anchors.Count < 8 && toTarget.Length > 20)
            {
                if (_random.NextDouble() < 15.0 * dt) 
                {
                    _isSearchingAnchor = true;
                    var dir = toTarget;
                    dir.Normalize();
                    var center = _creatureCenter;
                    var left = (int)work.Left;
                    var right = (int)work.Right - 1;
                    var top = (int)work.Top;
                    var bottom = (int)work.Bottom - 1;

                    Task.Run(() => 
                    {
                        IntPtr hdc = GetDC(IntPtr.Zero);
                        var foundAnchors = new List<Point>();
                        var localRandom = new Random();
                        
                        for (int attempt = 0; attempt < 35 && foundAnchors.Count < 3; attempt++)
                        {
                            var angle = Math.Atan2(dir.Y, dir.X) + (localRandom.NextDouble() - 0.5) * 3.5;
                            var dist = 30 + localRandom.NextDouble() * 130; 
                            int px = (int)(center.X + Math.Cos(angle) * dist);
                            int py = (int)(center.Y + Math.Sin(angle) * dist);
                            
                            px = Math.Clamp(px, left, right);
                            py = Math.Clamp(py, top, bottom);

                            if (IsGrabbable(hdc, px, py))
                            {
                                foundAnchors.Add(new Point(px, py));
                            }
                        }
                        ReleaseDC(IntPtr.Zero, hdc);

                        Dispatcher.InvokeAsync(() => 
                        {
                            foreach (var p in foundAnchors)
                            {
                                _anchors.Add(new TentacleAnchor {
                                    ScreenPos = p,
                                    Extension = 0.0,
                                    Speed = 7.0 + localRandom.NextDouble() * 4.0,
                                    SagPhase = localRandom.NextDouble() * Math.PI * 2,
                                    LifeTime = 1.0 + localRandom.NextDouble() * 2.0
                                });
                            }
                            _isSearchingAnchor = false;
                        });
                    });
                }
            }
        }

        var force = new Vector(0, 0);
        foreach (var a in _anchors)
        {
            if (a.IsAttached && !a.IsDetaching)
            {
                var pull = a.ScreenPos - _creatureCenter;
                force += pull * 6.0; 
            }
        }

        if (_listenState < 0.4)
        {
            _wanderPhase += dt;
            force.X += Math.Sin(_wanderPhase * 2.3) * 50.0;
            force.Y += Math.Sin(_wanderPhase * 1.7) * 50.0 + 35.0; 
        }

        _velocity += force * dt;
        if (_listenState >= 0.4)
        {
            _velocity -= _velocity * 12.0 * dt; // Heavy friction to stay still
        }
        else
        {
            _velocity -= _velocity * 6.0 * dt; 
        }

        _creatureCenter.X = Math.Clamp(_creatureCenter.X + _velocity.X * dt, work.Left + 15, work.Right - 15);
        _creatureCenter.Y = Math.Clamp(_creatureCenter.Y + _velocity.Y * dt, work.Top + 15, work.Bottom - 15);

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
