using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace StrikeRoulette
{
    // =========================================================================
    // In-Memory Data Models (No external files created)
    // =========================================================================
    public class RouletteItemData
    {
        public string Name { get; set; }
        public bool Enabled { get; set; }

        public RouletteItemData()
        {
            Name = "";
            Enabled = true;
        }

        public RouletteItemData(string name, bool enabled)
        {
            Name = name;
            Enabled = enabled;
        }
    }

    public class RoulettePreset
    {
        public string PresetName { get; set; }
        public List<RouletteItemData> Items { get; set; }

        public RoulettePreset()
        {
            PresetName = "기본 프리셋";
            Items = new List<RouletteItemData>();
        }
    }

    public class AppSessionState
    {
        public List<RoulettePreset> Presets { get; set; }
        public int SelectedPresetIndex { get; set; }
        public int TargetStreak { get; set; }
        public int BestOfWins { get; set; } // 선승제: 먼저 이 횟수만큼 뽑히면 승리 (2 = 2선승)
        public int SpinSpeedMode { get; set; }
        public bool SoundEnabled { get; set; }
        public bool SplitSmallPool { get; set; }
        public bool AutoMemoOnWin { get; set; }
        public bool AutoNewRoundAfterWin { get; set; }
        public string MemoText { get; set; }

        public AppSessionState()
        {
            Presets = new List<RoulettePreset>();
            SelectedPresetIndex = 0;
            TargetStreak = 3;
            BestOfWins = 2;
            SpinSpeedMode = 1; // 빠름 (0.7초) 기본값
            SoundEnabled = true;
            SplitSmallPool = true;
            AutoMemoOnWin = true;
            AutoNewRoundAfterWin = true;
            MemoText = "";
        }
    }

    // =========================================================================
    // Runtime Slice & History Models
    // =========================================================================
    public class WheelSlice
    {
        public string Name;
        public Color SliceColor;
        public int OriginalItemIndex;
    }

    public class SpinRecord
    {
        public int SpinNumber;
        public int RoundNumber;
        public string ItemName;
        public Color ItemColor;
        public int StreakCount;
        public int TargetAtSpin;
        public bool ChangedFromPrev;
        public bool IsTargetHit;
        public bool WasSingleMode;
        public bool WasBestOf; // 선승제 기록: StreakCount = 해당 항목 누적 승수, TargetAtSpin = 필요 승수
        public int GameNo;
        public DateTime Timestamp;
    }

    // =========================================================================
    // In-Memory Synthesized Sound Effects (Zero external files)
    // =========================================================================
    public static class SoundEffects
    {
        private static SoundPlayer _tickPlayer;
        private static SoundPlayer _reachPlayer;
        private static SoundPlayer _winPlayer;
        private static long _lastTickMs;
        private static Stopwatch _sw;

        public static void Initialize()
        {
            _sw = Stopwatch.StartNew();
            _lastTickMs = -1000;
            try
            {
                _tickPlayer = CreatePlayer(BuildTickWav());
                _reachPlayer = CreatePlayer(BuildReachWav());
                _winPlayer = CreatePlayer(BuildWinWav());
            }
            catch
            {
                // Ignore if audio device unavailable
            }
        }

        private static SoundPlayer CreatePlayer(byte[] wavBytes)
        {
            MemoryStream ms = new MemoryStream(wavBytes);
            SoundPlayer sp = new SoundPlayer(ms);
            sp.Load();
            return sp;
        }

        public static void PlayTick(bool enabled)
        {
            if (!enabled || _tickPlayer == null) return;
            long now = _sw.ElapsedMilliseconds;
            if (now - _lastTickMs < 48) return;
            _lastTickMs = now;
            try { _tickPlayer.Play(); } catch { }
        }

        public static void PlayReach(bool enabled)
        {
            if (!enabled || _reachPlayer == null) return;
            try { _reachPlayer.Play(); } catch { }
        }

        public static void PlayWin(bool enabled)
        {
            if (!enabled || _winPlayer == null) return;
            try { _winPlayer.Play(); } catch { }
        }

        private static byte[] BuildTickWav()
        {
            int sampleRate = 44100;
            int samples = (int)(sampleRate * 0.014);
            short[] pcm = new short[samples];
            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / sampleRate;
                double env = Math.Exp(-t * 260.0);
                double freq = 950.0 - 450.0 * ((double)i / samples);
                double val = Math.Sin(2.0 * Math.PI * freq * t) * env * 0.24;
                pcm[i] = (short)(val * 32767);
            }
            return EncodeWav(pcm, sampleRate);
        }

        private static byte[] BuildReachWav()
        {
            int sampleRate = 44100;
            double[] freqs = new double[] { 659.25, 880.0 }; // E5 -> A5
            double[] durs = new double[] { 0.07, 0.11 };
            return BuildMelodyWav(freqs, durs, sampleRate, 0.28);
        }

        private static byte[] BuildWinWav()
        {
            int sampleRate = 44100;
            double[] freqs = new double[] { 523.25, 659.25, 783.99, 1046.50 }; // C5 E5 G5 C6
            double[] durs = new double[] { 0.075, 0.075, 0.085, 0.22 };
            return BuildMelodyWav(freqs, durs, sampleRate, 0.32);
        }

        private static byte[] BuildMelodyWav(double[] freqs, double[] durs, int sampleRate, double volume)
        {
            List<short> all = new List<short>();
            for (int n = 0; n < freqs.Length; n++)
            {
                int count = (int)(sampleRate * durs[n]);
                for (int i = 0; i < count; i++)
                {
                    double t = (double)i / sampleRate;
                    double progress = (double)i / count;
                    double attack = progress < 0.08 ? (progress / 0.08) : 1.0;
                    double decay = Math.Exp(-progress * 3.2);
                    double wave = Math.Sin(2.0 * Math.PI * freqs[n] * t) * 0.8
                                + Math.Sin(2.0 * Math.PI * freqs[n] * 2.0 * t) * 0.2;
                    double sample = wave * attack * decay * volume;
                    all.Add((short)(sample * 32767));
                }
            }
            return EncodeWav(all.ToArray(), sampleRate);
        }

        private static byte[] EncodeWav(short[] pcm, int sampleRate)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                int dataSize = pcm.Length * 2;
                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(36 + dataSize);
                bw.Write(Encoding.ASCII.GetBytes("WAVE"));
                bw.Write(Encoding.ASCII.GetBytes("fmt "));
                bw.Write(16);
                bw.Write((short)1); // PCM
                bw.Write((short)1); // Mono
                bw.Write(sampleRate);
                bw.Write(sampleRate * 2);
                bw.Write((short)2);
                bw.Write((short)16);
                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(dataSize);
                for (int i = 0; i < pcm.Length; i++)
                {
                    bw.Write(pcm[i]);
                }
                return ms.ToArray();
            }
        }
    }

    // =========================================================================
    // Custom Vector Roulette Wheel Control (60fps Hardware-Accelerated)
    // =========================================================================
    public class RouletteWheelControl : FrameworkElement
    {
        public List<WheelSlice> Slices = new List<WheelSlice>();
        public double WheelRotationDeg = 0.0;
        public double NeedleKickDeg = 0.0;
        public bool IsWinnerHighlight = false;

        private readonly Typeface _boldTypeface = new Typeface(
            new FontFamily("Malgun Gothic"),
            FontStyles.Normal,
            FontWeights.Bold,
            FontStretches.Normal);

        public RouletteWheelControl()
        {
            MinWidth = 220;
            MinHeight = 220;
            SnapsToDevicePixels = true;
            Focusable = false;
        }

        public int GetCurrentSliceIndex()
        {
            if (Slices == null || Slices.Count == 0) return -1;
            double sliceDeg = 360.0 / Slices.Count;
            double relAngle = ((-WheelRotationDeg) % 360.0 + 360.0) % 360.0;
            int idx = (int)Math.Floor(relAngle / sliceDeg);
            if (idx < 0) idx = 0;
            if (idx >= Slices.Count) idx = Slices.Count - 1;
            return idx;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 20 || h <= 20) return;

            Point center = new Point(w / 2.0, h / 2.0 + 6.0);
            double radius = Math.Min(w, h) / 2.0 - 24.0;
            if (radius < 60) radius = 60;

            // Flat rim ring (accent green on win)
            Color rimCol = IsWinnerHighlight ? Color.FromRgb(108, 203, 158) : Color.FromRgb(42, 44, 51);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(17, 18, 22)), new Pen(new SolidColorBrush(rimCol), 2.0), center, radius + 8, radius + 8);

            if (Slices == null || Slices.Count == 0)
            {
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(26, 27, 32)), null, center, radius, radius);
                FormattedText emptyFt = MakeText("왼쪽에서 항목을 추가하세요", 15, Color.FromRgb(160, 163, 172));
                dc.DrawText(emptyFt, new Point(center.X - emptyFt.Width / 2, center.Y - emptyFt.Height / 2));
                return;
            }

            int count = Slices.Count;
            double sliceDeg = 360.0 / count;
            int activeIdx = GetCurrentSliceIndex();

            if (count == 1)
            {
                Brush singleBrush = new SolidColorBrush(Slices[0].SliceColor);
                dc.DrawEllipse(singleBrush, new Pen(Brushes.White, 2), center, radius, radius);
                FormattedText ft1 = MakeText(Slices[0].Name, 20, Colors.White);
                dc.DrawText(ft1, new Point(center.X - ft1.Width / 2, center.Y - radius * 0.55 - ft1.Height / 2));
            }
            else
            {
                Pen dividerPen = new Pen(new SolidColorBrush(Color.FromRgb(17, 18, 22)), 1.5);

                for (int i = 0; i < count; i++)
                {
                    double startDeg = WheelRotationDeg - 90.0 + i * sliceDeg;

                    StreamGeometry geom = CreatePieSliceGeometry(center, radius, startDeg, sliceDeg);
                    Color baseCol = Slices[i].SliceColor;
                    if (IsWinnerHighlight && i == activeIdx)
                    {
                        baseCol = LightenColor(baseCol, 0.22);
                    }

                    Brush fillBrush = new SolidColorBrush(baseCol);
                    dc.DrawGeometry(fillBrush, dividerPen, geom);

                    if (IsWinnerHighlight && i == activeIdx)
                    {
                        Pen winSlicePen = new Pen(Brushes.White, 3.0);
                        dc.DrawGeometry(null, winSlicePen, geom);
                    }

                    double midDeg = startDeg + sliceDeg / 2.0;
                    DrawSliceLabel(dc, center, radius, midDeg, sliceDeg, Slices[i].Name);
                }
            }

            // Small flat dividers on the rim (one per slice)
            if (count >= 2 && count <= 24)
            {
                Brush pegBrush = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
                for (int p = 0; p < count; p++)
                {
                    double pegDeg = WheelRotationDeg - 90.0 + p * (360.0 / count);
                    double rad = pegDeg * Math.PI / 180.0;
                    Point pegPt = new Point(
                        center.X + (radius + 4) * Math.Cos(rad),
                        center.Y + (radius + 4) * Math.Sin(rad));
                    dc.DrawEllipse(pegBrush, null, pegPt, 2.0, 2.0);
                }
            }

            // Center Hub (shows live slice under pointer!)
            double hubRadius = Math.Max(36.0, radius * 0.23);
            Pen hubBorder = new Pen(
                new SolidColorBrush(IsWinnerHighlight ? Color.FromRgb(108, 203, 158) : Color.FromRgb(58, 61, 70)),
                2.0);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(17, 18, 22)), hubBorder, center, hubRadius, hubRadius);

            if (activeIdx >= 0 && activeIdx < Slices.Count)
            {
                string curName = TruncateText(Slices[activeIdx].Name, 7);
                FormattedText hubTitle = MakeText(curName, Math.Max(11.5, hubRadius * 0.30), Colors.White);
                dc.DrawText(hubTitle, new Point(center.X - hubTitle.Width / 2.0, center.Y - hubTitle.Height / 2.0));
            }

            // Top Pointer Needle (12 o'clock with physics kick)
            Point pivot = new Point(center.X, center.Y - radius - 12.0);
            dc.PushTransform(new RotateTransform(NeedleKickDeg, pivot.X, pivot.Y));

            StreamGeometry needleGeom = new StreamGeometry();
            using (StreamGeometryContext ctx = needleGeom.Open())
            {
                ctx.BeginFigure(new Point(pivot.X, pivot.Y + 30.0), true, true);
                ctx.LineTo(new Point(pivot.X - 13.0, pivot.Y - 6.0), true, true);
                ctx.LineTo(new Point(pivot.X + 13.0, pivot.Y - 6.0), true, true);
            }
            needleGeom.Freeze();

            Brush needleBrush = new SolidColorBrush(Color.FromRgb(244, 244, 246));
            Pen needlePen = new Pen(new SolidColorBrush(Color.FromRgb(17, 18, 22)), 2.0);
            dc.DrawGeometry(needleBrush, needlePen, needleGeom);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(91, 108, 240)), needlePen, pivot, 5.0, 5.0);

            dc.Pop();
        }

        private void DrawSliceLabel(DrawingContext dc, Point center, double radius, double midDeg, double sliceDeg, string rawText)
        {
            string label = TruncateText(rawText, 11);
            double fontSize = 15.0;
            if (Slices.Count > 10) fontSize = 12.5;
            if (Slices.Count > 16) fontSize = 10.5;
            if (label.Length > 7 && fontSize > 12.5) fontSize = 12.5;

            FormattedText ft = MakeText(label, fontSize, Colors.White);
            FormattedText shadowFt = MakeText(label, fontSize, Color.FromArgb(110, 0, 0, 0));

            double normDeg = ((midDeg % 360.0) + 360.0) % 360.0;
            bool flip = (normDeg > 90.0 && normDeg < 270.0);

            double textRadius = radius * 0.63;
            double rad = midDeg * Math.PI / 180.0;
            Point pos = new Point(
                center.X + textRadius * Math.Cos(rad),
                center.Y + textRadius * Math.Sin(rad));

            double rotAngle = flip ? (midDeg + 180.0) : midDeg;

            dc.PushTransform(new TranslateTransform(pos.X, pos.Y));
            dc.PushTransform(new RotateTransform(rotAngle));

            Point origin = new Point(-ft.Width / 2.0, -ft.Height / 2.0);
            dc.DrawText(shadowFt, new Point(origin.X + 1.2, origin.Y + 1.2));
            dc.DrawText(ft, origin);

            dc.Pop();
            dc.Pop();
        }

        private FormattedText MakeText(string text, double size, Color color)
        {
            return new FormattedText(
                text ?? "",
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                _boldTypeface,
                size,
                new SolidColorBrush(color));
        }

        private static string TruncateText(string s, int maxChars)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= maxChars) return s;
            return s.Substring(0, maxChars - 1) + "…";
        }

        private static StreamGeometry CreatePieSliceGeometry(Point center, double radius, double startDeg, double sweepDeg)
        {
            double startRad = startDeg * Math.PI / 180.0;
            double endRad = (startDeg + sweepDeg) * Math.PI / 180.0;

            Point pStart = new Point(
                center.X + radius * Math.Cos(startRad),
                center.Y + radius * Math.Sin(startRad));
            Point pEnd = new Point(
                center.X + radius * Math.Cos(endRad),
                center.Y + radius * Math.Sin(endRad));

            bool isLargeArc = sweepDeg > 180.0;

            StreamGeometry geom = new StreamGeometry();
            using (StreamGeometryContext ctx = geom.Open())
            {
                ctx.BeginFigure(center, true, true);
                ctx.LineTo(pStart, true, true);
                ctx.ArcTo(pEnd, new Size(radius, radius), 0.0, isLargeArc, SweepDirection.Clockwise, true, true);
            }
            geom.Freeze();
            return geom;
        }

        private static Color LightenColor(Color c, double amount)
        {
            byte r = (byte)Math.Min(255, c.R + (255 - c.R) * amount);
            byte g = (byte)Math.Min(255, c.G + (255 - c.G) * amount);
            byte b = (byte)Math.Min(255, c.B + (255 - c.B) * amount);
            return Color.FromRgb(r, g, b);
        }
    }

    // =========================================================================
    // Main Application Window
    // =========================================================================
    public partial class MainWindow : Window
    {
        // Vibrant Casino / Sports Palette
        internal static readonly Color[] SlicePalette = new Color[]
        {
            // v1.2: 채도를 맞춘 조화로운 톤 (흰 글자 대비 유지)
            Color.FromRgb(224, 92, 92),   // Red
            Color.FromRgb(91, 108, 240),  // Indigo
            Color.FromRgb(46, 160, 120),  // Green
            Color.FromRgb(214, 152, 56),  // Amber
            Color.FromRgb(146, 104, 224), // Violet
            Color.FromRgb(214, 92, 150),  // Pink
            Color.FromRgb(40, 156, 180),  // Teal
            Color.FromRgb(222, 120, 70),  // Orange
            Color.FromRgb(110, 160, 70),  // Olive
            Color.FromRgb(84, 132, 214)   // Blue
        };

        // Session state & RNG (Purely in-memory, zero external files)
        private AppSessionState _state;
        private readonly RNGCryptoServiceProvider _rng = new RNGCryptoServiceProvider();

        // Roulette mode: 0 = 연속 (기본값), 1 = 선승제, 2 = 단판
        private int _rouletteMode = 0;

        // 선승제 진행 상황 (항목별 승수, 현재 몇 판째인지)
        private readonly Dictionary<string, int> _matchWins = new Dictionary<string, int>();
        private int _matchGameNo = 0;

        // Spin animation state
        private bool _isSpinning = false;
        private double _spinStartAngle = 0.0;
        private double _spinTargetAngle = 0.0;
        private double _spinDurationSec = 0.75;
        private Stopwatch _spinStopwatch;
        private int _lastObservedSliceIdx = -1;
        private int _plannedWinningSliceIdx = -1;

        // Auto-spin until target streak
        private bool _isAutoSpinning = false;
        private DispatcherTimer _autoSpinNextTimer;

        // Streak & Session Tracking
        private readonly List<SpinRecord> _history = new List<SpinRecord>();
        private readonly Dictionary<string, int> _sessionCounts = new Dictionary<string, int>();
        private string _currentCandidate = null;
        private Color _currentCandidateColor = Colors.Gray;
        private int _currentStreak = 0;
        private int _currentRound = 1;
        private bool _roundWonPendingReset = false;
        private bool _lastWasCandidateSwitch = false;

        // UI Elements - Left Panel
        private ComboBox _presetCombo;
        private bool _suppressPresetComboEvent = false;
        private TextBox _newItemInput;
        private StackPanel _itemListPanel;
        private TextBlock _itemSummaryText;
        private CheckBox _splitSmallPoolCheck;

        // UI Elements - Center Scoreboard & Wheel
        private Border _scoreboardBorder;
        private TextBlock _scoreboardTitleText;
        private readonly List<Button> _modeButtons = new List<Button>();
        private Button _resetStreakBtn;
        private DockPanel _targetRowPanel;
        private TextBlock _roundBadgeText;
        private TextBlock _targetLabelText;
        private readonly List<Button> _targetButtons = new List<Button>();
        private readonly List<Button> _bestOfButtons = new List<Button>();
        private TextBlock _candidateCaptionText;
        private Ellipse _candidateDot;
        private TextBlock _candidateNameText;
        private Border _candidateTagBorder;
        private TextBlock _candidateTagText;
        private TextBlock _bsoSLetter;
        private StackPanel _strikeLampsPanel;
        private TextBlock _scoreboardStatusText;

        private RouletteWheelControl _wheelControl;
        private readonly List<Button> _speedButtons = new List<Button>();
        private CheckBox _soundCheck;
        private Button _spinButton;
        private Button _autoSpinButton;

        // UI Elements - Right History & Memo
        private TextBlock _historyTitleText;
        private TextBlock _historyCountBadgeText;
        private WrapPanel _freqWrapPanel;
        private ScrollViewer _historyScroll;
        private StackPanel _historyStack;
        private CheckBox _autoMemoCheck;
        private CheckBox _autoNewRoundCheck;
        private TextBox _memoTextBox;
        private bool _suppressMemoTextChanged = false;
        private TextBlock _copyFeedbackText;
        private Border _focusAnchor;

        // Top Multi-Game Navigation Tabs (0 = 연속 룰렛, 1 = 사다리 게임, 2 = 제비뽑기, 3 = 팀 나누기)
        private int _activeTabIndex = 0;
        private readonly List<Button> _tabButtons = new List<Button>();
        private TextBlock _tabHintText;
        private Grid _rouletteRootGrid;
        private LadderTabView _ladderTab;
        private LotsTabView _lotsTab;
        private TeamSplitTabView _teamSplitTab;

        internal bool IsSoundEnabled
        {
            get { return _state != null && _state.SoundEnabled; }
        }

        public MainWindow()
        {
            InitDefaultState();
            _rouletteMode = 0; // 항상 기본값 연속 모드!
            SoundEffects.Initialize();

            Title = "랜덤 게임 (연속 룰렛 · 사다리 · 제비뽑기 · 팀 나누기)";

            // Fit comfortably on any DPI / screen resolution without overflowing
            double workW = SystemParameters.WorkArea.Width;
            double workH = SystemParameters.WorkArea.Height;
            Width = Math.Min(1440, Math.Max(1100, workW * 0.94)); // 왼쪽 사이드바만큼 넓힘
            Height = Math.Min(885, Math.Max(690, workH * 0.91));
            MinWidth = 980;
            MinHeight = 650;

            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = BrushFromHex("#0F172A");
            FontFamily = new FontFamily("Malgun Gothic");
            UseLayoutRounding = true;
            Focusable = true;

            // Disable IME on the Window itself so Space / A / R work even in Korean Hangul mode!
            InputMethod.SetIsInputMethodEnabled(this, false);

            TryLoadWindowIcon();
            BuildUI();
            SetupTimersAndEvents();

            RefreshPresetCombo();
            RebuildItemListUI();
            RebuildWheelSlices();
            UpdateStreakModeUI();
            UpdateTargetButtonsUI();
            UpdateSpeedButtonsUI();
            UpdateScoreboardUI();
            UpdateFreqStripUI();
            UpdateTopTabBarUI();
        }

        internal void FocusMainWindowForShortcuts()
        {
            if (_focusAnchor != null)
            {
                FocusManager.SetFocusedElement(this, _focusAnchor);
                _focusAnchor.Focus();
                Keyboard.Focus(_focusAnchor);
            }
            else
            {
                Keyboard.ClearFocus();
            }
        }

        private void TryLoadWindowIcon()
        {
            try
            {
                using (Stream icoStream = typeof(MainWindow).Assembly.GetManifestResourceStream("StrikeRoulette.app.ico"))
                {
                    if (icoStream != null)
                    {
                        System.Windows.Media.Imaging.IconBitmapDecoder dec = new System.Windows.Media.Imaging.IconBitmapDecoder(
                            icoStream,
                            System.Windows.Media.Imaging.BitmapCreateOptions.None,
                            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                        System.Windows.Media.Imaging.BitmapFrame best = null;
                        for (int i = 0; i < dec.Frames.Count; i++)
                        {
                            if (best == null || dec.Frames[i].PixelWidth > best.PixelWidth)
                            {
                                best = dec.Frames[i];
                            }
                        }
                        if (best != null)
                        {
                            Icon = best;
                            return;
                        }
                    }
                }

                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                System.Drawing.Icon sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (sysIcon != null)
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        sysIcon.ToBitmap().Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        ms.Position = 0;
                        System.Windows.Media.Imaging.BitmapImage bi = new System.Windows.Media.Imaging.BitmapImage();
                        bi.BeginInit();
                        bi.StreamSource = ms;
                        bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bi.EndInit();
                        bi.Freeze();
                        Icon = bi;
                    }
                }
            }
            catch { }
        }

        // =====================================================================
        // In-Memory Session State (No JSON or external files saved)
        // =====================================================================
        private void InitDefaultState()
        {
            _state = new AppSessionState();

            // Index 0: 기본값 '없음' (빈 항목 상태로 시작)
            RoulettePreset pNone = new RoulettePreset { PresetName = "없음" };

            // 프로그램 내장 프리셋들
            RoulettePreset p1 = new RoulettePreset { PresetName = "오늘 메뉴 고르기" };
            p1.Items.Add(new RouletteItemData("치킨", true));
            p1.Items.Add(new RouletteItemData("피자", true));
            p1.Items.Add(new RouletteItemData("삼겹살", true));
            p1.Items.Add(new RouletteItemData("마라탕", true));
            p1.Items.Add(new RouletteItemData("초밥", true));
            p1.Items.Add(new RouletteItemData("국밥", true));

            RoulettePreset p2 = new RoulettePreset { PresetName = "할까 말까 (2지선다)" };
            p2.Items.Add(new RouletteItemData("한다!", true));
            p2.Items.Add(new RouletteItemData("안 한다!", true));

            RoulettePreset p3 = new RoulettePreset { PresetName = "숫자 뽑기 (1~5)" };
            p3.Items.Add(new RouletteItemData("1번", true));
            p3.Items.Add(new RouletteItemData("2번", true));
            p3.Items.Add(new RouletteItemData("3번", true));
            p3.Items.Add(new RouletteItemData("4번", true));
            p3.Items.Add(new RouletteItemData("5번", true));

            _state.Presets.Add(pNone);
            _state.Presets.Add(p1);
            _state.Presets.Add(p2);
            _state.Presets.Add(p3);
            _state.SelectedPresetIndex = 0; // 항상 '없음'으로 시작!
            _state.TargetStreak = 3;
            _state.SpinSpeedMode = 1;
            _state.MemoText = "=== 당첨 기록 & 메모 ===\r\n";
        }

        private void ScheduleSave()
        {
            // Intentionally empty: user requested pure in-memory operation without creating any .json file
        }

        private RoulettePreset CurrentPreset
        {
            get
            {
                if (_state.Presets.Count == 0)
                {
                    _state.Presets.Add(new RoulettePreset { PresetName = "기본 프리셋" });
                }
                if (_state.SelectedPresetIndex < 0 || _state.SelectedPresetIndex >= _state.Presets.Count)
                {
                    _state.SelectedPresetIndex = 0;
                }
                return _state.Presets[_state.SelectedPresetIndex];
            }
        }

        // =====================================================================
        // UI Construction (Proportional Responsive Columns — Zero Clipping)
        // =====================================================================
        private void BuildUI()
        {
            // v1.2 레이아웃: [왼쪽 사이드바 | (페이지 제목 + 탭 내용)]
            Grid shellGrid = new Grid { Background = Brushes.Transparent };
            shellGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            shellGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            shellGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shellGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Dedicated non-FocusScope focus target so clicking outside Memo/Input TextBox
            // reliably strips keyboard focus away from TextBoxes across ALL tabs!
            _focusAnchor = new Border
            {
                Width = 0,
                Height = 0,
                Focusable = true,
                FocusVisualStyle = null,
                IsHitTestVisible = false
            };
            InputMethod.SetIsInputMethodEnabled(_focusAnchor, false);
            shellGrid.Children.Add(_focusAnchor);

            UIElement sidebar = BuildSidebar();
            Grid.SetRow(sidebar, 0);
            Grid.SetRowSpan(sidebar, 2);
            shellGrid.Children.Add(sidebar);

            UIElement pageHeader = BuildPageHeader();
            Grid.SetRow(pageHeader, 0);
            Grid.SetColumn(pageHeader, 1);
            shellGrid.Children.Add(pageHeader);

            // Tab 0: 연속 룰렛 (기존 UI 그대로 유지)
            Grid rootGrid = new Grid();
            rootGrid.Background = Brushes.Transparent;
            rootGrid.Margin = new Thickness(18, 6, 22, 22);
            rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.72, GridUnitType.Star), MinWidth = 250 });
            rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.24, GridUnitType.Star), MinWidth = 385 });
            rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.04, GridUnitType.Star), MinWidth = 360 });

            UIElement leftCol = BuildLeftPanel();
            Grid.SetColumn(leftCol, 0);
            rootGrid.Children.Add(leftCol);

            UIElement centerCol = BuildCenterPanel();
            Grid.SetColumn(centerCol, 2);
            rootGrid.Children.Add(centerCol);

            UIElement rightCol = BuildRightPanel();
            Grid.SetColumn(rightCol, 4);
            rootGrid.Children.Add(rightCol);

            _rouletteRootGrid = rootGrid;
            Grid.SetRow(_rouletteRootGrid, 1);
            Grid.SetColumn(_rouletteRootGrid, 1);
            shellGrid.Children.Add(_rouletteRootGrid);

            // Tab 1: 사다리 게임
            _ladderTab = new LadderTabView(this);
            _ladderTab.Margin = new Thickness(18, 6, 22, 22);
            _ladderTab.Visibility = Visibility.Collapsed;
            Grid.SetRow(_ladderTab, 1);
            Grid.SetColumn(_ladderTab, 1);
            shellGrid.Children.Add(_ladderTab);

            // Tab 2: 제비뽑기
            _lotsTab = new LotsTabView(this);
            _lotsTab.Margin = new Thickness(18, 6, 22, 22);
            _lotsTab.Visibility = Visibility.Collapsed;
            Grid.SetRow(_lotsTab, 1);
            Grid.SetColumn(_lotsTab, 1);
            shellGrid.Children.Add(_lotsTab);

            // Tab 3: 팀 나누기
            _teamSplitTab = new TeamSplitTabView(this);
            _teamSplitTab.Margin = new Thickness(18, 6, 22, 22);
            _teamSplitTab.Visibility = Visibility.Collapsed;
            Grid.SetRow(_teamSplitTab, 1);
            Grid.SetColumn(_teamSplitTab, 1);
            shellGrid.Children.Add(_teamSplitTab);

            Content = shellGrid;
        }

        private static readonly string[] TabIcons = new string[] { "🎡", "┣┫", "🎫", "👥" };
        private static readonly string[] TabNames = new string[] { "연속 룰렛", "사다리 게임", "제비뽑기", "팀 나누기" };
        private TextBlock _pageTitleText;
        private TextBlock _pageIconText;

        private UIElement BuildSidebar()
        {
            Border side = new Border
            {
                Background = BrushFromHex("#0B1120"),
                Padding = new Thickness(14, 22, 14, 18)
            };
            DockPanel dock = new DockPanel { LastChildFill = true };

            // Brand
            StackPanel brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 28) };
            if (Icon != null)
            {
                brand.Children.Add(new Image { Source = Icon, Width = 30, Height = 30, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            }
            StackPanel brandText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            brandText.Children.Add(new TextBlock { Text = "랜덤 게임", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
            brandText.Children.Add(new TextBlock { Text = "Random Games", FontSize = 10.5, Foreground = BrushFromHex("#64748B") });
            brand.Children.Add(brandText);
            DockPanel.SetDock(brand, Dock.Top);
            dock.Children.Add(brand);

            TextBlock sectionLbl = new TextBlock
            {
                Text = "게임",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFromHex("#64748B"),
                Margin = new Thickness(10, 0, 0, 8)
            };
            DockPanel.SetDock(sectionLbl, Dock.Top);
            dock.Children.Add(sectionLbl);

            TextBlock version = new TextBlock
            {
                Text = "v1.2 · 파일 저장 없음",
                FontSize = 10.5,
                Foreground = BrushFromHex("#475569"),
                Margin = new Thickness(10, 0, 0, 0)
            };
            DockPanel.SetDock(version, Dock.Bottom);
            dock.Children.Add(version);

            StackPanel nav = new StackPanel();
            _tabButtons.Clear();
            for (int i = 0; i < TabNames.Length; i++)
            {
                int capturedIdx = i;
                Grid content = new Grid();
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                TextBlock ic = new TextBlock { Text = TabIcons[i], FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
                TextBlock lb = new TextBlock { Text = TabNames[i], FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(lb, 1);
                content.Children.Add(ic);
                content.Children.Add(lb);

                Button tb = MakeStyledButton("", "#0B1120", "#94A3B8", 13.5, 42);
                tb.Content = content;
                tb.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                tb.Padding = new Thickness(12, 0, 10, 0);
                tb.Margin = new Thickness(0, 0, 0, 4);
                tb.Click += (s, e) => SelectTab(capturedIdx);
                _tabButtons.Add(tb);
                nav.Children.Add(tb);
            }
            dock.Children.Add(nav);

            side.Child = dock;
            return side;
        }

        private UIElement BuildPageHeader()
        {
            DockPanel header = new DockPanel { Margin = new Thickness(20, 20, 22, 10) };

            _pageIconText = new TextBlock
            {
                FontSize = 22,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            DockPanel.SetDock(_pageIconText, Dock.Left);
            header.Children.Add(_pageIconText);

            StackPanel titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _pageTitleText = new TextBlock
            {
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            titles.Children.Add(_pageTitleText);

            _tabHintText = new TextBlock
            {
                FontSize = 12,
                Foreground = BrushFromHex("#94A3B8"),
                Margin = new Thickness(0, 3, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            titles.Children.Add(_tabHintText);
            header.Children.Add(titles);
            return header;
        }

        private void SelectTab(int tabIdx)
        {
            if (tabIdx < 0 || tabIdx > 3) return;
            if (_activeTabIndex == 0 && tabIdx != 0)
            {
                StopAutoSpin();
            }
            if (_activeTabIndex == 1 && tabIdx != 1 && _ladderTab != null)
            {
                _ladderTab.StopAutoSequence();
            }

            _activeTabIndex = tabIdx;

            if (_rouletteRootGrid != null)
                _rouletteRootGrid.Visibility = (tabIdx == 0) ? Visibility.Visible : Visibility.Collapsed;
            if (_ladderTab != null)
                _ladderTab.Visibility = (tabIdx == 1) ? Visibility.Visible : Visibility.Collapsed;
            if (_lotsTab != null)
                _lotsTab.Visibility = (tabIdx == 2) ? Visibility.Visible : Visibility.Collapsed;
            if (_teamSplitTab != null)
                _teamSplitTab.Visibility = (tabIdx == 3) ? Visibility.Visible : Visibility.Collapsed;

            UpdateTopTabBarUI();
            FocusMainWindowForShortcuts();
        }

        private void UpdateTopTabBarUI()
        {
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                bool active = (i == _activeTabIndex);
                _tabButtons[i].Background = BrushFromHex(active ? "#1E293B" : "#0B1120");
                _tabButtons[i].Foreground = BrushFromHex(active ? "#FFFFFF" : "#94A3B8");
                _tabButtons[i].FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
            }
            if (_pageTitleText != null)
            {
                _pageTitleText.Text = TabNames[_activeTabIndex];
                _pageIconText.Text = TabIcons[_activeTabIndex];
            }

            if (_tabHintText != null)
            {
                if (_activeTabIndex == 0)
                    _tabHintText.Text = "단축키 — Space: 돌리기 · A: 자동 · R: 카운트 리셋 · Esc: 입력창 나가기";
                else if (_activeTabIndex == 1)
                    _tabHintText.Text = "참가자 이름 클릭: 개별 사다리 출발 · Space: 다음 출발 · Esc: 입력창 나가기";
                else if (_activeTabIndex == 2)
                    _tabHintText.Text = "제비 카드 클릭: 직접 뽑기 · Space: 랜덤 1개 뽑기 · Esc: 입력창 나가기";
                else
                    _tabHintText.Text = "팀 개수를 고르고 명단을 넣은 뒤 Space 또는 [팀 나누기] 버튼을 누르세요";
            }
        }

        private UIElement BuildLeftPanel()
        {
            Border card = MakeCardBorder();
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Preset bar
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Add item bar
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Item list
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer options

            // 1. Header
            DockPanel header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            TextBlock title = new TextBlock
            {
                Text = "룰렛 항목 설정",
                FontSize = 15.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            StackPanel headerBtns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            Button bulkBtn = MakeSmallButton("일괄 편집", "#334155", "#E2E8F0");
            bulkBtn.Margin = new Thickness(0, 0, 4, 0);
            bulkBtn.Click += (s, e) => OpenBulkEditDialog();
            headerBtns.Children.Add(bulkBtn);

            Button clearItemsBtn = MakeSmallButton("비우기", "#334155", "#FCA5A5");
            clearItemsBtn.ToolTip = "현재 목록의 항목을 모두 비웁니다";
            clearItemsBtn.Click += (s, e) => ClearCurrentItems();
            headerBtns.Children.Add(clearItemsBtn);

            DockPanel.SetDock(headerBtns, Dock.Right);
            header.Children.Add(headerBtns);
            header.Children.Add(title);
            Grid.SetRow(header, 0);
            g.Children.Add(header);

            // 2. Built-in Preset selector row (Clean full-width dropdown without add/rename/delete clutter)
            StackPanel presetBox = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            TextBlock presetLbl = new TextBlock
            {
                Text = "기본 프리셋 불러오기",
                FontSize = 11.5,
                Foreground = BrushFromHex("#94A3B8"),
                Margin = new Thickness(0, 0, 0, 4)
            };
            presetBox.Children.Add(presetLbl);

            _presetCombo = new ComboBox
            {
                Height = 31,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 12.5,
                Focusable = false
            };
            _presetCombo.SelectionChanged += OnPresetSelectionChanged;
            presetBox.Children.Add(_presetCombo);

            Grid.SetRow(presetBox, 1);
            g.Children.Add(presetBox);

            // 3. Quick Add Item Row (with placeholder watermark)
            Grid addRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid inputContainer = new Grid { Margin = new Thickness(0, 0, 6, 0) };
            _newItemInput = new TextBox
            {
                Height = 34,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = BrushFromHex("#0F172A"),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                BorderBrush = BrushFromHex("#475569"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 0, 8, 0),
                FontSize = 13,
                ToolTip = "항목 이름을 입력하고 Enter 키를 누르세요 (비어 있을 때 Space를 누르면 바로 룰렛이 돌아갑니다)"
            };
            InputMethod.SetIsInputMethodEnabled(_newItemInput, true);

            TextBlock inputHint = new TextBlock
            {
                Text = "새 항목 입력 후 Enter...",
                Foreground = BrushFromHex("#64748B"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                IsHitTestVisible = false
            };
            _newItemInput.TextChanged += (s, e) =>
            {
                inputHint.Visibility = string.IsNullOrEmpty(_newItemInput.Text) ? Visibility.Visible : Visibility.Collapsed;
            };
            _newItemInput.GotKeyboardFocus += (s, e) => _newItemInput.BorderBrush = BrushFromHex("#38BDF8");
            _newItemInput.LostKeyboardFocus += (s, e) => _newItemInput.BorderBrush = BrushFromHex("#475569");
            _newItemInput.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    AddNewItemFromInput(false);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    FocusMainWindowForShortcuts();
                    e.Handled = true;
                }
            };
            inputContainer.Children.Add(_newItemInput);
            inputContainer.Children.Add(inputHint);
            Grid.SetColumn(inputContainer, 0);
            addRow.Children.Add(inputContainer);

            Button addBtn = MakeStyledButton("+ 추가", "#3B82F6", "#FFFFFF", 13, 34);
            addBtn.Padding = new Thickness(12, 0, 12, 0);
            addBtn.Click += (s, e) => AddNewItemFromInput(true);
            Grid.SetColumn(addBtn, 1);
            addRow.Children.Add(addBtn);

            Grid.SetRow(addRow, 2);
            g.Children.Add(addRow);

            // 4. Item List ScrollViewer
            Border listBorder = new Border
            {
                Background = BrushFromHex("#0F172A"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6)
            };
            ScrollViewer itemScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Focusable = false
            };
            _itemListPanel = new StackPanel();
            itemScroll.Content = _itemListPanel;
            listBorder.Child = itemScroll;
            Grid.SetRow(listBorder, 3);
            g.Children.Add(listBorder);

            // 5. Footer Info & Small Pool Split Option
            StackPanel footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            _itemSummaryText = new TextBlock
            {
                Text = "활성 항목: 0개",
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Foreground = BrushFromHex("#94A3B8"),
                Margin = new Thickness(0, 0, 0, 6)
            };
            footer.Children.Add(_itemSummaryText);

            _splitSmallPoolCheck = new CheckBox
            {
                IsChecked = _state.SplitSmallPool,
                Foreground = BrushFromHex("#CBD5E1"),
                FontSize = 11.5,
                Focusable = false,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "항목이 2개나 3개일 때 휠을 6칸으로 번갈아 나누어 회전감을 살립니다 (당첨 확률은 완전히 동일)",
                Content = new TextBlock
                {
                    Text = "항목 2~3개일 때 6칸 교차 분할 (확률 동일)",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = BrushFromHex("#CBD5E1"),
                    FontSize = 11.5
                }
            };
            _splitSmallPoolCheck.Checked += (s, e) => { _state.SplitSmallPool = true; RebuildWheelSlices(); ScheduleSave(); };
            _splitSmallPoolCheck.Unchecked += (s, e) => { _state.SplitSmallPool = false; RebuildWheelSlices(); ScheduleSave(); };
            footer.Children.Add(_splitSmallPoolCheck);

            Grid.SetRow(footer, 4);
            g.Children.Add(footer);

            card.Child = g;
            return card;
        }

        private UIElement BuildCenterPanel()
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Baseball Strike Scoreboard
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Wheel
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Controls

            // =================================================================
            // 1. Baseball Strike Count Scoreboard (전광판 — 2단 헤더로 잘림 완벽 방지)
            // =================================================================
            _scoreboardBorder = new Border
            {
                Background = BrushFromHex("#0B1120"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 11, 14, 11),
                Margin = new Thickness(0, 0, 0, 10)
            };

            StackPanel sbStack = new StackPanel();

            // Header Row 1: Title (Left)  <--->  [연속 모드: ON/OFF] + [카운트 리셋] (Right)
            DockPanel sbTopRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };

            StackPanel rightCtrlGroup = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 모드 선택 (연속 / 선승제 / 단판) — 세그먼트 버튼
            Border modeSeg = new Border
            {
                Background = BrushFromHex("#0F172A"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(3),
                Margin = new Thickness(0, 0, 6, 0)
            };
            StackPanel modeRow = new StackPanel { Orientation = Orientation.Horizontal };
            string[] modeNames = new string[] { "연속", "선승제", "단판" };
            string[] modeTips = new string[]
            {
                "같은 항목이 목표 횟수만큼 연속으로 나오면 당첨",
                "먼저 정해진 승수를 채운 항목이 당첨 (2선승 ~ 5선승)",
                "1번 돌린 결과가 바로 당첨"
            };
            _modeButtons.Clear();
            for (int i = 0; i < modeNames.Length; i++)
            {
                int capturedMode = i;
                Button mb = MakeSmallButton(modeNames[i], "#0F172A", "#94A3B8");
                mb.Height = 24;
                mb.Padding = new Thickness(11, 2, 11, 2);
                mb.ToolTip = modeTips[i];
                mb.Click += (s, e) => SetRouletteMode(capturedMode);
                _modeButtons.Add(mb);
                modeRow.Children.Add(mb);
            }
            modeSeg.Child = modeRow;
            rightCtrlGroup.Children.Add(modeSeg);

            _resetStreakBtn = MakeSmallButton("카운트 리셋 (R)", "#1E293B", "#CBD5E1");
            _resetStreakBtn.Padding = new Thickness(9, 4, 9, 4);
            _resetStreakBtn.ToolTip = "현재 연속 카운트만 0으로 초기화합니다 (단축키: R)";
            _resetStreakBtn.Click += (s, e) => ResetCurrentStreakOnly();
            rightCtrlGroup.Children.Add(_resetStreakBtn);

            DockPanel.SetDock(rightCtrlGroup, Dock.Right);
            sbTopRow.Children.Add(rightCtrlGroup);

            _scoreboardTitleText = new TextBlock
            {
                Text = "연속 스트라이크 전광판",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = BrushFromHex("#F8FAFC"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            sbTopRow.Children.Add(_scoreboardTitleText);
            sbStack.Children.Add(sbTopRow);

            // Header Row 2: Target Streak Pills (Left) <---> Round Badge (Right)
            _targetRowPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };

            Border roundBadge = new Border
            {
                Background = BrushFromHex("#1E293B"),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
            _roundBadgeText = new TextBlock
            {
                Text = "라운드 1",
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFromHex("#93C5FD")
            };
            roundBadge.Child = _roundBadgeText;
            DockPanel.SetDock(roundBadge, Dock.Right);
            _targetRowPanel.Children.Add(roundBadge);

            StackPanel targetPills = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            _targetLabelText = new TextBlock
            {
                Text = "목표 연속:",
                FontSize = 12,
                Foreground = BrushFromHex("#94A3B8"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            targetPills.Children.Add(_targetLabelText);

            int[] bestOfWins = new int[] { 2, 3, 4, 5 };
            for (int i = 0; i < bestOfWins.Length; i++)
            {
                int wVal = bestOfWins[i];
                Button bb = MakeSmallButton(string.Format("{0}선승", wVal), "#1E293B", "#CBD5E1");
                bb.Tag = wVal;
                bb.Margin = new Thickness(0, 0, 4, 0);
                bb.Padding = new Thickness(10, 3, 10, 3);
                bb.Click += (s, e) => SetBestOfWins(wVal);
                _bestOfButtons.Add(bb);
                targetPills.Children.Add(bb);
            }

            int[] targets = new int[] { 2, 3, 4, 5 };
            for (int i = 0; i < targets.Length; i++)
            {
                int tVal = targets[i];
                Button tb = MakeSmallButton(tVal + "연속", "#1E293B", "#CBD5E1");
                tb.Tag = tVal;
                tb.Margin = new Thickness(0, 0, 4, 0);
                tb.Padding = new Thickness(10, 3, 10, 3);
                tb.Click += (s, e) => SetTargetStreak(tVal);
                _targetButtons.Add(tb);
                targetPills.Children.Add(tb);
            }
            _targetRowPanel.Children.Add(targetPills);
            sbStack.Children.Add(_targetRowPanel);

            // Row 3: Main Stadium Display (Candidate Left | S ● ● ○ Right)
            Border innerBoard = new Border
            {
                Background = BrushFromHex("#131C31"),
                BorderBrush = BrushFromHex("#1E293B"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 8)
            };

            Grid boardGrid = new Grid();
            boardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            boardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left: Candidate Info
            StackPanel candPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            StackPanel candTopRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
            _candidateCaptionText = new TextBlock
            {
                Text = "현재 연속 후보",
                FontSize = 11.5,
                Foreground = BrushFromHex("#94A3B8"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            candTopRow.Children.Add(_candidateCaptionText);

            _candidateTagBorder = new Border
            {
                Background = BrushFromHex("#334155"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Visibility = Visibility.Collapsed
            };
            _candidateTagText = new TextBlock
            {
                Text = "대기",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            _candidateTagBorder.Child = _candidateTagText;
            candTopRow.Children.Add(_candidateTagBorder);
            candPanel.Children.Add(candTopRow);

            Grid candValueRow = new Grid();
            candValueRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            candValueRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _candidateDot = new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = BrushFromHex("#475569"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(_candidateDot, 0);
            candValueRow.Children.Add(_candidateDot);

            _candidateNameText = new TextBlock
            {
                Text = "대기 중",
                FontSize = 21,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(_candidateNameText, 1);
            candValueRow.Children.Add(_candidateNameText);
            candPanel.Children.Add(candValueRow);

            Grid.SetColumn(candPanel, 0);
            boardGrid.Children.Add(candPanel);

            // Right: Baseball "S" + Glowing Lamps
            StackPanel bsoRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            _bsoSLetter = new TextBlock
            {
                Text = "S",
                FontFamily = new FontFamily("Consolas, Malgun Gothic"),
                FontSize = 26,
                FontWeight = FontWeights.ExtraBold,
                Foreground = BrushFromHex("#FBBF24"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            bsoRow.Children.Add(_bsoSLetter);

            _strikeLampsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            bsoRow.Children.Add(_strikeLampsPanel);

            Grid.SetColumn(bsoRow, 1);
            boardGrid.Children.Add(bsoRow);

            innerBoard.Child = boardGrid;
            sbStack.Children.Add(innerBoard);

            // Row 4: Live Commentary Status
            _scoreboardStatusText = new TextBlock
            {
                Text = "스페이스바(Space) 또는 [돌리기] 버튼을 눌러 시작하세요!",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFromHex("#CBD5E1"),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            sbStack.Children.Add(_scoreboardStatusText);

            _scoreboardBorder.Child = sbStack;
            Grid.SetRow(_scoreboardBorder, 0);
            g.Children.Add(_scoreboardBorder);

            // =================================================================
            // 2. Center Roulette Wheel Area
            // =================================================================
            Border wheelCard = MakeCardBorder();
            wheelCard.Margin = new Thickness(0, 0, 0, 10);
            wheelCard.Padding = new Thickness(8);
            wheelCard.Background = Brushes.Transparent; // 휠은 카드 없이 페이지 위에 떠 있게

            _wheelControl = new RouletteWheelControl();
            wheelCard.Child = _wheelControl;
            Grid.SetRow(wheelCard, 1);
            g.Children.Add(wheelCard);

            // =================================================================
            // 3. Spin Speed & Action Controls
            // =================================================================
            Border ctrlCard = MakeCardBorder();
            ctrlCard.Padding = new Thickness(14, 11, 14, 11);
            StackPanel ctrlStack = new StackPanel();

            // Speed Bar + Sound Check
            DockPanel speedBar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };

            _soundCheck = new CheckBox
            {
                Content = "효과음",
                IsChecked = _state.SoundEnabled,
                Foreground = BrushFromHex("#CBD5E1"),
                FontSize = 12,
                Focusable = false,
                VerticalAlignment = VerticalAlignment.Center
            };
            _soundCheck.Checked += (s, e) => { _state.SoundEnabled = true; ScheduleSave(); };
            _soundCheck.Unchecked += (s, e) => { _state.SoundEnabled = false; ScheduleSave(); };
            DockPanel.SetDock(_soundCheck, Dock.Right);
            speedBar.Children.Add(_soundCheck);

            StackPanel speedPills = new StackPanel { Orientation = Orientation.Horizontal };
            TextBlock speedLbl = new TextBlock
            {
                Text = "속도:",
                FontSize = 12,
                Foreground = BrushFromHex("#94A3B8"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            speedPills.Children.Add(speedLbl);

            string[] speedNames = new string[] { "일반", "빠름", "초고속", "즉시" };
            string[] speedTips = new string[] { "일반 회전 속도 (약 2.0초)", "빠른 회전 속도 (약 0.7초, 다회차 추천)", "초고속 회전 (약 0.25초)", "회전 애니메이션 없이 즉시 결과" };
            for (int i = 0; i < speedNames.Length; i++)
            {
                int modeIdx = i;
                Button sb = MakeSmallButton(speedNames[i], "#0F172A", "#CBD5E1");
                sb.Tag = modeIdx;
                sb.ToolTip = speedTips[i];
                sb.Margin = new Thickness(0, 0, 4, 0);
                sb.Padding = new Thickness(9, 4, 9, 4);
                sb.Click += (s, e) => SetSpinSpeedMode(modeIdx);
                _speedButtons.Add(sb);
                speedPills.Children.Add(sb);
            }
            speedBar.Children.Add(speedPills);
            ctrlStack.Children.Add(speedBar);

            // Primary Spin & Auto-Spin Buttons
            Grid actionGrid = new Grid();
            actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _spinButton = MakeStyledButton("▶ 돌리기 (Space)", "#2563EB", "#FFFFFF", 15.5, 46);
            _spinButton.Click += (s, e) => OnSpinButtonClick();
            Grid.SetColumn(_spinButton, 0);
            actionGrid.Children.Add(_spinButton);

            _autoSpinButton = MakeStyledButton("⚡ 3연속까지 자동", "#059669", "#FFFFFF", 13.5, 46);
            _autoSpinButton.Click += (s, e) => ToggleAutoSpin();
            Grid.SetColumn(_autoSpinButton, 2);
            actionGrid.Children.Add(_autoSpinButton);

            ctrlStack.Children.Add(actionGrid);

            TextBlock shortcutHint = new TextBlock
            {
                Text = "단축키: [Space] 돌리기/즉시스킵 · [A] 자동연속 · [R] 카운트리셋 · [Esc] 중지/입력창나오기",
                FontSize = 11,
                Foreground = BrushFromHex("#64748B"),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 7, 0, 0)
            };
            ctrlStack.Children.Add(shortcutHint);

            ctrlCard.Child = ctrlStack;
            Grid.SetRow(ctrlCard, 2);
            g.Children.Add(ctrlCard);

            return g;
        }

        private UIElement BuildRightPanel()
        {
            Grid rightGrid = new Grid();
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.15, GridUnitType.Star), MinHeight = 210 });
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.95, GridUnitType.Star), MinHeight = 195 });

            // =================================================================
            // Top Half: Auto-Stacking Spin & Strike History Board
            // =================================================================
            Border histCard = MakeCardBorder();
            Grid hg = new Grid();
            hg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            hg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Frequency summary
            hg.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Feed list

            // Clip-proof Grid header: Title + [총 n회] Pill Badge (Left) | [구분선] [비우기] (Right)
            Grid hHeader = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            hHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel hLeft = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            _historyTitleText = new TextBlock
            {
                Text = "스트라이크 기록",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0)
            };
            hLeft.Children.Add(_historyTitleText);

            Border countPill = new Border
            {
                Background = BrushFromHex("#1D4ED8"),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            _historyCountBadgeText = new TextBlock
            {
                Text = "0회",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            countPill.Child = _historyCountBadgeText;
            hLeft.Children.Add(countPill);

            Grid.SetColumn(hLeft, 0);
            hHeader.Children.Add(hLeft);

            StackPanel hBtns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            Button divBtn = MakeSmallButton("구분선", "#334155", "#CBD5E1");
            divBtn.Margin = new Thickness(0, 0, 5, 0);
            divBtn.ToolTip = "새로운 주제를 돌릴 때 기록에 라운드 구분선을 넣습니다";
            divBtn.Click += (s, e) => StartNewRoundManual();
            hBtns.Children.Add(divBtn);

            Button clearHistBtn = MakeSmallButton("비우기", "#334155", "#FCA5A5");
            clearHistBtn.ToolTip = "스핀 기록과 누적 횟수를 비웁니다";
            clearHistBtn.Click += (s, e) => ClearHistory();
            hBtns.Children.Add(clearHistBtn);

            Grid.SetColumn(hBtns, 1);
            hHeader.Children.Add(hBtns);

            Grid.SetRow(hHeader, 0);
            hg.Children.Add(hHeader);

            // Frequency Chip Strip
            Border freqBorder = new Border
            {
                Background = BrushFromHex("#0F172A"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 0, 8)
            };
            _freqWrapPanel = new WrapPanel();
            freqBorder.Child = _freqWrapPanel;
            Grid.SetRow(freqBorder, 1);
            hg.Children.Add(freqBorder);

            // Auto-stacking list
            Border feedBorder = new Border
            {
                Background = BrushFromHex("#0B1120"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6)
            };
            _historyScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Focusable = false
            };
            _historyStack = new StackPanel();
            _historyScroll.Content = _historyStack;
            feedBorder.Child = _historyScroll;
            Grid.SetRow(feedBorder, 2);
            hg.Children.Add(feedBorder);

            histCard.Child = hg;
            Grid.SetRow(histCard, 0);
            rightGrid.Children.Add(histCard);

            // Draggable Splitter
            GridSplitter splitter = new GridSplitter
            {
                Height = 10,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                ShowsPreview = true,
                Cursor = Cursors.SizeNS,
                Focusable = false
            };
            Grid.SetRow(splitter, 1);
            rightGrid.Children.Add(splitter);

            // =================================================================
            // Bottom Half: Editable Result Memo Pad
            // =================================================================
            Border memoCard = MakeCardBorder();
            Grid mg = new Grid();
            mg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            mg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Checkboxes
            mg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Action buttons
            mg.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // TextBox

            DockPanel mHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            TextBlock mTitle = new TextBlock
            {
                Text = "결과 & 자유 메모장",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            _copyFeedbackText = new TextBlock
            {
                Text = "",
                FontSize = 11.5,
                Foreground = BrushFromHex("#34D399"),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_copyFeedbackText, Dock.Right);
            mHeader.Children.Add(_copyFeedbackText);
            mHeader.Children.Add(mTitle);
            Grid.SetRow(mHeader, 0);
            mg.Children.Add(mHeader);

            // Auto behaviors checkboxes
            WrapPanel checkRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            _autoMemoCheck = new CheckBox
            {
                Content = "당첨 확정 시 자동 기록",
                IsChecked = _state.AutoMemoOnWin,
                Foreground = BrushFromHex("#CBD5E1"),
                FontSize = 12,
                Focusable = false,
                Margin = new Thickness(0, 0, 12, 2)
            };
            _autoMemoCheck.Checked += (s, e) => { _state.AutoMemoOnWin = true; };
            _autoMemoCheck.Unchecked += (s, e) => { _state.AutoMemoOnWin = false; };
            checkRow.Children.Add(_autoMemoCheck);

            _autoNewRoundCheck = new CheckBox
            {
                Content = "달성 후 다음 스핀 시 새 라운드",
                IsChecked = _state.AutoNewRoundAfterWin,
                Foreground = BrushFromHex("#CBD5E1"),
                FontSize = 12,
                Focusable = false,
                Margin = new Thickness(0, 0, 0, 2),
                ToolTip = "목표 연속 당첨 이후 다시 돌리면 카운트를 1스트라이크부터 새로 시작합니다"
            };
            _autoNewRoundCheck.Checked += (s, e) => { _state.AutoNewRoundAfterWin = true; };
            _autoNewRoundCheck.Unchecked += (s, e) => { _state.AutoNewRoundAfterWin = false; };
            checkRow.Children.Add(_autoNewRoundCheck);

            Grid.SetRow(checkRow, 1);
            mg.Children.Add(checkRow);

            // Memo quick action toolbar
            WrapPanel memoTools = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            Button addCurBtn = MakeSmallButton("+ 현재 결과 넣기", "#2563EB", "#FFFFFF");
            addCurBtn.Margin = new Thickness(0, 0, 5, 0);
            addCurBtn.Click += (s, e) => AppendLatestResultToMemo();
            memoTools.Children.Add(addCurBtn);

            Button addSumBtn = MakeSmallButton("+ 누적 통계", "#334155", "#E2E8F0");
            addSumBtn.Margin = new Thickness(0, 0, 5, 0);
            addSumBtn.Click += (s, e) => AppendSummaryToMemo();
            memoTools.Children.Add(addSumBtn);

            Button copyMemoBtn = MakeSmallButton("전체 복사", "#334155", "#34D399");
            copyMemoBtn.Margin = new Thickness(0, 0, 5, 0);
            copyMemoBtn.Click += (s, e) => CopyMemoToClipboard();
            memoTools.Children.Add(copyMemoBtn);

            Button clearMemoBtn = MakeSmallButton("비우기", "#334155", "#FCA5A5");
            clearMemoBtn.Click += (s, e) => ClearMemoPad();
            memoTools.Children.Add(clearMemoBtn);

            Grid.SetRow(memoTools, 2);
            mg.Children.Add(memoTools);

            // Editable Multi-line Memo TextBox
            _memoTextBox = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = BrushFromHex("#0B1120"),
                Foreground = BrushFromHex("#F1F5F9"),
                CaretBrush = BrushFromHex("#38BDF8"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                FontSize = 13.5,
                Text = _state.MemoText ?? ""
            };
            InputMethod.SetIsInputMethodEnabled(_memoTextBox, true);
            _memoTextBox.CaretIndex = _memoTextBox.Text.Length;
            _memoTextBox.GotKeyboardFocus += (s, e) => _memoTextBox.BorderBrush = BrushFromHex("#38BDF8");
            _memoTextBox.LostKeyboardFocus += (s, e) => _memoTextBox.BorderBrush = BrushFromHex("#334155");
            _memoTextBox.TextChanged += (s, e) =>
            {
                if (_suppressMemoTextChanged) return;
                if (_copyFeedbackText != null && _copyFeedbackText.Text.Length > 0)
                {
                    _copyFeedbackText.Text = "";
                }
            };
            _memoTextBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    FocusMainWindowForShortcuts();
                    e.Handled = true;
                }
            };

            Grid.SetRow(_memoTextBox, 3);
            mg.Children.Add(_memoTextBox);

            memoCard.Child = mg;
            Grid.SetRow(memoCard, 2);
            rightGrid.Children.Add(memoCard);

            return rightGrid;
        }

        // =====================================================================
        // Timers, Render Loop, Focus & Win32/IME-Safe Keyboard Shortcuts
        // =====================================================================
        private void SetupTimersAndEvents()
        {
            _autoSpinNextTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(280)
            };
            _autoSpinNextTimer.Tick += (s, e) =>
            {
                _autoSpinNextTimer.Stop();
                if (_isAutoSpinning && !_isSpinning)
                {
                    StartSingleSpin();
                }
            };

            CompositionTarget.Rendering += OnAnimationFrame;

            Loaded += (s, e) => FocusMainWindowForShortcuts();
            ContentRendered += (s, e) => FocusMainWindowForShortcuts();

            // Win32 raw message-pump hook so Spacebar works 100% reliably regardless of Korean IME or WPF focus!
            ComponentDispatcher.ThreadPreprocessMessage += OnThreadPreprocessMessage;

            // Clicking anywhere outside a TextBox / ComboBox returns focus to MainWindow
            PreviewMouseDown += OnWindowPreviewMouseDown;
            PreviewKeyDown += OnWindowPreviewKeyDown;

            Closing += (s, e) =>
            {
                ComponentDispatcher.ThreadPreprocessMessage -= OnThreadPreprocessMessage;
                StopAutoSpin();
            };
        }

        private void OnThreadPreprocessMessage(ref MSG msg, ref bool handled)
        {
            if (handled) return;
            const int WM_KEYDOWN = 0x0100;
            if (msg.message != WM_KEYDOWN) return;
            if (!IsActive || OwnedWindows.Count > 0) return;

            int vk = (int)(msg.wParam.ToInt64() & 0xFFFF);
            int scanCode = (int)((msg.lParam.ToInt64() >> 16) & 0xFF);
            bool isRepeat = ((msg.lParam.ToInt64() & (1L << 30)) != 0);

            // VK_SPACE (0x20) or Korean IME VK_IME_PROCESSKEY (0xE5) with physical Spacebar scan code (0x39)
            bool isSpaceKey = (vk == 0x20) || (vk == 0xE5 && scanCode == 0x39);
            if (!isSpaceKey || isRepeat) return;

            TextBox focusedTb = Keyboard.FocusedElement as TextBox;
            if (focusedTb != null)
            {
                // Allow normal space typing in memo or non-empty item input
                if (_activeTabIndex != 0 || focusedTb != _newItemInput || !string.IsNullOrEmpty(_newItemInput.Text))
                {
                    return;
                }
                FocusMainWindowForShortcuts();
            }

            DispatchSpaceShortcutForActiveTab();
            handled = true;
        }

        private void DispatchSpaceShortcutForActiveTab()
        {
            if (_activeTabIndex == 0)
            {
                OnSpinButtonClick();
            }
            else if (_activeTabIndex == 1 && _ladderTab != null)
            {
                _ladderTab.HandleSpaceKey();
            }
            else if (_activeTabIndex == 2 && _lotsTab != null)
            {
                _lotsTab.HandleSpaceKey();
            }
            else if (_activeTabIndex == 3 && _teamSplitTab != null)
            {
                _teamSplitTab.HandleSpaceKey();
            }
        }

        private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject src = e.OriginalSource as DependencyObject;
            if (!IsInsideTextInputOrCombo(src))
            {
                FocusMainWindowForShortcuts();
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusMainWindowForShortcuts));
            }
        }

        private static bool IsInsideTextInputOrCombo(DependencyObject dep)
        {
            DependencyObject cur = dep;
            while (cur != null)
            {
                if (cur is TextBox || cur is ComboBox || cur is ComboBoxItem)
                {
                    return true;
                }
                if (cur is Visual || cur is System.Windows.Media.Media3D.Visual3D)
                {
                    cur = VisualTreeHelper.GetParent(cur);
                }
                else
                {
                    cur = LogicalTreeHelper.GetParent(cur);
                }
            }
            return false;
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Unwrap Korean IME processed key or System key!
            Key actualKey = e.Key;
            if (actualKey == Key.ImeProcessed)
            {
                actualKey = e.ImeProcessedKey;
            }
            else if (actualKey == Key.System)
            {
                actualKey = e.SystemKey;
            }

            TextBox focusedTb = Keyboard.FocusedElement as TextBox;
            if (focusedTb != null)
            {
                if (actualKey == Key.Escape)
                {
                    FocusMainWindowForShortcuts();
                    e.Handled = true;
                    return;
                }
                // Convenience: if cursor is inside the EMPTY new-item input box and user hits Space,
                // spin the roulette right away instead of typing a leading space!
                if (_activeTabIndex == 0 && focusedTb == _newItemInput && actualKey == Key.Space && string.IsNullOrEmpty(_newItemInput.Text))
                {
                    FocusMainWindowForShortcuts();
                    OnSpinButtonClick();
                    e.Handled = true;
                    return;
                }
                return;
            }

            if (actualKey == Key.Space)
            {
                DispatchSpaceShortcutForActiveTab();
                e.Handled = true;
                return;
            }

            if (_activeTabIndex != 0)
            {
                if (actualKey == Key.Escape && _activeTabIndex == 1 && _ladderTab != null)
                {
                    _ladderTab.StopAutoSequence();
                    e.Handled = true;
                }
                return;
            }

            if (actualKey == Key.A)
            {
                if (_rouletteMode != 2)
                {
                    ToggleAutoSpin();
                    e.Handled = true;
                }
            }
            else if (actualKey == Key.R)
            {
                ResetCurrentStreakOnly();
                e.Handled = true;
            }
            else if (actualKey == Key.Escape)
            {
                if (_isAutoSpinning)
                {
                    StopAutoSpin();
                    e.Handled = true;
                }
            }
        }

        // =====================================================================
        // Roulette Mode Select: 연속 (기본) / 선승제 / 단판
        // =====================================================================
        private void SetRouletteMode(int mode)
        {
            if (mode == _rouletteMode)
            {
                FocusMainWindowForShortcuts();
                return;
            }
            StopAutoSpin();
            _rouletteMode = mode;

            _currentCandidate = null;
            _currentStreak = 0;
            _roundWonPendingReset = false;
            _lastWasCandidateSwitch = false;
            _matchWins.Clear();
            _matchGameNo = 0;
            _wheelControl.IsWinnerHighlight = false;
            _wheelControl.InvalidateVisual();

            UpdateStreakModeUI();
            UpdateTargetButtonsUI();
            UpdateScoreboardUI();
            FocusMainWindowForShortcuts();
        }

        private string AutoSpinIdleLabel()
        {
            if (_rouletteMode == 1) return string.Format("⚡ {0}선승까지 자동", _state.BestOfWins);
            return string.Format("⚡ {0}연속까지 자동", _state.TargetStreak);
        }

        private void UpdateStreakModeUI()
        {
            for (int i = 0; i < _modeButtons.Count; i++)
            {
                bool active = (i == _rouletteMode);
                _modeButtons[i].Background = BrushFromHex(active ? "#2563EB" : "#0F172A");
                _modeButtons[i].Foreground = active ? Brushes.White : BrushFromHex("#94A3B8");
            }

            bool countMode = (_rouletteMode != 2);
            if (_rouletteMode == 0)
            {
                _scoreboardTitleText.Text = "연속 스트라이크 전광판";
                _historyTitleText.Text = "스트라이크 기록";
                _targetLabelText.Text = "목표 연속:";
                _bsoSLetter.Text = "S";
                _resetStreakBtn.ToolTip = "현재 연속 카운트만 0으로 초기화합니다 (단축키: R)";
            }
            else if (_rouletteMode == 1)
            {
                _scoreboardTitleText.Text = "선승제 전광판";
                _historyTitleText.Text = "선승제 기록";
                _targetLabelText.Text = "경기 방식:";
                _bsoSLetter.Text = "W";
                _resetStreakBtn.ToolTip = "현재 경기의 승수를 0으로 초기화합니다 (단축키: R)";
            }
            else
            {
                _scoreboardTitleText.Text = "단판 룰렛 전광판";
                _historyTitleText.Text = "스핀 기록";
            }

            for (int i = 0; i < _targetButtons.Count; i++)
                _targetButtons[i].Visibility = (_rouletteMode == 0) ? Visibility.Visible : Visibility.Collapsed;
            for (int i = 0; i < _bestOfButtons.Count; i++)
                _bestOfButtons[i].Visibility = (_rouletteMode == 1) ? Visibility.Visible : Visibility.Collapsed;

            _targetRowPanel.Visibility = countMode ? Visibility.Visible : Visibility.Collapsed;
            _resetStreakBtn.Visibility = countMode ? Visibility.Visible : Visibility.Collapsed;
            _bsoSLetter.Visibility = countMode ? Visibility.Visible : Visibility.Collapsed;
            _autoNewRoundCheck.Visibility = (_rouletteMode == 0) ? Visibility.Visible : Visibility.Collapsed;

            _autoSpinButton.IsEnabled = countMode;
            _autoSpinButton.Opacity = countMode ? 1.0 : 0.45;
            _autoSpinButton.Content = countMode ? AutoSpinIdleLabel() : "연속·선승제 전용";
            _autoSpinButton.Background = BrushFromHex(countMode ? "#059669" : "#334155");
        }

        // =====================================================================
        // Presets & Item List Logic
        // =====================================================================
        private void RefreshPresetCombo()
        {
            _suppressPresetComboEvent = true;
            _presetCombo.Items.Clear();
            for (int i = 0; i < _state.Presets.Count; i++)
            {
                _presetCombo.Items.Add(_state.Presets[i].PresetName);
            }
            _presetCombo.SelectedIndex = _state.SelectedPresetIndex;
            _suppressPresetComboEvent = false;
        }

        private void OnPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressPresetComboEvent) return;
            if (_presetCombo.SelectedIndex < 0 || _presetCombo.SelectedIndex >= _state.Presets.Count) return;

            StopAutoSpin();
            _state.SelectedPresetIndex = _presetCombo.SelectedIndex;
            if (_state.SelectedPresetIndex == 0)
            {
                CurrentPreset.Items.Clear();
            }
            ResetCurrentStreakOnly();
            RebuildItemListUI();
            RebuildWheelSlices();
            FocusMainWindowForShortcuts();
        }

        private void ClearCurrentItems()
        {
            StopAutoSpin();
            CurrentPreset.Items.Clear();
            ResetCurrentStreakOnly();
            RebuildItemListUI();
            RebuildWheelSlices();
            FocusMainWindowForShortcuts();
        }

        private void AddNewItemFromInput(bool returnFocusToWheel)
        {
            string raw = (_newItemInput.Text ?? "").Trim();
            if (string.IsNullOrEmpty(raw))
            {
                if (returnFocusToWheel) FocusMainWindowForShortcuts();
                return;
            }

            CurrentPreset.Items.Add(new RouletteItemData(raw, true));
            _newItemInput.Text = "";
            RebuildItemListUI();
            RebuildWheelSlices();
            ScheduleSave();

            if (returnFocusToWheel)
            {
                FocusMainWindowForShortcuts();
            }
        }

        private void OpenBulkEditDialog()
        {
            Window dlg = new Window
            {
                Title = "항목 일괄 텍스트 편집 (한 줄에 하나씩 입력)",
                Width = 420,
                Height = 460,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = BrushFromHex("#1E293B"),
                FontFamily = new FontFamily("Malgun Gothic"),
                ResizeMode = ResizeMode.CanResize
            };

            Grid g = new Grid { Margin = new Thickness(14) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock info = new TextBlock
            {
                Text = "한 줄에 항목을 하나씩 입력하거나 붙여넣으세요:",
                Foreground = BrushFromHex("#E2E8F0"),
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(info, 0);
            g.Children.Add(info);

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < CurrentPreset.Items.Count; i++)
            {
                sb.AppendLine(CurrentPreset.Items[i].Name);
            }

            TextBox editor = new TextBox
            {
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = BrushFromHex("#0F172A"),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                BorderBrush = BrushFromHex("#475569"),
                Padding = new Thickness(10),
                FontSize = 13.5,
                Text = sb.ToString()
            };
            InputMethod.SetIsInputMethodEnabled(editor, true);
            Grid.SetRow(editor, 1);
            g.Children.Add(editor);

            StackPanel btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            Button cancelBtn = MakeStyledButton("취소", "#475569", "#FFFFFF", 13, 34);
            cancelBtn.Padding = new Thickness(16, 0, 16, 0);
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.Click += (s, e) => dlg.DialogResult = false;
            btnRow.Children.Add(cancelBtn);

            Button okBtn = MakeStyledButton("적용하기", "#2563EB", "#FFFFFF", 13, 34);
            okBtn.Padding = new Thickness(18, 0, 18, 0);
            okBtn.Click += (s, e) => dlg.DialogResult = true;
            btnRow.Children.Add(okBtn);

            Grid.SetRow(btnRow, 2);
            g.Children.Add(btnRow);

            dlg.Content = g;
            if (dlg.ShowDialog() == true)
            {
                string[] lines = editor.Text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                CurrentPreset.Items.Clear();
                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = lines[i].Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        CurrentPreset.Items.Add(new RouletteItemData(trimmed, true));
                    }
                }
                RebuildItemListUI();
                RebuildWheelSlices();
                ScheduleSave();
            }
            FocusMainWindowForShortcuts();
        }

        private void RebuildItemListUI()
        {
            _itemListPanel.Children.Clear();
            List<RouletteItemData> items = CurrentPreset.Items;

            if (items.Count == 0)
            {
                _itemListPanel.Children.Add(new TextBlock
                {
                    Text = "항목이 비어 있습니다.\n위 입력창에 항목을 입력하고 Enter를 누르거나,\n프리셋 보관함에서 내장 프리셋을 선택해 보세요.",
                    FontSize = 12,
                    Foreground = BrushFromHex("#64748B"),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(8, 36, 8, 20)
                });
                _itemSummaryText.Text = "활성 항목: 0개 (위 입력창에서 바로 추가 가능)";
                return;
            }

            int enabledCount = 0;
            for (int i = 0; i < items.Count; i++)
            {
                int idx = i;
                RouletteItemData item = items[i];
                if (item.Enabled) enabledCount++;
                Color itemCol = SlicePalette[i % SlicePalette.Length];

                Border rowBorder = new Border
                {
                    Background = BrushFromHex(item.Enabled ? "#1E293B" : "#131C2E"),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 0, 0, 4)
                };

                Grid rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                CheckBox cb = new CheckBox
                {
                    IsChecked = item.Enabled,
                    Focusable = false,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 7, 0)
                };
                cb.Checked += (s, e) =>
                {
                    item.Enabled = true;
                    RebuildItemListUI();
                    RebuildWheelSlices();
                    ScheduleSave();
                };
                cb.Unchecked += (s, e) =>
                {
                    item.Enabled = false;
                    RebuildItemListUI();
                    RebuildWheelSlices();
                    ScheduleSave();
                };
                Grid.SetColumn(cb, 0);
                rowGrid.Children.Add(cb);

                Ellipse dot = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = new SolidColorBrush(item.Enabled ? itemCol : Color.FromRgb(71, 85, 105)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                Grid.SetColumn(dot, 1);
                rowGrid.Children.Add(dot);

                TextBlock nameTb = new TextBlock
                {
                    Text = item.Name,
                    Foreground = item.Enabled ? Brushes.White : BrushFromHex("#64748B"),
                    FontSize = 13,
                    FontWeight = item.Enabled ? FontWeights.SemiBold : FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Cursor = Cursors.Hand,
                    ToolTip = "더블클릭하여 이름 수정"
                };
                nameTb.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.ClickCount == 2)
                    {
                        string edited = PromptInputDialog("항목 이름 수정", "새 이름을 입력하세요:", item.Name);
                        if (!string.IsNullOrWhiteSpace(edited))
                        {
                            item.Name = edited.Trim();
                            RebuildItemListUI();
                            RebuildWheelSlices();
                            ScheduleSave();
                        }
                        FocusMainWindowForShortcuts();
                        e.Handled = true;
                    }
                };
                Grid.SetColumn(nameTb, 2);
                rowGrid.Children.Add(nameTb);

                int hitCount = 0;
                _sessionCounts.TryGetValue(item.Name, out hitCount);
                if (hitCount > 0)
                {
                    Border hitBadge = new Border
                    {
                        Background = BrushFromHex("#0F172A"),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(5, 1, 5, 1),
                        Margin = new Thickness(4, 0, 6, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    hitBadge.Child = new TextBlock
                    {
                        Text = hitCount + "회",
                        FontSize = 11,
                        Foreground = BrushFromHex("#94A3B8")
                    };
                    Grid.SetColumn(hitBadge, 3);
                    rowGrid.Children.Add(hitBadge);
                }

                Button delBtn = new Button
                {
                    Content = "✕",
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = BrushFromHex("#64748B"),
                    FontSize = 12,
                    Cursor = Cursors.Hand,
                    Focusable = false,
                    Padding = new Thickness(4, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                delBtn.Click += (s, e) =>
                {
                    items.RemoveAt(idx);
                    RebuildItemListUI();
                    RebuildWheelSlices();
                    ScheduleSave();
                };
                Grid.SetColumn(delBtn, 4);
                rowGrid.Children.Add(delBtn);

                rowBorder.Child = rowGrid;
                _itemListPanel.Children.Add(rowBorder);
            }

            if (enabledCount > 0)
            {
                double pct = 100.0 / enabledCount;
                _itemSummaryText.Text = string.Format("활성 항목: {0}개 (각 {1:0.#}%) · 더블클릭 시 이름 수정", enabledCount, pct);
            }
            else
            {
                _itemSummaryText.Text = "활성화된 항목이 없습니다 (최소 1개 이상 체크하세요)";
            }
        }

        private void RebuildWheelSlices()
        {
            List<WheelSlice> activeBase = new List<WheelSlice>();
            List<RouletteItemData> items = CurrentPreset.Items;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Enabled && !string.IsNullOrWhiteSpace(items[i].Name))
                {
                    activeBase.Add(new WheelSlice
                    {
                        Name = items[i].Name,
                        SliceColor = SlicePalette[i % SlicePalette.Length],
                        OriginalItemIndex = i
                    });
                }
            }

            List<WheelSlice> finalSlices = new List<WheelSlice>();
            if (_state.SplitSmallPool && (activeBase.Count == 2 || activeBase.Count == 3))
            {
                int repeat = (activeBase.Count == 2) ? 3 : 2;
                for (int r = 0; r < repeat; r++)
                {
                    for (int i = 0; i < activeBase.Count; i++)
                    {
                        finalSlices.Add(activeBase[i]);
                    }
                }
            }
            else
            {
                finalSlices.AddRange(activeBase);
            }

            _wheelControl.Slices = finalSlices;
            _wheelControl.IsWinnerHighlight = false;
            _wheelControl.InvalidateVisual();
        }

        private Color GetColorForItemName(string itemName)
        {
            List<RouletteItemData> items = CurrentPreset.Items;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Name == itemName)
                    return SlicePalette[i % SlicePalette.Length];
            }
            return SlicePalette[0];
        }

        // =====================================================================
        // Target Streak & Spin Speed Controls
        // =====================================================================
        private void SetTargetStreak(int target)
        {
            _state.TargetStreak = target;
            UpdateTargetButtonsUI();
            UpdateScoreboardUI();
            ScheduleSave();
        }

        private void SetBestOfWins(int wins)
        {
            _state.BestOfWins = wins;
            UpdateTargetButtonsUI();
            ResetCurrentStreakOnly(); // 필요 승수가 바뀌면 진행 중인 경기 승수는 초기화
        }

        private void UpdateTargetButtonsUI()
        {
            for (int i = 0; i < _targetButtons.Count + _bestOfButtons.Count; i++)
            {
                bool isStreakBtn = i < _targetButtons.Count;
                Button b = isStreakBtn ? _targetButtons[i] : _bestOfButtons[i - _targetButtons.Count];
                int val = (int)b.Tag;
                bool active = (val == (isStreakBtn ? _state.TargetStreak : _state.BestOfWins));
                b.Background = BrushFromHex(active ? "#2563EB" : "#1E293B");
                b.Foreground = active ? Brushes.White : BrushFromHex("#94A3B8");
                b.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
            }
            if (_autoSpinButton != null && !_isAutoSpinning && _rouletteMode != 2)
            {
                _autoSpinButton.Content = AutoSpinIdleLabel();
            }
        }

        private void SetSpinSpeedMode(int mode)
        {
            _state.SpinSpeedMode = mode;
            UpdateSpeedButtonsUI();
            ScheduleSave();
        }

        private void UpdateSpeedButtonsUI()
        {
            for (int i = 0; i < _speedButtons.Count; i++)
            {
                Button b = _speedButtons[i];
                int val = (int)b.Tag;
                bool active = (val == _state.SpinSpeedMode);
                b.Background = BrushFromHex(active ? "#3B82F6" : "#0F172A");
                b.Foreground = active ? Brushes.White : BrushFromHex("#94A3B8");
                b.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
            }
        }

        private double GetDurationSecondsForMode(int mode)
        {
            switch (mode)
            {
                case 0: return 2.05;
                case 1: return 0.72;
                case 2: return 0.24;
                case 3: return 0.0;
                default: return 0.72;
            }
        }

        // =====================================================================
        // Spinning & True Cryptographic Randomness
        // =====================================================================
        internal int NextFairRandomInt(int maxExclusive)
        {
            if (maxExclusive <= 1) return 0;
            byte[] buf = new byte[4];
            uint limit = uint.MaxValue - (uint.MaxValue % (uint)maxExclusive);
            while (true)
            {
                _rng.GetBytes(buf);
                uint val = BitConverter.ToUInt32(buf, 0);
                if (val < limit)
                {
                    return (int)(val % (uint)maxExclusive);
                }
            }
        }

        private double NextFairRandomDouble()
        {
            byte[] buf = new byte[4];
            _rng.GetBytes(buf);
            uint val = BitConverter.ToUInt32(buf, 0);
            return val / ((double)uint.MaxValue + 1.0);
        }

        private void OnSpinButtonClick()
        {
            FocusMainWindowForShortcuts();

            if (_isSpinning)
            {
                FinishSpinImmediately();
                return;
            }

            StartSingleSpin();
        }

        private void ToggleAutoSpin()
        {
            FocusMainWindowForShortcuts();

            if (_rouletteMode == 2) return;

            if (_isAutoSpinning)
            {
                StopAutoSpin();
                return;
            }

            if (_wheelControl.Slices == null || _wheelControl.Slices.Count == 0)
            {
                MessageBox.Show(this, "돌릴 수 있는 활성 항목이 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_roundWonPendingReset)
            {
                PrepareNextRoundInternal();
            }

            _isAutoSpinning = true;
            _autoSpinButton.Content = "⏹ 자동 중지 (Esc)";
            _autoSpinButton.Background = BrushFromHex("#DC2626");

            if (!_isSpinning)
            {
                StartSingleSpin();
            }
        }

        private void StopAutoSpin()
        {
            _isAutoSpinning = false;
            if (_autoSpinNextTimer != null) _autoSpinNextTimer.Stop();
            if (_autoSpinButton != null && _rouletteMode != 2)
            {
                _autoSpinButton.Content = AutoSpinIdleLabel();
                _autoSpinButton.Background = BrushFromHex("#059669");
            }
        }

        private void StartSingleSpin()
        {
            if (_wheelControl.Slices == null || _wheelControl.Slices.Count == 0)
            {
                MessageBox.Show(this, "왼쪽 목록에서 최소 1개 이상의 항목을 활성화해 주세요.", "항목 없음", MessageBoxButton.OK, MessageBoxImage.Information);
                StopAutoSpin();
                return;
            }

            // 선승제는 승부가 난 경기를 이어갈 수 없으므로 항상 새 라운드로 시작
            if (_roundWonPendingReset && (_rouletteMode == 1 || (_rouletteMode == 0 && _state.AutoNewRoundAfterWin)))
            {
                PrepareNextRoundInternal();
            }

            int sliceCount = _wheelControl.Slices.Count;
            int chosenSliceIdx = NextFairRandomInt(sliceCount);
            _plannedWinningSliceIdx = chosenSliceIdx;

            double sliceDeg = 360.0 / sliceCount;
            double insideRatio = 0.16 + NextFairRandomDouble() * 0.68;
            double desiredRelAngle = (chosenSliceIdx + insideRatio) * sliceDeg;
            double desiredModWheelRot = ((360.0 - desiredRelAngle) % 360.0 + 360.0) % 360.0;

            double currentMod = ((_wheelControl.WheelRotationDeg % 360.0) + 360.0) % 360.0;
            double forwardDelta = desiredModWheelRot - currentMod;
            if (forwardDelta <= 0) forwardDelta += 360.0;

            double duration = GetDurationSecondsForMode(_state.SpinSpeedMode);
            if (_isAutoSpinning && duration > 0.65)
            {
                duration = 0.55;
            }

            if (duration <= 0.01)
            {
                _wheelControl.WheelRotationDeg += 360.0 + forwardDelta;
                _wheelControl.NeedleKickDeg = 0.0;
                SoundEffects.PlayTick(_state.SoundEnabled);
                CompleteSpinWithSlice(chosenSliceIdx);
                return;
            }

            int extraRotations = (_state.SpinSpeedMode == 0) ? 5 : (_state.SpinSpeedMode == 1 ? 3 : 2);
            _spinStartAngle = _wheelControl.WheelRotationDeg;
            _spinTargetAngle = _spinStartAngle + extraRotations * 360.0 + forwardDelta;
            _spinDurationSec = duration;
            _wheelControl.IsWinnerHighlight = false;
            _lastObservedSliceIdx = _wheelControl.GetCurrentSliceIndex();

            _isSpinning = true;
            _spinStopwatch = Stopwatch.StartNew();
            _spinButton.Content = "⏩ 즉시 멈추기 (Space)";
            _spinButton.Background = BrushFromHex("#4F46E5");
        }

        private void OnAnimationFrame(object sender, EventArgs e)
        {
            if (Math.Abs(_wheelControl.NeedleKickDeg) > 0.1)
            {
                _wheelControl.NeedleKickDeg *= 0.80;
                if (Math.Abs(_wheelControl.NeedleKickDeg) <= 0.1)
                    _wheelControl.NeedleKickDeg = 0.0;
                if (!_isSpinning) _wheelControl.InvalidateVisual();
            }

            if (!_isSpinning || _spinStopwatch == null) return;

            double elapsed = _spinStopwatch.Elapsed.TotalSeconds;
            double t = elapsed / _spinDurationSec;

            if (t >= 1.0)
            {
                FinishSpinImmediately();
                return;
            }

            double inv = 1.0 - t;
            double ease = 1.0 - (inv * inv * inv * inv);
            _wheelControl.WheelRotationDeg = _spinStartAngle + (_spinTargetAngle - _spinStartAngle) * ease;

            int curIdx = _wheelControl.GetCurrentSliceIndex();
            if (curIdx != _lastObservedSliceIdx && curIdx >= 0)
            {
                _lastObservedSliceIdx = curIdx;
                _wheelControl.NeedleKickDeg = -16.0;
                SoundEffects.PlayTick(_state.SoundEnabled);
            }

            _wheelControl.InvalidateVisual();
        }

        private void FinishSpinImmediately()
        {
            _isSpinning = false;
            if (_spinStopwatch != null) _spinStopwatch.Stop();

            _wheelControl.WheelRotationDeg = _spinTargetAngle % 36000.0;
            _wheelControl.NeedleKickDeg = -8.0;

            _spinButton.Content = "▶ 돌리기 (Space)";
            _spinButton.Background = BrushFromHex("#2563EB");

            int winIdx = _plannedWinningSliceIdx;
            if (winIdx < 0 || winIdx >= _wheelControl.Slices.Count)
            {
                winIdx = _wheelControl.GetCurrentSliceIndex();
            }
            CompleteSpinWithSlice(winIdx);
        }

        // =====================================================================
        // Core Streak / Single-Spin Logic, Scoreboard & Auto-Stacking Feed
        // =====================================================================
        private void CompleteSpinWithSlice(int sliceIdx)
        {
            if (sliceIdx < 0 || sliceIdx >= _wheelControl.Slices.Count) return;

            WheelSlice winner = _wheelControl.Slices[sliceIdx];
            string itemName = winner.Name;
            Color itemColor = winner.SliceColor;

            _wheelControl.IsWinnerHighlight = true;
            _wheelControl.InvalidateVisual();

            // Session frequency counter
            int prevCount = 0;
            _sessionCounts.TryGetValue(itemName, out prevCount);
            _sessionCounts[itemName] = prevCount + 1;

            SpinRecord rec;

            if (_rouletteMode == 2)
            {
                // Single-spin mode (연속 OFF) — 1 spin = immediate result!
                _currentCandidate = itemName;
                _currentCandidateColor = itemColor;
                _currentStreak = 1;
                _lastWasCandidateSwitch = false;

                rec = new SpinRecord
                {
                    SpinNumber = _history.Count + 1,
                    RoundNumber = _currentRound,
                    ItemName = itemName,
                    ItemColor = itemColor,
                    StreakCount = 1,
                    TargetAtSpin = 1,
                    ChangedFromPrev = false,
                    IsTargetHit = true,
                    WasSingleMode = true,
                    Timestamp = DateTime.Now
                };
                _history.Add(rec);

                SoundEffects.PlayWin(_state.SoundEnabled);
                UpdateScoreboardUI();
                AppendHistoryRowUI(rec);
                RebuildItemListUI();
                UpdateFreqStripUI();

                if (_state.AutoMemoOnWin)
                {
                    AppendWinRecordToMemo(rec);
                }
                return;
            }

            if (_rouletteMode == 1)
            {
                // 선승제: 먼저 BestOfWins 승을 채운 항목이 승리 (판수 제한 없음)
                int need = _state.BestOfWins;
                _matchGameNo++;
                int wins = 0;
                _matchWins.TryGetValue(itemName, out wins);
                wins++;
                _matchWins[itemName] = wins;

                int maxWins = 0;
                foreach (KeyValuePair<string, int> kv in _matchWins)
                {
                    if (kv.Value > maxWins) maxWins = kv.Value;
                }
                // 선두 = 최다 승수, 동률이면 방금 뽑힌 항목
                if (wins == maxWins)
                {
                    _lastWasCandidateSwitch = !string.IsNullOrEmpty(_currentCandidate) && _currentCandidate != itemName;
                    _currentCandidate = itemName;
                    _currentCandidateColor = itemColor;
                }
                else
                {
                    _lastWasCandidateSwitch = false;
                }
                _currentStreak = maxWins;

                bool matchWon = wins >= need;
                if (matchWon) _roundWonPendingReset = true;

                rec = new SpinRecord
                {
                    SpinNumber = _history.Count + 1,
                    RoundNumber = _currentRound,
                    ItemName = itemName,
                    ItemColor = itemColor,
                    StreakCount = wins,
                    TargetAtSpin = need,
                    ChangedFromPrev = false,
                    IsTargetHit = matchWon,
                    WasSingleMode = false,
                    WasBestOf = true,
                    GameNo = _matchGameNo,
                    Timestamp = DateTime.Now
                };
                _history.Add(rec);

                if (matchWon)
                {
                    SoundEffects.PlayWin(_state.SoundEnabled);
                }
                else if (wins >= 2 && wins == need - 1)
                {
                    SoundEffects.PlayReach(_state.SoundEnabled);
                }

                UpdateScoreboardUI();
                AppendHistoryRowUI(rec);
                RebuildItemListUI();
                UpdateFreqStripUI();

                if (matchWon && _state.AutoMemoOnWin)
                {
                    AppendWinRecordToMemo(rec);
                }

                if (_isAutoSpinning)
                {
                    if (matchWon) StopAutoSpin();
                    else _autoSpinNextTimer.Start();
                }
                return;
            }

            // Streak mode (연속 ON)
            bool hadPreviousInRound = !string.IsNullOrEmpty(_currentCandidate);
            if (hadPreviousInRound && _currentCandidate == itemName)
            {
                _currentStreak++;
                _lastWasCandidateSwitch = false;
            }
            else
            {
                _currentCandidate = itemName;
                _currentCandidateColor = itemColor;
                _currentStreak = 1;
                _lastWasCandidateSwitch = hadPreviousInRound;
            }

            if (_currentStreak >= _state.TargetStreak)
            {
                _roundWonPendingReset = true;
            }

            rec = new SpinRecord
            {
                SpinNumber = _history.Count + 1,
                RoundNumber = _currentRound,
                ItemName = itemName,
                ItemColor = itemColor,
                StreakCount = _currentStreak,
                TargetAtSpin = _state.TargetStreak,
                ChangedFromPrev = _lastWasCandidateSwitch,
                IsTargetHit = (_currentStreak >= _state.TargetStreak),
                WasSingleMode = false,
                Timestamp = DateTime.Now
            };
            _history.Add(rec);

            if (_currentStreak >= _state.TargetStreak)
            {
                SoundEffects.PlayWin(_state.SoundEnabled);
            }
            else if (_currentStreak >= 2 && _currentStreak == _state.TargetStreak - 1)
            {
                SoundEffects.PlayReach(_state.SoundEnabled);
            }

            UpdateScoreboardUI();
            AppendHistoryRowUI(rec);
            RebuildItemListUI();
            UpdateFreqStripUI();

            if (_currentStreak == _state.TargetStreak && _state.AutoMemoOnWin)
            {
                AppendWinRecordToMemo(rec);
            }

            if (_isAutoSpinning)
            {
                if (_currentStreak >= _state.TargetStreak)
                {
                    StopAutoSpin();
                }
                else
                {
                    _autoSpinNextTimer.Start();
                }
            }
        }

        private void UpdateScoreboardUI()
        {
            if (_roundBadgeText != null)
            {
                _roundBadgeText.Text = "라운드 " + _currentRound;
            }

            // Single-spin mode UI (연속 OFF)
            if (_rouletteMode == 2)
            {
                _candidateCaptionText.Text = "이번 단판 당첨 결과";
                _strikeLampsPanel.Children.Clear();

                bool hasResult = !string.IsNullOrEmpty(_currentCandidate) && _currentStreak > 0;
                if (!hasResult)
                {
                    _scoreboardBorder.BorderBrush = BrushFromHex("#475569");
                    _scoreboardBorder.Background = BrushFromHex("#0B1120");
                    _candidateDot.Fill = BrushFromHex("#475569");
                    _candidateNameText.Text = "대기 중";
                    _candidateNameText.Foreground = BrushFromHex("#94A3B8");
                    _candidateTagBorder.Visibility = Visibility.Collapsed;
                    _scoreboardStatusText.Text = "단판 모드(연속 OFF) — 1번 돌린 결과가 바로 당첨됩니다!";
                    _scoreboardStatusText.Foreground = BrushFromHex("#CBD5E1");
                }
                else
                {
                    _scoreboardBorder.BorderBrush = BrushFromHex("#10B981");
                    _scoreboardBorder.Background = BrushFromHex("#071F18");
                    _candidateDot.Fill = new SolidColorBrush(_currentCandidateColor);
                    _candidateNameText.Text = _currentCandidate;
                    _candidateNameText.Foreground = BrushFromHex("#6EE7B7");
                    _candidateTagBorder.Visibility = Visibility.Visible;
                    _candidateTagBorder.Background = BrushFromHex("#059669");
                    _candidateTagText.Text = "🎯 단판 당첨!";

                    Border winStar = new Border
                    {
                        Width = 32,
                        Height = 32,
                        CornerRadius = new CornerRadius(16),
                        Background = BrushFromHex("#34D399"),
                        Child = new TextBlock
                        {
                            Text = "★",
                            FontSize = 16,
                            FontWeight = FontWeights.Bold,
                            Foreground = BrushFromHex("#064E3B"),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };
                    _strikeLampsPanel.Children.Add(winStar);

                    _scoreboardStatusText.Text = string.Format("🎯 단판 결과 확정: [{0}] 당첨!", _currentCandidate);
                    _scoreboardStatusText.Foreground = BrushFromHex("#34D399");
                }
                return;
            }

            if (_rouletteMode == 1)
            {
                UpdateBestOfScoreboardUI();
                return;
            }

            // Streak mode UI (연속 ON)
            _candidateCaptionText.Text = "현재 연속 후보";
            int target = _state.TargetStreak;
            int streak = _currentStreak;
            bool isWin = (streak >= target && streak > 0);
            bool isReach = (!isWin && streak >= 2 && streak == target - 1);
            bool isBuilding = (!isWin && streak >= 2);

            if (isWin)
            {
                _scoreboardBorder.BorderBrush = BrushFromHex("#10B981");
                _scoreboardBorder.Background = BrushFromHex("#071F18");
            }
            else if (isReach || isBuilding)
            {
                _scoreboardBorder.BorderBrush = BrushFromHex("#F59E0B");
                _scoreboardBorder.Background = BrushFromHex("#1C1408");
            }
            else
            {
                _scoreboardBorder.BorderBrush = BrushFromHex("#334155");
                _scoreboardBorder.Background = BrushFromHex("#0B1120");
            }

            if (streak == 0 || string.IsNullOrEmpty(_currentCandidate))
            {
                _candidateDot.Fill = BrushFromHex("#475569");
                _candidateNameText.Text = "대기 중";
                _candidateNameText.Foreground = BrushFromHex("#94A3B8");
                _candidateTagBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                _candidateDot.Fill = new SolidColorBrush(_currentCandidateColor);
                _candidateNameText.Text = _currentCandidate;
                _candidateNameText.Foreground = isWin ? BrushFromHex("#6EE7B7") : Brushes.White;
                _candidateTagBorder.Visibility = Visibility.Visible;

                if (isWin)
                {
                    _candidateTagBorder.Background = BrushFromHex("#059669");
                    _candidateTagText.Text = string.Format("🏆 {0}연속 달성!", streak);
                }
                else if (streak >= 2)
                {
                    _candidateTagBorder.Background = BrushFromHex("#EA580C");
                    _candidateTagText.Text = string.Format("🔥 {0}연속 적중!", streak);
                }
                else if (_lastWasCandidateSwitch)
                {
                    _candidateTagBorder.Background = BrushFromHex("#334155");
                    _candidateTagText.Text = "후보 교체 · 1S";
                }
                else
                {
                    _candidateTagBorder.Background = BrushFromHex("#1D4ED8");
                    _candidateTagText.Text = "첫 진입 · 1S";
                }
            }

            // Rebuild Baseball Strike Lamps (● ● ○)
            RebuildStrikeLamps(Math.Max(target, streak), streak, isWin, streak >= 2);

            if (streak == 0)
            {
                _scoreboardStatusText.Text = string.Format("목표 [{0}연속] 대기 중 — 스페이스바나 돌리기 버튼을 눌러주세요", target);
                _scoreboardStatusText.Foreground = BrushFromHex("#94A3B8");
            }
            else if (isWin)
            {
                _scoreboardStatusText.Text = string.Format("🎉 삼진/당첨 확정!! [{0}] 항목이 {1}연속으로 뽑혔습니다!", _currentCandidate, streak);
                _scoreboardStatusText.Foreground = BrushFromHex("#34D399");
            }
            else if (streak == target - 1)
            {
                if (streak == 1 && target == 2)
                {
                    string prefix = _lastWasCandidateSwitch ? "후보 교체 → " : "";
                    _scoreboardStatusText.Text = string.Format("{0}[{1}] 1스트라이크! 다음 번에도 같으면 2연속 당첨 확정!", prefix, _currentCandidate);
                    _scoreboardStatusText.Foreground = BrushFromHex("#FDE047");
                }
                else
                {
                    _scoreboardStatusText.Text = string.Format("🔥 리치!! [{0}] {1}연속 적중! 마지막 1번만 더 나오면 확정!", _currentCandidate, streak);
                    _scoreboardStatusText.Foreground = BrushFromHex("#FB923C");
                }
            }
            else
            {
                string prefix = _lastWasCandidateSwitch ? "후보 교체 → " : "";
                _scoreboardStatusText.Text = string.Format("{0}[{1}] {2}스트라이크 ({2} / {3})", prefix, _currentCandidate, streak, target);
                _scoreboardStatusText.Foreground = BrushFromHex("#E2E8F0");
            }
        }

        // 전광판 램프 (평면 디자인): litCount개 점등, 승리 시 초록+★, 2개 이상이면 주황, 1개면 노랑
        private void RebuildStrikeLamps(int lampCount, int litCount, bool isWin, bool isHot)
        {
            _strikeLampsPanel.Children.Clear();
            if (lampCount > 6) lampCount = 6;

            for (int i = 0; i < lampCount; i++)
            {
                bool lit = (i < litCount);
                string fillHex;
                if (!lit) fillHex = "#0B1120";
                else if (isWin) fillHex = "#34D399";
                else if (isHot) fillHex = "#FB923C";
                else fillHex = "#FBBF24";

                Border lamp = new Border
                {
                    Width = 28,
                    Height = 28,
                    CornerRadius = new CornerRadius(14),
                    Background = BrushFromHex(fillHex),
                    BorderBrush = BrushFromHex(lit ? fillHex : "#475569"),
                    BorderThickness = new Thickness(1.5),
                    Margin = new Thickness(3, 0, 3, 0)
                };

                if (lit && isWin)
                {
                    lamp.Child = new TextBlock
                    {
                        Text = "★",
                        FontSize = 13,
                        FontWeight = FontWeights.Bold,
                        Foreground = BrushFromHex("#064E3B"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }

                _strikeLampsPanel.Children.Add(lamp);
            }
        }

        private void UpdateBestOfScoreboardUI()
        {
            int need = _state.BestOfWins;
            int lead = _currentStreak;
            bool isWin = _roundWonPendingReset && lead >= need;
            bool isHot = !isWin && lead > 0 && lead == need - 1;

            _candidateCaptionText.Text = "현재 선두";

            if (isWin)
            {
                _scoreboardBorder.BorderBrush = BrushFromHex("#10B981");
                _scoreboardBorder.Background = BrushFromHex("#071F18");
            }
            else if (isHot)
            {
                _scoreboardBorder.BorderBrush = BrushFromHex("#F59E0B");
                _scoreboardBorder.Background = BrushFromHex("#1C1408");
            }
            else
            {
                _scoreboardBorder.BorderBrush = BrushFromHex("#334155");
                _scoreboardBorder.Background = BrushFromHex("#0B1120");
            }

            if (_matchGameNo == 0 || string.IsNullOrEmpty(_currentCandidate))
            {
                _candidateDot.Fill = BrushFromHex("#475569");
                _candidateNameText.Text = "대기 중";
                _candidateNameText.Foreground = BrushFromHex("#94A3B8");
                _candidateTagBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                _candidateDot.Fill = new SolidColorBrush(_currentCandidateColor);
                _candidateNameText.Text = _currentCandidate;
                _candidateNameText.Foreground = isWin ? BrushFromHex("#6EE7B7") : Brushes.White;
                _candidateTagBorder.Visibility = Visibility.Visible;
                if (isWin)
                {
                    _candidateTagBorder.Background = BrushFromHex("#059669");
                    _candidateTagText.Text = string.Format("🏆 {0}선승!", need);
                }
                else
                {
                    _candidateTagBorder.Background = BrushFromHex(isHot ? "#EA580C" : "#1D4ED8");
                    _candidateTagText.Text = string.Format("{0}승 / {1}승", lead, need);
                }
            }

            RebuildStrikeLamps(need, lead, isWin, lead >= 2);

            if (_matchGameNo == 0)
            {
                _scoreboardStatusText.Text = string.Format("{0}선승제 — 먼저 {0}번 뽑힌 항목이 승리합니다", need);
                _scoreboardStatusText.Foreground = BrushFromHex("#94A3B8");
            }
            else if (isWin)
            {
                _scoreboardStatusText.Text = string.Format("🎉 [{0}] {1}선승 달성! ({2}판 만에 결정)", _currentCandidate, need, _matchGameNo);
                _scoreboardStatusText.Foreground = BrushFromHex("#34D399");
            }
            else
            {
                List<string> parts = new List<string>();
                foreach (KeyValuePair<string, int> kv in _matchWins.OrderByDescending(x => x.Value).Take(5))
                {
                    parts.Add(string.Format("{0} {1}승", kv.Key, kv.Value));
                }
                _scoreboardStatusText.Text = string.Format("{0}판 진행 · {1}", _matchGameNo, string.Join(" · ", parts.ToArray()));
                _scoreboardStatusText.Foreground = BrushFromHex(isHot ? "#FB923C" : "#E2E8F0");
            }
        }

        private void AppendHistoryRowUI(SpinRecord rec)
        {
            _historyCountBadgeText.Text = _history.Count + "회";

            bool isWin = rec.IsTargetHit;
            bool isMulti = (!isWin && !rec.WasSingleMode && rec.StreakCount >= 2);

            string bgHex = "#151F32";
            string borderHex = "#26334D";
            if (isWin)
            {
                bgHex = "#0A2E23";
                borderHex = "#10B981";
            }
            else if (isMulti)
            {
                bgHex = "#291B0C";
                borderHex = "#F59E0B";
            }

            Border row = new Border
            {
                Background = BrushFromHex(bgHex),
                BorderBrush = BrushFromHex(borderHex),
                BorderThickness = new Thickness(isWin ? 1.8 : 1.0),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(9, 6, 9, 6),
                Margin = new Thickness(0, 0, 0, 4)
            };

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 34 });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock numTb = new TextBlock
            {
                Text = "#" + rec.SpinNumber,
                FontSize = 11.5,
                FontFamily = new FontFamily("Consolas, Malgun Gothic"),
                Foreground = BrushFromHex("#64748B"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            Grid.SetColumn(numTb, 0);
            g.Children.Add(numTb);

            Ellipse cDot = new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = new SolidColorBrush(rec.ItemColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0)
            };
            Grid.SetColumn(cDot, 1);
            g.Children.Add(cDot);

            TextBlock nameTb = new TextBlock
            {
                Text = rec.ItemName,
                FontSize = 13.5,
                FontWeight = (isWin || isMulti) ? FontWeights.Bold : FontWeights.SemiBold,
                Foreground = isWin ? BrushFromHex("#6EE7B7") : Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(nameTb, 2);
            g.Children.Add(nameTb);

            if (!rec.WasSingleMode)
            {
                StackPanel miniDots = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 7, 0)
                };
                int totalDots = Math.Max(rec.TargetAtSpin, rec.StreakCount);
                if (totalDots > 5) totalDots = 5;
                for (int d = 0; d < totalDots; d++)
                {
                    bool lit = (d < rec.StreakCount);
                    Color dCol;
                    if (!lit) dCol = Color.FromRgb(51, 65, 85);
                    else if (isWin) dCol = Color.FromRgb(52, 211, 153);
                    else if (isMulti) dCol = Color.FromRgb(251, 146, 60);
                    else dCol = Color.FromRgb(251, 191, 36);

                    Ellipse dotEl = new Ellipse
                    {
                        Width = 10.5,
                        Height = 10.5,
                        Fill = new SolidColorBrush(dCol),
                        Stroke = new SolidColorBrush(lit ? Colors.White : Color.FromRgb(71, 85, 105)),
                        StrokeThickness = lit ? 0.8 : 0.6,
                        Margin = new Thickness(1.8, 0, 1.8, 0)
                    };
                    miniDots.Children.Add(dotEl);
                }
                Grid.SetColumn(miniDots, 3);
                g.Children.Add(miniDots);
            }

            string badgeText;
            string badgeBg;
            string badgeFg;
            if (rec.WasSingleMode)
            {
                badgeText = "🎯 당첨!";
                badgeBg = "#059669";
                badgeFg = "#FFFFFF";
            }
            else if (rec.WasBestOf)
            {
                badgeText = isWin
                    ? string.Format("🏆 {0}선승!", rec.TargetAtSpin)
                    : string.Format("{0}판 · {1}승", rec.GameNo, rec.StreakCount);
                badgeBg = isWin ? "#059669" : (isMulti ? "#D97706" : "#1E293B");
                badgeFg = (isWin || isMulti) ? "#FFFFFF" : "#94A3B8";
            }
            else if (isWin)
            {
                badgeText = string.Format("🏆 {0}연속!", rec.StreakCount);
                badgeBg = "#059669";
                badgeFg = "#FFFFFF";
            }
            else if (isMulti)
            {
                badgeText = string.Format("🔥 {0}연속", rec.StreakCount);
                badgeBg = "#D97706";
                badgeFg = "#FFFFFF";
            }
            else
            {
                badgeText = rec.ChangedFromPrev ? "1S·교체" : "1S";
                badgeBg = "#1E293B";
                badgeFg = "#94A3B8";
            }

            Border pill = new Border
            {
                Background = BrushFromHex(badgeBg),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(4, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            pill.Child = new TextBlock
            {
                Text = badgeText,
                FontSize = 11,
                FontWeight = (isWin || isMulti) ? FontWeights.Bold : FontWeights.Normal,
                Foreground = BrushFromHex(badgeFg)
            };
            Grid.SetColumn(pill, 4);
            g.Children.Add(pill);

            Button rowMemoBtn = new Button
            {
                Content = "+메모",
                Background = BrushFromHex("#1E293B"),
                Foreground = BrushFromHex("#93C5FD"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                FontSize = 10.5,
                Padding = new Thickness(5, 1, 5, 1),
                Cursor = Cursors.Hand,
                Focusable = false,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "이 결과를 아래 메모장에 추가합니다"
            };
            SpinRecord capturedRec = rec;
            rowMemoBtn.Click += (s, e) => AppendSpinRecordToMemoManual(capturedRec);
            Grid.SetColumn(rowMemoBtn, 5);
            g.Children.Add(rowMemoBtn);

            row.Child = g;
            _historyStack.Children.Add(row);
            _historyScroll.ScrollToEnd();
        }

        private void AddRoundDividerInFeed(int roundNum)
        {
            Border div = new Border
            {
                Margin = new Thickness(0, 6, 0, 6),
                Padding = new Thickness(4, 3, 4, 3),
                Background = BrushFromHex("#1E293B"),
                CornerRadius = new CornerRadius(4)
            };
            div.Child = new TextBlock
            {
                Text = string.Format("── 라운드 {0} (새 카운트 시작) ──", roundNum),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFromHex("#93C5FD"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _historyStack.Children.Add(div);
            _historyScroll.ScrollToEnd();
        }

        private void UpdateFreqStripUI()
        {
            _freqWrapPanel.Children.Clear();
            if (_sessionCounts.Count == 0)
            {
                _freqWrapPanel.Children.Add(new TextBlock
                {
                    Text = "아직 돌린 기록이 없습니다 (항목별 누적 횟수가 표시됩니다)",
                    FontSize = 11.5,
                    Foreground = BrushFromHex("#64748B")
                });
                return;
            }

            List<KeyValuePair<string, int>> sorted = _sessionCounts.OrderByDescending(kv => kv.Value).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                KeyValuePair<string, int> kv = sorted[i];
                Color col = GetColorForItemName(kv.Key);

                Border chip = new Border
                {
                    Background = BrushFromHex("#1E293B"),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 2, 5, 2)
                };
                StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(col),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0)
                });
                sp.Children.Add(new TextBlock
                {
                    Text = string.Format("{0} {1}회", kv.Key, kv.Value),
                    FontSize = 11.5,
                    Foreground = BrushFromHex("#E2E8F0")
                });
                chip.Child = sp;
                _freqWrapPanel.Children.Add(chip);
            }
        }

        private void PrepareNextRoundInternal()
        {
            _currentRound++;
            _currentCandidate = null;
            _currentStreak = 0;
            _roundWonPendingReset = false;
            _lastWasCandidateSwitch = false;
            _matchWins.Clear();
            _matchGameNo = 0;
            if (_history.Count > 0)
            {
                AddRoundDividerInFeed(_currentRound);
            }
            UpdateScoreboardUI();
        }

        private void StartNewRoundManual()
        {
            StopAutoSpin();
            PrepareNextRoundInternal();
            FocusMainWindowForShortcuts();
        }

        private void ResetCurrentStreakOnly()
        {
            StopAutoSpin();
            _currentCandidate = null;
            _currentStreak = 0;
            _roundWonPendingReset = false;
            _lastWasCandidateSwitch = false;
            _matchWins.Clear();
            _matchGameNo = 0;
            _wheelControl.IsWinnerHighlight = false;
            _wheelControl.InvalidateVisual();
            UpdateScoreboardUI();
            FocusMainWindowForShortcuts();
        }

        private void ClearHistory()
        {
            StopAutoSpin();
            _history.Clear();
            _sessionCounts.Clear();
            _historyStack.Children.Clear();
            _currentRound = 1;
            _currentCandidate = null;
            _currentStreak = 0;
            _roundWonPendingReset = false;
            _lastWasCandidateSwitch = false;
            _matchWins.Clear();
            _matchGameNo = 0;

            _historyCountBadgeText.Text = "0회";
            UpdateScoreboardUI();
            RebuildItemListUI();
            UpdateFreqStripUI();
            FocusMainWindowForShortcuts();
        }

        // =====================================================================
        // Memo Pad Operations
        // =====================================================================
        private void AppendLineToMemo(string line)
        {
            string existing = _memoTextBox.Text ?? "";
            if (existing.Length > 0 && !existing.EndsWith("\n"))
            {
                existing += "\r\n";
            }
            existing += line + "\r\n";

            _suppressMemoTextChanged = true;
            _memoTextBox.Text = existing;
            _memoTextBox.CaretIndex = _memoTextBox.Text.Length;
            _memoTextBox.ScrollToEnd();
            _suppressMemoTextChanged = false;

            ScheduleSave();
        }

        private void AppendWinRecordToMemo(SpinRecord rec)
        {
            string line;
            if (rec.WasSingleMode)
            {
                line = string.Format(
                    "[{0:HH:mm:ss}] 🎯 단판 당첨: {1} ({2}, #{3}회차)",
                    rec.Timestamp,
                    rec.ItemName,
                    CurrentPreset.PresetName,
                    rec.SpinNumber);
            }
            else if (rec.WasBestOf)
            {
                line = string.Format(
                    "[{0:HH:mm:ss}] 🏆 {1}선승 당첨: {2} ({3}, {4}판 만에 결정, #{5}회차)",
                    rec.Timestamp,
                    rec.TargetAtSpin,
                    rec.ItemName,
                    CurrentPreset.PresetName,
                    rec.GameNo,
                    rec.SpinNumber);
            }
            else
            {
                line = string.Format(
                    "[{0:HH:mm:ss}] 🏆 {1}연속 당첨: {2} ({3}, #{4}회차)",
                    rec.Timestamp,
                    rec.StreakCount,
                    rec.ItemName,
                    CurrentPreset.PresetName,
                    rec.SpinNumber);
            }
            AppendLineToMemo(line);
        }

        private void AppendSpinRecordToMemoManual(SpinRecord rec)
        {
            string tag;
            if (rec.WasSingleMode)
                tag = "🎯 단판 당첨";
            else if (rec.WasBestOf)
                tag = rec.IsTargetHit
                    ? string.Format("🏆 {0}선승 확정", rec.TargetAtSpin)
                    : string.Format("{0}판째 · {1}승", rec.GameNo, rec.StreakCount);
            else if (rec.IsTargetHit)
                tag = string.Format("🏆 {0}연속 확정", rec.StreakCount);
            else
                tag = string.Format("{0}연속", rec.StreakCount);

            string line = string.Format(
                "[{0:HH:mm:ss}] #{1} {2} ({3})",
                rec.Timestamp,
                rec.SpinNumber,
                rec.ItemName,
                tag);
            AppendLineToMemo(line);
        }

        private void AppendLatestResultToMemo()
        {
            if (_history.Count == 0)
            {
                AppendLineToMemo(string.Format("[{0:HH:mm:ss}] (아직 스핀 결과 없음)", DateTime.Now));
                return;
            }
            SpinRecord latest = _history[_history.Count - 1];
            AppendSpinRecordToMemoManual(latest);
        }

        private void AppendSummaryToMemo()
        {
            if (_sessionCounts.Count == 0)
            {
                MessageBox.Show(this, "아직 돌린 통계가 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in _sessionCounts.OrderByDescending(x => x.Value))
            {
                parts.Add(string.Format("{0}:{1}회", kv.Key, kv.Value));
            }
            string summary = string.Format(
                "[{0:HH:mm}] 📊 누적 {1}회 요약 ({2}) → {3}",
                DateTime.Now,
                _history.Count,
                CurrentPreset.PresetName,
                string.Join(", ", parts.ToArray()));
            AppendLineToMemo(summary);
        }

        private void CopyMemoToClipboard()
        {
            try
            {
                Clipboard.SetText(_memoTextBox.Text ?? "");
                _copyFeedbackText.Text = "✔ 클립보드에 복사됨!";
            }
            catch { }
        }

        private void ClearMemoPad()
        {
            if (string.IsNullOrWhiteSpace(_memoTextBox.Text)) return;
            MessageBoxResult res = MessageBox.Show(
                this,
                "메모장 내용을 모두 비울까요?",
                "메모 비우기",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                _memoTextBox.Text = "";
                ScheduleSave();
            }
            FocusMainWindowForShortcuts();
        }

        // =====================================================================
        // Reusable UI Helpers
        // =====================================================================
        internal Border MakeCardBorder()
        {
            return new Border
            {
                // Borderless floating card: separation comes from surface tone, not outlines
                Background = BrushFromHex("#1E293B"),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(16)
            };
        }

        internal Button MakeStyledButton(string text, string bgHex, string fgHex, double fontSize, double height)
        {
            Button btn = new Button
            {
                Content = text,
                Height = height,
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                Background = BrushFromHex(bgHex),
                Foreground = BrushFromHex(fgHex),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Focusable = false
            };

            // Flat button: background + white overlay that brightens on hover/press (reads better on dark surfaces than opacity)
            ControlTemplate tpl = new ControlTemplate(typeof(Button));
            FrameworkElementFactory root = new FrameworkElementFactory(typeof(Grid));

            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "btnBorder";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            root.AppendChild(border);

            FrameworkElementFactory overlay = new FrameworkElementFactory(typeof(Border));
            overlay.Name = "btnOverlay";
            overlay.SetValue(Border.BackgroundProperty, Brushes.White);
            overlay.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            overlay.SetValue(UIElement.OpacityProperty, 0.0);
            overlay.SetValue(UIElement.IsHitTestVisibleProperty, false);
            root.AppendChild(overlay);

            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            root.AppendChild(cp);

            tpl.VisualTree = root;

            Trigger hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.08, "btnOverlay"));
            tpl.Triggers.Add(hoverTrigger);

            Trigger pressTrigger = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.14, "btnOverlay"));
            tpl.Triggers.Add(pressTrigger);

            btn.Template = tpl;
            return btn;
        }

        internal Button MakeSmallButton(string text, string bgHex, string fgHex)
        {
            Button b = MakeStyledButton(text, bgHex, fgHex, 11.5, 27);
            b.FontWeight = FontWeights.SemiBold;
            b.Padding = new Thickness(9, 2, 9, 2);
            return b;
        }

        internal string ShowMultilineDialog(string title, string infoText, string initialText)
        {
            Window dlg = new Window
            {
                Title = title,
                Width = 430,
                Height = 470,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = BrushFromHex("#1E293B"),
                FontFamily = new FontFamily("Malgun Gothic"),
                ResizeMode = ResizeMode.CanResize
            };

            Grid g = new Grid { Margin = new Thickness(16) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock info = new TextBlock
            {
                Text = infoText,
                Foreground = BrushFromHex("#E2E8F0"),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(info, 0);
            g.Children.Add(info);

            TextBox editor = new TextBox
            {
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = BrushFromHex("#0F172A"),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                BorderBrush = BrushFromHex("#475569"),
                Padding = new Thickness(10),
                FontSize = 13.5,
                Text = initialText ?? ""
            };
            InputMethod.SetIsInputMethodEnabled(editor, true);
            Grid.SetRow(editor, 1);
            g.Children.Add(editor);

            StackPanel btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            Button cancelBtn = MakeStyledButton("취소", "#475569", "#FFFFFF", 13, 34);
            cancelBtn.Padding = new Thickness(16, 0, 16, 0);
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.Click += (s, e) => dlg.DialogResult = false;
            btnRow.Children.Add(cancelBtn);

            Button okBtn = MakeStyledButton("적용하기", "#2563EB", "#FFFFFF", 13, 34);
            okBtn.Padding = new Thickness(18, 0, 18, 0);
            okBtn.Click += (s, e) => dlg.DialogResult = true;
            btnRow.Children.Add(okBtn);

            Grid.SetRow(btnRow, 2);
            g.Children.Add(btnRow);

            dlg.Content = g;
            dlg.Loaded += (s, e) => { editor.Focus(); editor.CaretIndex = editor.Text.Length; };
            bool? res = dlg.ShowDialog();
            FocusMainWindowForShortcuts();
            return res == true ? editor.Text : null;
        }

        internal string PromptInputDialog(string title, string message, string defaultVal)
        {
            Window dlg = new Window
            {
                Title = title,
                Width = 360,
                Height = 175,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = BrushFromHex("#1E293B"),
                FontFamily = new FontFamily("Malgun Gothic"),
                ResizeMode = ResizeMode.NoResize
            };

            StackPanel sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8)
            });

            TextBox tb = new TextBox
            {
                Text = defaultVal ?? "",
                Height = 32,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = BrushFromHex("#0F172A"),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                BorderBrush = BrushFromHex("#475569"),
                Padding = new Thickness(8, 0, 8, 0),
                FontSize = 13
            };
            InputMethod.SetIsInputMethodEnabled(tb, true);
            tb.SelectAll();
            tb.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { dlg.DialogResult = true; e.Handled = true; }
            };
            sp.Children.Add(tb);

            StackPanel btns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            Button cancel = MakeSmallButton("취소", "#475569", "#FFFFFF");
            cancel.Margin = new Thickness(0, 0, 8, 0);
            cancel.Click += (s, e) => dlg.DialogResult = false;
            btns.Children.Add(cancel);

            Button ok = MakeSmallButton("확인", "#2563EB", "#FFFFFF");
            ok.Click += (s, e) => dlg.DialogResult = true;
            btns.Children.Add(ok);
            sp.Children.Add(btns);

            dlg.Content = sp;
            dlg.Loaded += (s, e) => tb.Focus();

            return dlg.ShowDialog() == true ? tb.Text : null;
        }

        // v1.2 디자인: 모든 탭이 이 함수를 거치므로 여기서 남색 slate 팔레트를 중립 그래파이트 + 단일 인디고 액센트로 일괄 치환
        private static readonly Dictionary<string, string> ThemeMap = new Dictionary<string, string>
        {
            // Neutrals (navy slate → graphite)
            { "#0B1120", "#0D0E11" }, { "#0F172A", "#111216" }, { "#111827", "#15161A" },
            { "#131C31", "#16171B" }, { "#131C2E", "#15161A" }, { "#151F32", "#18191E" },
            { "#111E3B", "#171A26" }, { "#0F1E3A", "#171A26" }, { "#1E293B", "#1A1B20" },
            { "#26334D", "#2A2C33" }, { "#334155", "#2A2C33" }, { "#475569", "#3A3D46" },
            { "#64748B", "#6E727D" }, { "#94A3B8", "#A0A3AC" }, { "#CBD5E1", "#CDCFD4" },
            { "#E2E8F0", "#E6E7EA" }, { "#F1F5F9", "#F4F4F6" }, { "#F8FAFC", "#F4F4F6" },
            // Blues → single indigo accent
            { "#2563EB", "#5B6CF0" }, { "#3B82F6", "#6878F2" }, { "#1D4ED8", "#4A5BE0" },
            { "#4F46E5", "#4351D6" }, { "#38BDF8", "#8B97F7" }, { "#93C5FD", "#A9B3FA" },
            { "#172554", "#1C1F3A" }, { "#1E3A8A", "#2A3170" },
            // Greens / ambers / reds / violet (slightly desaturated)
            { "#059669", "#2E9A6B" }, { "#10B981", "#3DB47F" }, { "#34D399", "#6CCB9E" },
            { "#6EE7B7", "#95DDBB" }, { "#071F18", "#12201A" }, { "#0A2E23", "#14251E" },
            { "#064E3B", "#0F3B2B" }, { "#D97706", "#C98A2E" }, { "#F59E0B", "#E0A243" },
            { "#FBBF24", "#EDBB5A" }, { "#FB923C", "#E89A5E" }, { "#EA580C", "#D2763A" },
            { "#1C1408", "#211C12" }, { "#291B0C", "#231D14" }, { "#FDE68A", "#F4D99A" },
            { "#DC2626", "#D24C4C" }, { "#FCA5A5", "#EFA3A3" }, { "#7C3AED", "#7A62E8" }
        };

        internal static SolidColorBrush BrushFromHex(string hex)
        {
            string mapped;
            if (hex != null && ThemeMap.TryGetValue(hex.ToUpperInvariant(), out mapped)) hex = mapped;
            Color c = (Color)ColorConverter.ConvertFromString(hex);
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }

    // =========================================================================
    // v1.2 Theme: dark implicit styles for native controls + dark title bar
    // =========================================================================
    internal static class AppTheme
    {
        private const string StylesXaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <Style TargetType='CheckBox'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <Grid Background='Transparent'>
            <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
            <Border x:Name='box' Width='17' Height='17' CornerRadius='5' Background='#0D0E11'
                    BorderBrush='#4A4D57' BorderThickness='1.5' VerticalAlignment='Center'>
              <Path x:Name='mark' Data='M3.2,7.6 L6.2,10.6 L11.4,4.6' Stroke='White' StrokeThickness='2'
                    StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' Visibility='Collapsed'/>
            </Border>
            <ContentPresenter x:Name='cp' Grid.Column='1' Margin='8,0,0,0' VerticalAlignment='Center'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='Content' Value='{x:Null}'><Setter TargetName='cp' Property='Margin' Value='0'/></Trigger>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='box' Property='BorderBrush' Value='#8B97F7'/></Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='box' Property='Background' Value='#5B6CF0'/>
              <Setter TargetName='box' Property='BorderBrush' Value='#5B6CF0'/>
              <Setter TargetName='mark' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <ControlTemplate x:Key='ThumbTpl' TargetType='Thumb'>
    <Border x:Name='t' CornerRadius='4' Background='#3A3D46'/>
    <ControlTemplate.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='t' Property='Background' Value='#5A5E6A'/></Trigger>
      <Trigger Property='IsDragging' Value='True'><Setter TargetName='t' Property='Background' Value='#6E727D'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>
  <ControlTemplate x:Key='PageTpl' TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate>

  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='10'/>
    <Setter Property='MinWidth' Value='10'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Track x:Name='PART_Track' IsDirectionReversed='True' Margin='2,4'>
            <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Template='{StaticResource PageTpl}' Focusable='False'/></Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Template='{StaticResource PageTpl}' Focusable='False'/></Track.IncreaseRepeatButton>
            <Track.Thumb><Thumb Template='{StaticResource ThumbTpl}'/></Track.Thumb>
          </Track>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Width' Value='Auto'/>
        <Setter Property='MinWidth' Value='0'/>
        <Setter Property='Height' Value='10'/>
        <Setter Property='MinHeight' Value='10'/>
        <Setter Property='Template'>
          <Setter.Value>
            <ControlTemplate TargetType='ScrollBar'>
              <Track x:Name='PART_Track' Margin='4,2'>
                <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageLeftCommand' Template='{StaticResource PageTpl}' Focusable='False'/></Track.DecreaseRepeatButton>
                <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageRightCommand' Template='{StaticResource PageTpl}' Focusable='False'/></Track.IncreaseRepeatButton>
                <Track.Thumb><Thumb Template='{StaticResource ThumbTpl}'/></Track.Thumb>
              </Track>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style TargetType='ComboBoxItem'>
    <Setter Property='Foreground' Value='#E6E7EA'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBoxItem'>
          <Border x:Name='bd' CornerRadius='6' Padding='10,7' Background='Transparent'><ContentPresenter/></Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='bd' Property='Background' Value='#2A2C33'/></Trigger>
            <Trigger Property='IsSelected' Value='True'><Setter Property='Foreground' Value='#A9B3FA'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ComboBox'>
    <Setter Property='Foreground' Value='#E6E7EA'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBox'>
          <Grid>
            <ToggleButton Focusable='False' ClickMode='Press'
                          IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
              <ToggleButton.Template>
                <ControlTemplate TargetType='ToggleButton'>
                  <Border x:Name='bd' Background='#0D0E11' BorderBrush='#2A2C33' BorderThickness='1' CornerRadius='8'>
                    <Path HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,12,0'
                          Data='M0,0 L4.5,4.5 L9,0' Stroke='#A0A3AC' StrokeThickness='1.6'/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='#4A4D57'/></Trigger>
                    <Trigger Property='IsChecked' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='#5B6CF0'/></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter IsHitTestVisible='False' Margin='12,0,32,0' VerticalAlignment='Center'
                              Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'/>
            <Popup x:Name='PART_Popup' Placement='Bottom' AllowsTransparency='True' Focusable='False'
                   PopupAnimation='Fade' IsOpen='{TemplateBinding IsDropDownOpen}'>
              <Border Background='#1A1B20' BorderBrush='#2A2C33' BorderThickness='1' CornerRadius='10' Padding='4' Margin='0,4,0,0'
                      MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'
                      MaxHeight='{TemplateBinding MaxDropDownHeight}'>
                <ScrollViewer><ItemsPresenter/></ScrollViewer>
              </Border>
            </Popup>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ToolTip'>
    <Setter Property='Foreground' Value='#E6E7EA'/>
    <Setter Property='FontFamily' Value='Malgun Gothic'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='MaxWidth' Value='380'/>
    <Setter Property='HasDropShadow' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToolTip'>
          <Border Background='#2A2C33' CornerRadius='7' Padding='10,6'>
            <ContentPresenter>
              <ContentPresenter.Resources>
                <Style TargetType='TextBlock'><Setter Property='TextWrapping' Value='Wrap'/></Style>
              </ContentPresenter.Resources>
            </ContentPresenter>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";

        public static void Apply()
        {
            Application app = Application.Current;
            if (app == null) return;
            app.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse(StylesXaml));
            // Every window (main + 일괄 편집 등 대화상자) gets the dark caption
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((s, e) => { Window w = s as Window; if (w != null) ApplyDarkTitleBar(w); }));
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // Dark caption on Windows 10 20H1+ / 11; silently ignored elsewhere
        public static void ApplyDarkTitleBar(Window w)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(w).Handle;
                int on = 1;
                DwmSetWindowAttribute(hwnd, 20, ref on, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE
                int caption = 0x00110E0D; // COLORREF (BGR) of #0D0E11, matches the sidebar
                DwmSetWindowAttribute(hwnd, 35, ref caption, 4); // DWMWA_CAPTION_COLOR (Windows 11)
            }
            catch { }
        }
    }

    // =========================================================================
    // Entry Point
    // =========================================================================
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            Application app = new Application();
            AppTheme.Apply();
            MainWindow win = new MainWindow();
            app.Run(win);
        }
    }
}
