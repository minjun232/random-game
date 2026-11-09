using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace StrikeRoulette
{
    public class LadderPathPoint
    {
        public int Col;
        public double NormY; // 0.0 (top) .. 1.0 (bottom)

        public LadderPathPoint(int col, double normY)
        {
            Col = col;
            NormY = normY;
        }
    }

    public class LadderBridge
    {
        public int LeftCol;
        public double NormY; // 0.0 (top) .. 1.0 (bottom)
        public bool IsCustom;

        public LadderBridge(int leftCol, double normY, bool isCustom)
        {
            LeftCol = leftCol;
            NormY = normY;
            IsCustom = isCustom;
        }
    }

    public class LadderBoardControl : FrameworkElement
    {
        public List<string> PlayerNames = new List<string>();
        public List<string> BottomResults = new List<string>();
        public List<LadderBridge> BridgeList = new List<LadderBridge>();

        // Default ON! Once the first player starts (CurtainRevealed = true),
        // the curtain stays lifted for the rest of the round (never re-hiding between Player 1 and Player 2).
        public bool HideBridges = true;
        public bool CurtainRevealed = false;

        public bool[] CompletedPlayers = new bool[0];
        public int[] EndColumnByPlayer = new int[0];
        public List<LadderPathPoint>[] PathsByPlayer = new List<LadderPathPoint>[0];

        // Active animation & interactive hover states
        public int AnimatingPlayer = -1;
        public double AnimProgress = 0.0; // 0..1
        public int HighlightPlayer = -1;
        public int HoverPlayer = -1;
        public int HoverAddCol = -1;
        public double HoverAddNormY = 0.0;
        public int HoverDeleteBridgeIdx = -1;

        public event Action<int> PlayerClicked;
        public event Action<int, double> BridgeAddRequested;
        public event Action<int> BridgeDeleteRequested;

        public LadderBoardControl()
        {
            ClipToBounds = true;
            Cursor = Cursors.Arrow;
            MouseMove += OnBoardMouseMove;
            MouseLeave += (s, e) =>
            {
                if (HoverPlayer != -1 || HoverAddCol != -1 || HoverDeleteBridgeIdx != -1)
                {
                    HoverPlayer = -1;
                    HoverAddCol = -1;
                    HoverDeleteBridgeIdx = -1;
                    Cursor = Cursors.Arrow;
                    InvalidateVisual();
                }
            };
            MouseLeftButtonDown += OnBoardMouseLeftDown;
            MouseRightButtonDown += OnBoardMouseRightDown;
        }

        private double GetColX(int col, int count, double w)
        {
            if (count <= 1) return w * 0.5;
            double padX = Math.Max(44.0, Math.Min(70.0, w / (count * 1.6)));
            double usable = Math.Max(40.0, w - padX * 2.0);
            return padX + usable * ((double)col / (count - 1));
        }

        private Rect GetTopPillRect(int col, int count, double w)
        {
            double cx = GetColX(col, count, w);
            double spacing = (count > 1) ? ((w - 88.0) / (count - 1)) : 120.0;
            double pillW = Math.Max(42.0, Math.Min(92.0, spacing * 0.86));
            return new Rect(cx - pillW * 0.5, 10.0, pillW, 32.0);
        }

        private Rect GetBottomPillRect(int col, int count, double w, double h)
        {
            double cx = GetColX(col, count, w);
            double spacing = (count > 1) ? ((w - 88.0) / (count - 1)) : 120.0;
            double pillW = Math.Max(42.0, Math.Min(92.0, spacing * 0.86));
            return new Rect(cx - pillW * 0.5, h - 42.0, pillW, 32.0);
        }

        private void UpdateHoverStateFromPoint(Point pt)
        {
            int n = PlayerNames.Count;
            double w = ActualWidth;
            double h = ActualHeight;

            int newHoverPlayer = -1;
            int newAddCol = -1;
            double newAddNormY = 0.0;
            int newDelIdx = -1;

            if (n > 0 && w > 10 && h > 80)
            {
                for (int i = 0; i < n; i++)
                {
                    Rect r = GetTopPillRect(i, n, w);
                    r.Inflate(4, 4);
                    if (r.Contains(pt))
                    {
                        newHoverPlayer = i;
                        break;
                    }
                }

                if (newHoverPlayer < 0 && AnimatingPlayer < 0 && n >= 2)
                {
                    double topY = 48.0;
                    double botY = h - 48.0;
                    double railH = Math.Max(40.0, botY - topY);

                    if (pt.Y >= topY + 12.0 && pt.Y <= botY - 12.0)
                    {
                        int spanCol = -1;
                        for (int c = 0; c < n - 1; c++)
                        {
                            double x1 = GetColX(c, n, w);
                            double x2 = GetColX(c + 1, n, w);
                            if (pt.X >= x1 + 3.0 && pt.X <= x2 - 3.0)
                            {
                                spanCol = c;
                                break;
                            }
                        }

                        if (spanCol >= 0)
                        {
                            bool showCurtain = HideBridges && !CurtainRevealed;
                            double bestDist = 8.0;
                            if (BridgeList != null)
                            {
                                for (int i = 0; i < BridgeList.Count; i++)
                                {
                                    LadderBridge b = BridgeList[i];
                                    if (b.LeftCol != spanCol) continue;
                                    if (showCurtain && !b.IsCustom) continue;

                                    double by = topY + railH * b.NormY;
                                    double dy = Math.Abs(pt.Y - by);
                                    if (dy <= bestDist)
                                    {
                                        bestDist = dy;
                                        newDelIdx = i;
                                    }
                                }
                            }

                            if (newDelIdx < 0)
                            {
                                newAddCol = spanCol;
                                newAddNormY = Math.Max(0.045, Math.Min(0.955, (pt.Y - topY) / railH));
                            }
                        }
                    }
                }
            }

            bool changed = (newHoverPlayer != HoverPlayer)
                || (newAddCol != HoverAddCol)
                || (newDelIdx != HoverDeleteBridgeIdx)
                || (newAddCol >= 0 && Math.Abs(newAddNormY - HoverAddNormY) > 0.001);

            HoverPlayer = newHoverPlayer;
            HoverAddCol = newAddCol;
            HoverAddNormY = newAddNormY;
            HoverDeleteBridgeIdx = newDelIdx;

            if (HoverPlayer >= 0 || HoverAddCol >= 0 || HoverDeleteBridgeIdx >= 0)
                Cursor = Cursors.Hand;
            else
                Cursor = Cursors.Arrow;

            if (changed)
            {
                InvalidateVisual();
            }
        }

        private void OnBoardMouseMove(object sender, MouseEventArgs e)
        {
            UpdateHoverStateFromPoint(e.GetPosition(this));
        }

        private void OnBoardMouseLeftDown(object sender, MouseButtonEventArgs e)
        {
            int n = PlayerNames.Count;
            if (n == 0 || ActualWidth <= 10) return;
            Point pt = e.GetPosition(this);
            UpdateHoverStateFromPoint(pt);

            if (HoverPlayer >= 0)
            {
                if (PlayerClicked != null) PlayerClicked(HoverPlayer);
                e.Handled = true;
                return;
            }

            if (AnimatingPlayer < 0 && HoverAddCol >= 0 && HoverDeleteBridgeIdx < 0)
            {
                if (BridgeAddRequested != null) BridgeAddRequested(HoverAddCol, HoverAddNormY);
                UpdateHoverStateFromPoint(pt);
                e.Handled = true;
            }
        }

        private void OnBoardMouseRightDown(object sender, MouseButtonEventArgs e)
        {
            int n = PlayerNames.Count;
            if (n < 2 || ActualWidth <= 10 || AnimatingPlayer >= 0) return;
            Point pt = e.GetPosition(this);
            UpdateHoverStateFromPoint(pt);

            if (HoverDeleteBridgeIdx >= 0)
            {
                int delIdx = HoverDeleteBridgeIdx;
                HoverDeleteBridgeIdx = -1;
                if (BridgeDeleteRequested != null) BridgeDeleteRequested(delIdx);
                UpdateHoverStateFromPoint(pt);
                e.Handled = true;
            }
        }

        public static bool IsSpecialResultText(string txt)
        {
            if (string.IsNullOrEmpty(txt)) return false;
            string t = txt.Trim();
            if (t == "꽝" || t == "통과" || t == "패스" || t == "-" || t == "X" || t == "x" || t == "잘 마실게요" || t == "면제")
                return false;
            return true;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 20 || h <= 80) return;

            Rect bgRect = new Rect(0, 0, w, h);
            dc.DrawRoundedRectangle(MainWindow.BrushFromHex("#0B1120"), new Pen(MainWindow.BrushFromHex("#1E293B"), 1), bgRect, 10, 10);

            int n = PlayerNames.Count;
            if (n < 2) return;

            double topY = 48.0;
            double botY = h - 48.0;
            double railH = Math.Max(40.0, botY - topY);

            Pen railPen = new Pen(MainWindow.BrushFromHex("#334155"), 3.0);
            railPen.StartLineCap = PenLineCap.Round;
            railPen.EndLineCap = PenLineCap.Round;

            // 1. Base vertical rails
            for (int c = 0; c < n; c++)
            {
                double x = GetColX(c, n, w);
                dc.DrawLine(railPen, new Point(x, topY), new Point(x, botY));
            }

            // Curtain conceals auto-generated bridges ONLY before the first player starts!
            bool showCurtain = HideBridges && !CurtainRevealed;

            // 2. Mystery Curtain backdrop + guide rails (if active before departure)
            if (showCurtain)
            {
                Rect curtainRect = new Rect(18, topY + 12, Math.Max(20, w - 36), Math.Max(24, railH - 24));
                dc.DrawRoundedRectangle(
                    MainWindow.BrushFromHex("#111E3B"),
                    new Pen(MainWindow.BrushFromHex("#2563EB"), 1.4),
                    curtainRect, 10, 10);

                // Draw vertical guide rails across the curtain so players can see columns & add custom lines!
                Pen curtainRailPen = new Pen(MainWindow.BrushFromHex("#29467E"), 2.4);
                curtainRailPen.DashStyle = new DashStyle(new double[] { 3.0, 2.5 }, 0);
                for (int c = 0; c < n; c++)
                {
                    double x = GetColX(c, n, w);
                    dc.DrawLine(curtainRailPen, new Point(x, curtainRect.Top + 2), new Point(x, curtainRect.Bottom - 2));
                }

                FormattedText cft = MakeClippedText(
                    "❓ 가림막 ON — 출발 전 세로줄 사이 클릭으로 선 추가 가능 (첫 출발 시 공개)",
                    11.5, FontWeights.SemiBold, MainWindow.BrushFromHex("#BFDBFE"), Math.Max(40, curtainRect.Width - 28));
                Rect badgeRect = new Rect(
                    curtainRect.Left + (curtainRect.Width - (cft.Width + 20)) * 0.5,
                    curtainRect.Top + 8,
                    cft.Width + 20,
                    23);
                dc.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromArgb(215, 23, 37, 84)),
                    new Pen(MainWindow.BrushFromHex("#3B82F6"), 1.0),
                    badgeRect, 11, 11);
                dc.DrawText(cft, new Point(
                    badgeRect.Left + (badgeRect.Width - cft.Width) * 0.5,
                    badgeRect.Top + (badgeRect.Height - cft.Height) * 0.5));
            }

            // 3. Horizontal bridges (auto bridges hidden under curtain; custom bridges always visible!)
            if (BridgeList != null)
            {
                Pen autoPen = new Pen(MainWindow.BrushFromHex("#475569"), 3.0);
                Pen customPen = new Pen(MainWindow.BrushFromHex("#38BDF8"), 3.2);
                Pen delHoverPen = new Pen(MainWindow.BrushFromHex("#F87171"), 4.0);
                Brush customDotBrush = MainWindow.BrushFromHex("#38BDF8");
                Brush delDotBrush = MainWindow.BrushFromHex("#F87171");

                for (int i = 0; i < BridgeList.Count; i++)
                {
                    LadderBridge b = BridgeList[i];
                    if (b.LeftCol < 0 || b.LeftCol >= n - 1) continue;
                    if (showCurtain && !b.IsCustom) continue;

                    double x1 = GetColX(b.LeftCol, n, w);
                    double x2 = GetColX(b.LeftCol + 1, n, w);
                    double y = topY + railH * b.NormY;
                    bool isDelHover = (i == HoverDeleteBridgeIdx && AnimatingPlayer < 0);

                    if (isDelHover)
                    {
                        dc.DrawLine(delHoverPen, new Point(x1, y), new Point(x2, y));
                        dc.DrawEllipse(delDotBrush, null, new Point(x1, y), 3.6, 3.6);
                        dc.DrawEllipse(delDotBrush, null, new Point(x2, y), 3.6, 3.6);
                    }
                    else if (b.IsCustom)
                    {
                        dc.DrawLine(customPen, new Point(x1, y), new Point(x2, y));
                        dc.DrawEllipse(customDotBrush, null, new Point(x1, y), 3.0, 3.0);
                        dc.DrawEllipse(customDotBrush, null, new Point(x2, y), 3.0, 3.0);
                    }
                    else
                    {
                        dc.DrawLine(autoPen, new Point(x1, y), new Point(x2, y));
                    }
                }
            }

            // 4. Completed player paths (dimmed unless highlighted)
            for (int p = 0; p < n; p++)
            {
                if (p >= CompletedPlayers.Length || !CompletedPlayers[p]) continue;
                if (p == HighlightPlayer) continue;
                Color pCol = MainWindow.SlicePalette[p % MainWindow.SlicePalette.Length];
                Color dimCol = Color.FromArgb(115, pCol.R, pCol.G, pCol.B);
                DrawPlayerPolyline(dc, p, 1.0, new SolidColorBrush(dimCol), 3.0, topY, railH, n, w, false);
            }

            // 5. Highlighted completed path on top
            if (HighlightPlayer >= 0 && HighlightPlayer < CompletedPlayers.Length && CompletedPlayers[HighlightPlayer] && AnimatingPlayer < 0)
            {
                Color hCol = MainWindow.SlicePalette[HighlightPlayer % MainWindow.SlicePalette.Length];
                DrawPlayerPolyline(dc, HighlightPlayer, 1.0, new SolidColorBrush(hCol), 5.0, topY, railH, n, w, true);
            }

            // 6. Active animating path
            if (AnimatingPlayer >= 0 && AnimatingPlayer < n)
            {
                Color aCol = MainWindow.SlicePalette[AnimatingPlayer % MainWindow.SlicePalette.Length];
                DrawPlayerPolyline(dc, AnimatingPlayer, AnimProgress, new SolidColorBrush(aCol), 5.2, topY, railH, n, w, true);
            }

            // 7. Interactive Hover Preview (add line ghost OR right-click delete tooltip)
            if (AnimatingPlayer < 0)
            {
                if (HoverAddCol >= 0 && HoverAddCol < n - 1)
                {
                    double x1 = GetColX(HoverAddCol, n, w);
                    double x2 = GetColX(HoverAddCol + 1, n, w);
                    double py = topY + railH * HoverAddNormY;

                    Pen ghostPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 56, 189, 248)), 2.6);
                    ghostPen.DashStyle = new DashStyle(new double[] { 3.0, 2.0 }, 0);
                    dc.DrawLine(ghostPen, new Point(x1, py), new Point(x2, py));

                    Point mid = new Point((x1 + x2) * 0.5, py);
                    dc.DrawEllipse(MainWindow.BrushFromHex("#0284C7"), new Pen(MainWindow.BrushFromHex("#BAE6FD"), 1.2), mid, 8.0, 8.0);
                    FormattedText plusFt = MakeText("+", 11.5, FontWeights.Bold, Brushes.White);
                    dc.DrawText(plusFt, new Point(mid.X - plusFt.Width * 0.5, mid.Y - plusFt.Height * 0.5 - 0.5));
                }
                else if (HoverDeleteBridgeIdx >= 0 && BridgeList != null && HoverDeleteBridgeIdx < BridgeList.Count)
                {
                    LadderBridge hb = BridgeList[HoverDeleteBridgeIdx];
                    double x1 = GetColX(hb.LeftCol, n, w);
                    double x2 = GetColX(hb.LeftCol + 1, n, w);
                    double py = topY + railH * hb.NormY;

                    FormattedText delFt = MakeText("우클릭: 삭제", 10.5, FontWeights.Bold, MainWindow.BrushFromHex("#FECACA"));
                    double tagW = delFt.Width + 12;
                    double tagH = delFt.Height + 4;
                    double tagX = Math.Max(6, Math.Min(w - tagW - 6, (x1 + x2 - tagW) * 0.5));
                    double tagY = Math.Max(topY + 4, py - tagH - 6);
                    Rect tagRect = new Rect(tagX, tagY, tagW, tagH);
                    dc.DrawRoundedRectangle(
                        new SolidColorBrush(Color.FromArgb(230, 127, 29, 29)),
                        new Pen(MainWindow.BrushFromHex("#F87171"), 1.0),
                        tagRect, 5, 5);
                    dc.DrawText(delFt, new Point(tagRect.Left + 6, tagRect.Top + 2));
                }
            }

            // Track which bottom slots have been reached
            int[] reachedByPlayer = new int[n];
            for (int i = 0; i < n; i++) reachedByPlayer[i] = -1;
            for (int p = 0; p < n; p++)
            {
                if (p < CompletedPlayers.Length && CompletedPlayers[p] && p < EndColumnByPlayer.Length)
                {
                    int dest = EndColumnByPlayer[p];
                    if (dest >= 0 && dest < n) reachedByPlayer[dest] = p;
                }
            }

            // 7. Top Player Pills (Clickable)
            double fontSize = (n >= 11) ? 10.5 : (n >= 8 ? 11.5 : 12.5);
            for (int i = 0; i < n; i++)
            {
                Rect r = GetTopPillRect(i, n, w);
                Color pCol = MainWindow.SlicePalette[i % MainWindow.SlicePalette.Length];
                bool done = (i < CompletedPlayers.Length && CompletedPlayers[i]);
                bool isHot = (i == HoverPlayer || i == AnimatingPlayer || i == HighlightPlayer);

                Brush fillBrush = isHot
                    ? (Brush)new SolidColorBrush(pCol)
                    : (done ? (Brush)new SolidColorBrush(Color.FromArgb(65, pCol.R, pCol.G, pCol.B)) : MainWindow.BrushFromHex("#1E293B"));
                Pen borderPen = new Pen(new SolidColorBrush(pCol), isHot ? 2.2 : 1.4);

                dc.DrawRoundedRectangle(fillBrush, borderPen, r, 7, 7);

                string label = (i < PlayerNames.Count && !string.IsNullOrEmpty(PlayerNames[i]))
                    ? PlayerNames[i]
                    : string.Format("{0}번", i + 1);
                FormattedText ft = MakeClippedText(label, fontSize, FontWeights.Bold, Brushes.White, r.Width - 8);
                dc.DrawText(ft, new Point(r.Left + (r.Width - ft.Width) * 0.5, r.Top + (r.Height - ft.Height) * 0.5));
            }

            // 8. Bottom Result Pills
            for (int c = 0; c < n; c++)
            {
                Rect r = GetBottomPillRect(c, n, w, h);
                int ownerPlayer = reachedByPlayer[c];
                string resText = (c < BottomResults.Count) ? BottomResults[c] : "";
                bool isSpecial = IsSpecialResultText(resText);

                Brush bg;
                Pen pen;
                Brush fg;

                if (ownerPlayer >= 0)
                {
                    Color pCol = MainWindow.SlicePalette[ownerPlayer % MainWindow.SlicePalette.Length];
                    bg = isSpecial ? MainWindow.BrushFromHex("#064E3B") : MainWindow.BrushFromHex("#1E293B");
                    pen = new Pen(new SolidColorBrush(pCol), 2.2);
                    fg = isSpecial ? MainWindow.BrushFromHex("#6EE7B7") : Brushes.White;
                }
                else
                {
                    bg = isSpecial ? MainWindow.BrushFromHex("#1E1B4B") : MainWindow.BrushFromHex("#0F172A");
                    pen = new Pen(MainWindow.BrushFromHex(isSpecial ? "#F59E0B" : "#334155"), 1.3);
                    fg = isSpecial ? MainWindow.BrushFromHex("#FDE047") : MainWindow.BrushFromHex("#94A3B8");
                }

                dc.DrawRoundedRectangle(bg, pen, r, 7, 7);
                FormattedText ft = MakeClippedText(
                    string.IsNullOrEmpty(resText) ? "-" : resText,
                    fontSize,
                    isSpecial ? FontWeights.Bold : FontWeights.SemiBold,
                    fg,
                    r.Width - 8);
                dc.DrawText(ft, new Point(r.Left + (r.Width - ft.Width) * 0.5, r.Top + (r.Height - ft.Height) * 0.5));
            }
        }

        private void DrawPlayerPolyline(
            DrawingContext dc,
            int playerIdx,
            double normProgress,
            Brush strokeBrush,
            double thickness,
            double topY,
            double railH,
            int colCount,
            double w,
            bool drawHeadDot)
        {
            if (PathsByPlayer == null || playerIdx < 0 || playerIdx >= PathsByPlayer.Length) return;
            List<LadderPathPoint> pts = PathsByPlayer[playerIdx];
            if (pts == null || pts.Count < 2) return;

            List<Point> screenPts = new List<Point>(pts.Count);
            double totalLen = 0.0;
            for (int i = 0; i < pts.Count; i++)
            {
                Point sp = new Point(GetColX(pts[i].Col, colCount, w), topY + railH * pts[i].NormY);
                screenPts.Add(sp);
                if (i > 0)
                {
                    double dx = sp.X - screenPts[i - 1].X;
                    double dy = sp.Y - screenPts[i - 1].Y;
                    totalLen += Math.Sqrt(dx * dx + dy * dy);
                }
            }

            double targetDist = totalLen * Math.Max(0.0, Math.Min(1.0, normProgress));
            Pen pen = new Pen(strokeBrush, thickness);
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
            pen.LineJoin = PenLineJoin.Round;

            double acc = 0.0;
            Point headPt = screenPts[0];

            for (int i = 1; i < screenPts.Count; i++)
            {
                Point a = screenPts[i - 1];
                Point b = screenPts[i];
                double seg = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                if (seg <= 0.001) continue;

                if (acc + seg <= targetDist)
                {
                    dc.DrawLine(pen, a, b);
                    headPt = b;
                    acc += seg;
                }
                else
                {
                    double rem = targetDist - acc;
                    double ratio = rem / seg;
                    headPt = new Point(a.X + (b.X - a.X) * ratio, a.Y + (b.Y - a.Y) * ratio);
                    dc.DrawLine(pen, a, headPt);
                    break;
                }
            }

            if (drawHeadDot)
            {
                dc.DrawEllipse(Brushes.White, pen, headPt, 6.0, 6.0);
            }
        }

        private FormattedText MakeText(string text, double size, FontWeight weight, Brush fg)
        {
            return new FormattedText(
                text ?? "",
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Malgun Gothic"), FontStyles.Normal, weight, FontStretches.Normal),
                size,
                fg);
        }

        private FormattedText MakeClippedText(string text, double size, FontWeight weight, Brush fg, double maxWidth)
        {
            string s = text ?? "";
            FormattedText ft = MakeText(s, size, weight, fg);
            if (ft.Width <= maxWidth || s.Length <= 2) return ft;

            while (s.Length > 1)
            {
                s = s.Substring(0, s.Length - 1);
                ft = MakeText(s + "…", size, weight, fg);
                if (ft.Width <= maxWidth) return ft;
            }
            return ft;
        }
    }

    // =========================================================================
    // Tab 1: 사다리 게임 (Ladder Game View)
    // =========================================================================
    public class LadderTabView : Grid
    {
        private readonly MainWindow _host;

        private int _playerCount = 4;
        private int _rowCount = 12;
        private bool _denseMode = false;
        private readonly List<string> _players = new List<string>();
        private readonly List<string> _results = new List<string>();

        private readonly List<LadderBridge> _bridgeList = new List<LadderBridge>();
        private List<LadderPathPoint>[] _paths;
        private int[] _endCols;
        private bool[] _completed;

        // Animation state
        private DispatcherTimer _animTimer;
        private Stopwatch _animSw;
        private int _animPlayer = -1;
        private int _lastAnimSegmentTick = -1;
        private bool _autoSequenceRunning = false;

        // UI Controls
        private TextBlock _countLabel;
        private UniformGrid _playerInputsGrid;
        private UniformGrid _resultInputsGrid;
        private LadderBoardControl _board;
        private Button _curtainBtn;
        private Button _densityBtn;
        private Button _nextStartBtn;
        private Button _autoSeqBtn;
        private StackPanel _matchResultStack;
        private TextBlock _statusBannerText;
        private TextBlock _copyFeedbackText;
        private bool _suppressEntryEvents = false;

        public LadderTabView(MainWindow host)
        {
            _host = host;

            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.90, GridUnitType.Star), MinWidth = 290 });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.36, GridUnitType.Star), MinWidth = 395 });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.74, GridUnitType.Star), MinWidth = 250 });

            InitDefaultLadderData();

            UIElement left = BuildLeftSetupCard();
            SetColumn(left, 0);
            Children.Add(left);

            UIElement center = BuildCenterBoardCard();
            SetColumn(center, 2);
            Children.Add(center);

            UIElement right = BuildRightResultCard();
            SetColumn(right, 4);
            Children.Add(right);

            _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _animTimer.Tick += OnAnimTick;

            RebuildEntryRowsUI();
            GenerateNewLadder();
        }

        private void InitDefaultLadderData()
        {
            _playerCount = 4;
            _players.Clear();
            _results.Clear();
            for (int i = 0; i < _playerCount; i++)
            {
                _players.Add(string.Format("참가자 {0}", i + 1));
                _results.Add(i == 0 ? "🎉 당첨!" : "꽝");
            }
            ShuffleResultsInternal();
        }

        private UIElement BuildLeftSetupCard()
        {
            Border card = _host.MakeCardBorder();
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Title
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Player count stepper
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Separated Top/Bottom cards

            TextBlock title = new TextBlock
            {
                Text = "사다리 구성 설정",
                FontSize = 15.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 10)
            };
            SetRow(title, 0);
            g.Children.Add(title);

            // Stepper box
            Border countBox = new Border
            {
                Background = MainWindow.BrushFromHex("#0F172A"),
                BorderBrush = MainWindow.BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 10)
            };
            DockPanel countDock = new DockPanel();
            TextBlock countCap = new TextBlock
            {
                Text = "사다리 줄 수 / 인원 (2~14명)",
                FontSize = 12.5,
                Foreground = MainWindow.BrushFromHex("#CBD5E1"),
                VerticalAlignment = VerticalAlignment.Center
            };
            StackPanel stepBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button minusBtn = _host.MakeSmallButton("−", "#334155", "#FFFFFF");
            minusBtn.Width = 30;
            minusBtn.Click += (s, e) => ChangePlayerCount(-1);

            _countLabel = new TextBlock
            {
                Text = "4명",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = MainWindow.BrushFromHex("#38BDF8"),
                Width = 48,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Button plusBtn = _host.MakeSmallButton("+", "#2563EB", "#FFFFFF");
            plusBtn.Width = 30;
            plusBtn.Click += (s, e) => ChangePlayerCount(1);

            stepBtns.Children.Add(minusBtn);
            stepBtns.Children.Add(_countLabel);
            stepBtns.Children.Add(plusBtn);
            DockPanel.SetDock(stepBtns, Dock.Right);
            countDock.Children.Add(stepBtns);
            countDock.Children.Add(countCap);
            countBox.Child = countDock;
            SetRow(countBox, 1);
            g.Children.Add(countBox);

            // Scrollable separated sections: [Section A: 상단 출발 참가자] -> [사다리 무작위 연결 구분선] -> [Section B: 하단 도착 항목]
            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            StackPanel mainStack = new StackPanel();

            // --- Section A: Top Starting Participants ---
            Border topBox = new Border
            {
                Background = MainWindow.BrushFromHex("#0F172A"),
                BorderBrush = MainWindow.BrushFromHex("#1E3A8A"),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 9, 10, 9)
            };
            StackPanel topSp = new StackPanel();

            DockPanel topHdr = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            Button bulkPlayersBtn = _host.MakeSmallButton("이름 일괄 입력", "#1E293B", "#93C5FD");
            bulkPlayersBtn.Height = 24;
            bulkPlayersBtn.FontSize = 11;
            bulkPlayersBtn.Click += (s, e) => OpenBulkPlayersOnlyDialog();
            DockPanel.SetDock(bulkPlayersBtn, Dock.Right);
            topHdr.Children.Add(bulkPlayersBtn);

            topHdr.Children.Add(new TextBlock
            {
                Text = "👤 상단 출발 참가자",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = MainWindow.BrushFromHex("#93C5FD"),
                VerticalAlignment = VerticalAlignment.Center
            });
            topSp.Children.Add(topHdr);

            _playerInputsGrid = new UniformGrid { Columns = 2 };
            topSp.Children.Add(_playerInputsGrid);
            topBox.Child = topSp;
            mainStack.Children.Add(topBox);

            // --- Visual Non-1:1 Connector Divider ---
            Grid bridgeDiv = new Grid { Margin = new Thickness(4, 9, 4, 9) };
            bridgeDiv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bridgeDiv.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bridgeDiv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border lineL = new Border { Height = 1, Background = MainWindow.BrushFromHex("#334155"), VerticalAlignment = VerticalAlignment.Center };
            SetColumn(lineL, 0);
            bridgeDiv.Children.Add(lineL);

            Border midBadge = new Border
            {
                Background = MainWindow.BrushFromHex("#172554"),
                BorderBrush = MainWindow.BrushFromHex("#2563EB"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 2, 10, 2),
                Margin = new Thickness(8, 0, 8, 0),
                Child = new TextBlock
                {
                    Text = "▼ 사다리 다리로 무작위 연결 ▼",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = MainWindow.BrushFromHex("#93C5FD")
                }
            };
            SetColumn(midBadge, 1);
            bridgeDiv.Children.Add(midBadge);

            Border lineR = new Border { Height = 1, Background = MainWindow.BrushFromHex("#334155"), VerticalAlignment = VerticalAlignment.Center };
            SetColumn(lineR, 2);
            bridgeDiv.Children.Add(lineR);
            mainStack.Children.Add(bridgeDiv);

            // --- Section B: Bottom Arrival Slots ---
            Border botBox = new Border
            {
                Background = MainWindow.BrushFromHex("#0F172A"),
                BorderBrush = MainWindow.BrushFromHex("#065F46"),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 9, 10, 9)
            };
            StackPanel botSp = new StackPanel();

            DockPanel botHdr = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            StackPanel botTools = new StackPanel { Orientation = Orientation.Horizontal };

            Button shufBtn = _host.MakeSmallButton("🔀 위치 섞기", "#1E293B", "#FDE68A");
            shufBtn.Height = 24;
            shufBtn.FontSize = 11;
            shufBtn.Margin = new Thickness(0, 0, 4, 0);
            shufBtn.Click += (s, e) =>
            {
                ShuffleResultsInternal();
                RebuildEntryRowsUI();
                SyncBoardLabelsOnly();
                _host.FocusMainWindowForShortcuts();
            };
            botTools.Children.Add(shufBtn);

            Button bulkResBtn = _host.MakeSmallButton("일괄 편집", "#1E293B", "#A7F3D0");
            bulkResBtn.Height = 24;
            bulkResBtn.FontSize = 11;
            bulkResBtn.Click += (s, e) => OpenBulkResultsOnlyDialog();
            botTools.Children.Add(bulkResBtn);

            DockPanel.SetDock(botTools, Dock.Right);
            botHdr.Children.Add(botTools);

            botHdr.Children.Add(new TextBlock
            {
                Text = "🎁 하단 도착 칸",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = MainWindow.BrushFromHex("#6EE7B7"),
                VerticalAlignment = VerticalAlignment.Center
            });
            botSp.Children.Add(botHdr);

            // Quick result preset chips inside Section B
            WrapPanel presetWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            Button pWin1 = _host.MakeSmallButton("🎉 당첨 1명", "#065F46", "#A7F3D0");
            pWin1.Margin = new Thickness(0, 0, 4, 4);
            pWin1.Click += (s, e) => ApplyResultPreset("🎉 당첨!", "꽝", 1);

            Button pPen1 = _host.MakeSmallButton("💣 벌칙 1명", "#7F1D1D", "#FECACA");
            pPen1.Margin = new Thickness(0, 0, 4, 4);
            pPen1.Click += (s, e) => ApplyResultPreset("💣 벌칙!", "통과", 1);

            Button pCoffee = _host.MakeSmallButton("☕ 커피 쏘기", "#78350F", "#FDE68A");
            pCoffee.Margin = new Thickness(0, 0, 4, 4);
            pCoffee.Click += (s, e) => ApplyResultPreset("☕ 커피 쏘기", "잘 마실게요", 1);

            Button pRank = _host.MakeSmallButton("🏆 1~N등", "#312E81", "#C7D2FE");
            pRank.Margin = new Thickness(0, 0, 0, 4);
            pRank.Click += (s, e) => ApplyRankPreset();

            presetWrap.Children.Add(pWin1);
            presetWrap.Children.Add(pPen1);
            presetWrap.Children.Add(pCoffee);
            presetWrap.Children.Add(pRank);
            botSp.Children.Add(presetWrap);

            _resultInputsGrid = new UniformGrid { Columns = 2 };
            botSp.Children.Add(_resultInputsGrid);
            botBox.Child = botSp;
            mainStack.Children.Add(botBox);

            sv.Content = mainStack;
            SetRow(sv, 2);
            g.Children.Add(sv);

            card.Child = g;
            return card;
        }

        private UIElement BuildCenterBoardCard()
        {
            Border card = _host.MakeCardBorder();
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Top bar
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Status banner
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Ladder canvas
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Bottom buttons

            DockPanel topBar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            TextBlock title = new TextBlock
            {
                Text = "┣┫ 사다리 보드",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };

            StackPanel optBtns = new StackPanel { Orientation = Orientation.Horizontal };
            // Default ON!
            _curtainBtn = _host.MakeSmallButton("다리 가리기: ON", "#2563EB", "#FFFFFF");
            _curtainBtn.Margin = new Thickness(0, 0, 6, 0);
            _curtainBtn.Click += (s, e) =>
            {
                _board.HideBridges = !_board.HideBridges;
                _curtainBtn.Content = _board.HideBridges ? "다리 가리기: ON" : "다리 가리기: OFF";
                _curtainBtn.Background = MainWindow.BrushFromHex(_board.HideBridges ? "#2563EB" : "#334155");
                _curtainBtn.Foreground = MainWindow.BrushFromHex(_board.HideBridges ? "#FFFFFF" : "#E2E8F0");
                _board.InvalidateVisual();
                _host.FocusMainWindowForShortcuts();
            };

            _densityBtn = _host.MakeSmallButton("다리 수: 보통", "#334155", "#E2E8F0");
            _densityBtn.Margin = new Thickness(0, 0, 6, 0);
            _densityBtn.Click += (s, e) =>
            {
                _denseMode = !_denseMode;
                _rowCount = _denseMode ? 18 : 12;
                _densityBtn.Content = _denseMode ? "다리 수: 많음" : "다리 수: 보통";
                GenerateNewLadder();
                _host.FocusMainWindowForShortcuts();
            };

            Button regenTopBtn = _host.MakeSmallButton("🔄 사다리 재구성", "#0F172A", "#38BDF8");
            regenTopBtn.Click += (s, e) =>
            {
                GenerateNewLadder();
                _host.FocusMainWindowForShortcuts();
            };

            optBtns.Children.Add(_curtainBtn);
            optBtns.Children.Add(_densityBtn);
            optBtns.Children.Add(regenTopBtn);
            DockPanel.SetDock(optBtns, Dock.Right);
            topBar.Children.Add(optBtns);
            topBar.Children.Add(title);
            SetRow(topBar, 0);
            g.Children.Add(topBar);

            Border statusBox = new Border
            {
                Background = MainWindow.BrushFromHex("#0F172A"),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 8)
            };
            _statusBannerText = new TextBlock
            {
                Text = "세로줄 사이 클릭: 선 추가 · 우클릭: 삭제 | 상단 이름 클릭 또는 Space로 출발!",
                FontSize = 12.5,
                Foreground = MainWindow.BrushFromHex("#93C5FD"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            statusBox.Child = _statusBannerText;
            SetRow(statusBox, 1);
            g.Children.Add(statusBox);

            _board = new LadderBoardControl();
            _board.HideBridges = true;
            _board.CurtainRevealed = false;
            _board.PlayerClicked += OnBoardPlayerClicked;
            _board.BridgeAddRequested += OnBoardBridgeAddRequested;
            _board.BridgeDeleteRequested += OnBoardBridgeDeleteRequested;
            SetRow(_board, 2);
            g.Children.Add(_board);

            Grid botGrid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            botGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            botGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            botGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
            botGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            botGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.95, GridUnitType.Star) });

            _nextStartBtn = _host.MakeStyledButton("▶ 다음 참가자 출발 (Space)", "#059669", "#FFFFFF", 13.5, 42);
            _nextStartBtn.Click += (s, e) => HandleSpaceKey();
            SetColumn(_nextStartBtn, 0);
            botGrid.Children.Add(_nextStartBtn);

            _autoSeqBtn = _host.MakeStyledButton("⚡ 순서대로 자동 출발", "#2563EB", "#FFFFFF", 13, 42);
            _autoSeqBtn.Click += (s, e) => ToggleAutoSequence();
            SetColumn(_autoSeqBtn, 2);
            botGrid.Children.Add(_autoSeqBtn);

            Button revealAllBtn = _host.MakeStyledButton("⏩ 전체 결과 보기", "#475569", "#FFFFFF", 13, 42);
            revealAllBtn.Click += (s, e) => RevealAllImmediately();
            SetColumn(revealAllBtn, 4);
            botGrid.Children.Add(revealAllBtn);

            SetRow(botGrid, 3);
            g.Children.Add(botGrid);

            card.Child = g;
            return card;
        }

        private UIElement BuildRightResultCard()
        {
            Border card = _host.MakeCardBorder();
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            DockPanel hdr = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            TextBlock title = new TextBlock
            {
                Text = "사다리 매칭 결과",
                FontSize = 15.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            Button clearTraceBtn = _host.MakeSmallButton("경로 지우기", "#334155", "#CBD5E1");
            clearTraceBtn.Click += (s, e) =>
            {
                ResetTracesOnly();
                _host.FocusMainWindowForShortcuts();
            };
            DockPanel.SetDock(clearTraceBtn, Dock.Right);
            hdr.Children.Add(clearTraceBtn);
            hdr.Children.Add(title);
            SetRow(hdr, 0);
            g.Children.Add(hdr);

            ScrollViewer sv = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = MainWindow.BrushFromHex("#0B1120"),
                Padding = new Thickness(8)
            };
            _matchResultStack = new StackPanel();
            sv.Content = _matchResultStack;

            Border listWrap = new Border
            {
                Background = MainWindow.BrushFromHex("#0B1120"),
                BorderBrush = MainWindow.BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = sv
            };
            SetRow(listWrap, 1);
            g.Children.Add(listWrap);

            StackPanel footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            _copyFeedbackText = new TextBlock
            {
                Text = "",
                FontSize = 11.5,
                Foreground = MainWindow.BrushFromHex("#34D399"),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 0, 4)
            };
            footer.Children.Add(_copyFeedbackText);

            Button copyBtn = _host.MakeStyledButton("📋 사다리 결과 복사하기", "#334155", "#34D399", 13, 38);
            copyBtn.Click += (s, e) => CopyResultsToClipboard();
            footer.Children.Add(copyBtn);
            SetRow(footer, 2);
            g.Children.Add(footer);

            card.Child = g;
            return card;
        }

        private void ChangePlayerCount(int delta)
        {
            int next = _playerCount + delta;
            if (next < 2 || next > 14) return;
            _playerCount = next;
            _countLabel.Text = string.Format("{0}명", _playerCount);

            while (_players.Count < _playerCount)
                _players.Add(string.Format("참가자 {0}", _players.Count + 1));
            while (_players.Count > _playerCount)
                _players.RemoveAt(_players.Count - 1);

            while (_results.Count < _playerCount)
                _results.Add("꽝");
            while (_results.Count > _playerCount)
                _results.RemoveAt(_results.Count - 1);

            RebuildEntryRowsUI();
            GenerateNewLadder();
            _host.FocusMainWindowForShortcuts();
        }

        private void ApplyResultPreset(string hitLabel, string missLabel, int hitCount)
        {
            _results.Clear();
            for (int i = 0; i < _playerCount; i++)
            {
                _results.Add(i < hitCount ? hitLabel : missLabel);
            }
            ShuffleResultsInternal();
            RebuildEntryRowsUI();
            GenerateNewLadder();
            _host.FocusMainWindowForShortcuts();
        }

        private void ApplyRankPreset()
        {
            _results.Clear();
            for (int i = 0; i < _playerCount; i++)
            {
                _results.Add(string.Format("{0}등", i + 1));
            }
            ShuffleResultsInternal();
            RebuildEntryRowsUI();
            GenerateNewLadder();
            _host.FocusMainWindowForShortcuts();
        }

        private void ShuffleResultsInternal()
        {
            for (int i = _results.Count - 1; i > 0; i--)
            {
                int j = _host.NextFairRandomInt(i + 1);
                string tmp = _results[i];
                _results[i] = _results[j];
                _results[j] = tmp;
            }
        }

        private void OpenBulkPlayersOnlyDialog()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < _playerCount; i++) sb.AppendLine(_players[i]);

            string res = _host.ShowMultilineDialog(
                "상단 출발 참가자 일괄 입력",
                "한 줄에 한 명씩(또는 쉼표 구분) 참가자 이름을 입력하세요 (2~14명).\n인원 수가 바뀌면 하단 도착 칸 개수도 자동으로 맞춰집니다:",
                sb.ToString());
            if (res == null) return;

            string[] tokens = res.Split(new char[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> newP = new List<string>();
            for (int i = 0; i < tokens.Length && newP.Count < 14; i++)
            {
                string t = tokens[i].Trim();
                if (!string.IsNullOrEmpty(t)) newP.Add(t);
            }

            if (newP.Count < 2)
            {
                MessageBox.Show(_host, "사다리 게임은 최소 2명 이상이어야 합니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _playerCount = newP.Count;
            _countLabel.Text = string.Format("{0}명", _playerCount);
            _players.Clear();
            _players.AddRange(newP);

            while (_results.Count < _playerCount) _results.Add("꽝");
            while (_results.Count > _playerCount) _results.RemoveAt(_results.Count - 1);

            RebuildEntryRowsUI();
            GenerateNewLadder();
        }

        private void OpenBulkResultsOnlyDialog()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < _playerCount; i++) sb.AppendLine(_results[i]);

            string res = _host.ShowMultilineDialog(
                "하단 도착 항목 일괄 편집",
                string.Format("하단 도착 칸에 들어갈 항목을 한 줄에 하나씩 입력하세요 (현재 {0}칸):", _playerCount),
                sb.ToString());
            if (res == null) return;

            string[] lines = res.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> newR = new List<string>();
            for (int i = 0; i < lines.Length && newR.Count < _playerCount; i++)
            {
                string t = lines[i].Trim();
                if (!string.IsNullOrEmpty(t)) newR.Add(t);
            }
            while (newR.Count < _playerCount) newR.Add("꽝");

            _results.Clear();
            _results.AddRange(newR);

            RebuildEntryRowsUI();
            SyncBoardLabelsOnly();
        }

        private void RebuildEntryRowsUI()
        {
            _suppressEntryEvents = true;
            _playerInputsGrid.Children.Clear();
            _resultInputsGrid.Children.Clear();

            // 1. Top starting participant cells (1..N with player color dots)
            for (int i = 0; i < _playerCount; i++)
            {
                int idx = i;
                Color col = MainWindow.SlicePalette[i % MainWindow.SlicePalette.Length];

                Grid cell = new Grid { Margin = new Thickness(i % 2 == 0 ? 0 : 4, 0, i % 2 == 0 ? 4 : 0, 5) };
                cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(23) });
                cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Border badge = new Border
                {
                    Width = 19,
                    Height = 19,
                    CornerRadius = new CornerRadius(9.5),
                    Background = new SolidColorBrush(col),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock
                    {
                        Text = (i + 1).ToString(),
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                SetColumn(badge, 0);
                cell.Children.Add(badge);

                TextBox pTb = MakeEntryTextBox(_players[i], "#1E293B");
                pTb.TextChanged += (s, e) =>
                {
                    if (_suppressEntryEvents) return;
                    _players[idx] = pTb.Text;
                    SyncBoardLabelsOnly();
                };
                SetColumn(pTb, 1);
                cell.Children.Add(pTb);

                _playerInputsGrid.Children.Add(cell);
            }

            // 2. Bottom arrival slot cells (A칸, B칸, C칸... with neutral slot tags)
            for (int i = 0; i < _playerCount; i++)
            {
                int idx = i;
                string slotLabel = string.Format("{0}칸", (char)('A' + i));

                Grid cell = new Grid { Margin = new Thickness(i % 2 == 0 ? 0 : 4, 0, i % 2 == 0 ? 4 : 0, 5) };
                cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Border slotTag = new Border
                {
                    Background = MainWindow.BrushFromHex("#1E293B"),
                    BorderBrush = MainWindow.BrushFromHex("#475569"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(3, 1, 3, 1),
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = slotLabel,
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = MainWindow.BrushFromHex("#94A3B8"),
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                };
                SetColumn(slotTag, 0);
                cell.Children.Add(slotTag);

                TextBox rTb = MakeEntryTextBox(_results[i], "#1E293B");
                rTb.TextChanged += (s, e) =>
                {
                    if (_suppressEntryEvents) return;
                    _results[idx] = rTb.Text;
                    SyncBoardLabelsOnly();
                };
                SetColumn(rTb, 1);
                cell.Children.Add(rTb);

                _resultInputsGrid.Children.Add(cell);
            }

            _suppressEntryEvents = false;
        }

        private TextBox MakeEntryTextBox(string text, string bgHex)
        {
            TextBox tb = new TextBox
            {
                Text = text ?? "",
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = MainWindow.BrushFromHex(bgHex),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                BorderBrush = MainWindow.BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 0, 6, 0),
                FontSize = 12
            };
            InputMethod.SetIsInputMethodEnabled(tb, true);
            return tb;
        }

        private void SyncBoardLabelsOnly()
        {
            _board.PlayerNames = new List<string>(_players);
            _board.BottomResults = new List<string>(_results);
            _board.InvalidateVisual();
            RefreshMatchResultListUI();
        }

        public void GenerateNewLadder()
        {
            StopAutoSequence();
            _animTimer.Stop();
            _animPlayer = -1;

            int n = _playerCount;
            int rows = _rowCount;
            bool[,] grid = new bool[rows, Math.Max(1, n - 1)];

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < n - 1; c++)
                {
                    if (c > 0 && grid[r, c - 1]) continue;
                    int roll = _host.NextFairRandomInt(100);
                    if (roll < 44)
                    {
                        grid[r, c] = true;
                    }
                }
            }

            for (int c = 0; c < n - 1; c++)
            {
                bool hasAny = false;
                for (int r = 0; r < rows; r++)
                {
                    if (grid[r, c]) { hasAny = true; break; }
                }
                if (!hasAny)
                {
                    for (int attempt = 0; attempt < 24; attempt++)
                    {
                        int rr = _host.NextFairRandomInt(rows);
                        bool leftClash = (c > 0 && grid[rr, c - 1]);
                        bool rightClash = (c + 1 < n - 1 && grid[rr, c + 1]);
                        if (!leftClash && !rightClash)
                        {
                            grid[rr, c] = true;
                            break;
                        }
                    }
                }
            }

            _bridgeList.Clear();
            for (int r = 0; r < rows; r++)
            {
                double ny = (r + 1.0) / (rows + 1.0);
                for (int c = 0; c < n - 1; c++)
                {
                    if (grid[r, c])
                    {
                        _bridgeList.Add(new LadderBridge(c, ny, false));
                    }
                }
            }

            _completed = new bool[n];
            RebuildPathsFromBridges();

            _board.PlayerNames = new List<string>(_players);
            _board.BottomResults = new List<string>(_results);
            _board.BridgeList = _bridgeList;
            _board.CompletedPlayers = _completed;
            _board.AnimatingPlayer = -1;
            _board.HighlightPlayer = -1;
            _board.HoverDeleteBridgeIdx = -1;
            _board.HoverAddCol = -1;
            _board.CurtainRevealed = false; // Curtain re-covers newly generated ladder!
            _board.InvalidateVisual();

            _copyFeedbackText.Text = "";
            _statusBannerText.Text = "새 사다리 준비 완료! 세로줄 사이 클릭으로 선 추가(우클릭 삭제) 또는 Space로 출발하세요.";
            UpdateActionButtonsUI();
            RefreshMatchResultListUI();
        }

        private void RebuildPathsFromBridges()
        {
            int n = _playerCount;
            _paths = new List<LadderPathPoint>[n];
            _endCols = new int[n];

            List<LadderBridge> sorted = new List<LadderBridge>(_bridgeList);
            sorted.Sort(delegate(LadderBridge a, LadderBridge b)
            {
                int cmp = a.NormY.CompareTo(b.NormY);
                if (cmp != 0) return cmp;
                return a.LeftCol.CompareTo(b.LeftCol);
            });

            for (int startCol = 0; startCol < n; startCol++)
            {
                List<LadderPathPoint> pts = new List<LadderPathPoint>();
                int cur = startCol;
                pts.Add(new LadderPathPoint(cur, 0.0));

                for (int i = 0; i < sorted.Count; i++)
                {
                    LadderBridge b = sorted[i];
                    if (b.LeftCol == cur)
                    {
                        pts.Add(new LadderPathPoint(cur, b.NormY));
                        cur = cur + 1;
                        pts.Add(new LadderPathPoint(cur, b.NormY));
                    }
                    else if (b.LeftCol == cur - 1)
                    {
                        pts.Add(new LadderPathPoint(cur, b.NormY));
                        cur = cur - 1;
                        pts.Add(new LadderPathPoint(cur, b.NormY));
                    }
                }

                pts.Add(new LadderPathPoint(cur, 1.0));
                _paths[startCol] = pts;
                _endCols[startCol] = cur;
            }

            if (_board != null)
            {
                _board.PathsByPlayer = _paths;
                _board.EndColumnByPlayer = _endCols;
            }
        }

        private bool HasNearBridgeCollision(int leftCol, double candidateY)
        {
            for (int i = 0; i < _bridgeList.Count; i++)
            {
                LadderBridge b = _bridgeList[i];
                if (Math.Abs(b.LeftCol - leftCol) <= 1 && Math.Abs(b.NormY - candidateY) < 0.0055)
                {
                    return true;
                }
            }
            return false;
        }

        private void OnBoardBridgeAddRequested(int leftCol, double rawNormY)
        {
            _host.FocusMainWindowForShortcuts();
            if (_animPlayer >= 0 || leftCol < 0 || leftCol >= _playerCount - 1) return;

            StopAutoSequence();

            double baseY = Math.Max(0.045, Math.Min(0.955, rawNormY));
            double chosenY = -1.0;
            for (int step = 0; step <= 20; step++)
            {
                double delta = step * 0.006;
                double up = baseY - delta;
                if (up >= 0.04 && !HasNearBridgeCollision(leftCol, up))
                {
                    chosenY = up;
                    break;
                }
                double dn = baseY + delta;
                if (dn <= 0.96 && !HasNearBridgeCollision(leftCol, dn))
                {
                    chosenY = dn;
                    break;
                }
            }
            if (chosenY < 0.0) return;

            _bridgeList.Add(new LadderBridge(leftCol, chosenY, true));
            SoundEffects.PlayTick(_host.IsSoundEnabled);

            bool anyCompleted = false;
            if (_completed != null)
            {
                for (int i = 0; i < _completed.Length; i++)
                {
                    if (_completed[i]) { anyCompleted = true; _completed[i] = false; }
                }
            }
            _board.HighlightPlayer = -1;
            RebuildPathsFromBridges();
            _board.InvalidateVisual();
            UpdateActionButtonsUI();
            RefreshMatchResultListUI();

            int customCnt = 0;
            for (int i = 0; i < _bridgeList.Count; i++)
            {
                if (_bridgeList[i].IsCustom) customCnt++;
            }

            if (anyCompleted)
            {
                _statusBannerText.Text = string.Format("✏️ 가로선 추가됨 (직접 추가 {0}개) — 사다리가 변경되어 경로를 다시 출발합니다.", customCnt);
            }
            else
            {
                _statusBannerText.Text = string.Format("✏️ {0}번~{1}번 줄 사이에 가로선을 추가했습니다 (직접 추가 {2}개 · 우클릭 시 삭제)",
                    leftCol + 1, leftCol + 2, customCnt);
            }
        }

        private void OnBoardBridgeDeleteRequested(int bridgeIdx)
        {
            _host.FocusMainWindowForShortcuts();
            if (_animPlayer >= 0 || bridgeIdx < 0 || bridgeIdx >= _bridgeList.Count) return;

            bool showCurtain = _board.HideBridges && !_board.CurtainRevealed;
            if (showCurtain && !_bridgeList[bridgeIdx].IsCustom) return;

            StopAutoSequence();
            _bridgeList.RemoveAt(bridgeIdx);
            SoundEffects.PlayTick(_host.IsSoundEnabled);

            bool anyCompleted = false;
            if (_completed != null)
            {
                for (int i = 0; i < _completed.Length; i++)
                {
                    if (_completed[i]) { anyCompleted = true; _completed[i] = false; }
                }
            }
            _board.HighlightPlayer = -1;
            RebuildPathsFromBridges();
            _board.InvalidateVisual();
            UpdateActionButtonsUI();
            RefreshMatchResultListUI();

            _statusBannerText.Text = anyCompleted
                ? "🗑️ 가로선을 삭제했습니다 — 변경된 사다리로 다시 출발해 주세요."
                : "🗑️ 선택한 가로선을 삭제했습니다.";
        }

        private void ResetTracesOnly()
        {
            StopAutoSequence();
            _animTimer.Stop();
            _animPlayer = -1;
            if (_completed != null)
            {
                for (int i = 0; i < _completed.Length; i++) _completed[i] = false;
            }
            _board.AnimatingPlayer = -1;
            _board.HighlightPlayer = -1;
            _board.CurtainRevealed = false;
            _board.InvalidateVisual();
            _statusBannerText.Text = "경로를 초기화했습니다. 다시 출발할 수 있습니다.";
            UpdateActionButtonsUI();
            RefreshMatchResultListUI();
        }

        private void OnBoardPlayerClicked(int playerIdx)
        {
            _host.FocusMainWindowForShortcuts();
            if (playerIdx < 0 || playerIdx >= _playerCount) return;

            if (_animPlayer >= 0)
            {
                FinishCurrentAnimationImmediately();
            }

            if (_completed[playerIdx])
            {
                _board.HighlightPlayer = playerIdx;
                _board.InvalidateVisual();
                string destRes = _results[_endCols[playerIdx]];
                _statusBannerText.Text = string.Format("🔎 [{0}] → [{1}]", _players[playerIdx], destRes);
                return;
            }

            StartPlayerTrace(playerIdx);
        }

        public void HandleSpaceKey()
        {
            _host.FocusMainWindowForShortcuts();

            if (_animPlayer >= 0)
            {
                FinishCurrentAnimationImmediately();
                return;
            }

            int nextIdx = GetNextUncompletedPlayer();
            if (nextIdx < 0)
            {
                ResetTracesOnly();
                nextIdx = 0;
            }
            StartPlayerTrace(nextIdx);
        }

        private int GetNextUncompletedPlayer()
        {
            if (_completed == null) return -1;
            for (int i = 0; i < _playerCount && i < _completed.Length; i++)
            {
                if (!_completed[i]) return i;
            }
            return -1;
        }

        private void StartPlayerTrace(int playerIdx)
        {
            // Once the first player departs, lift the curtain permanently for this round!
            _board.CurtainRevealed = true;

            _animPlayer = playerIdx;
            _lastAnimSegmentTick = -1;
            _animSw = Stopwatch.StartNew();
            _board.AnimatingPlayer = playerIdx;
            _board.AnimProgress = 0.0;
            _board.HighlightPlayer = playerIdx;
            _statusBannerText.Text = string.Format("🏃 [{0}] 사다리 이동 중... (한 번 더 누르면 즉시 도착)", _players[playerIdx]);
            UpdateActionButtonsUI();
            _board.InvalidateVisual();
            _animTimer.Start();
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            if (_animPlayer < 0 || _animSw == null)
            {
                _animTimer.Stop();
                return;
            }

            double dur = _autoSequenceRunning ? 0.55 : 0.85;
            double p = _animSw.Elapsed.TotalSeconds / dur;
            if (p >= 1.0)
            {
                FinishCurrentAnimationImmediately();
                return;
            }

            int tickStep = (int)(p * 10.0);
            if (tickStep != _lastAnimSegmentTick)
            {
                _lastAnimSegmentTick = tickStep;
                SoundEffects.PlayTick(_host.IsSoundEnabled);
            }

            _board.AnimProgress = p;
            _board.InvalidateVisual();
        }

        private void FinishCurrentAnimationImmediately()
        {
            _animTimer.Stop();
            if (_animPlayer < 0) return;

            int doneIdx = _animPlayer;
            _animPlayer = -1;
            _completed[doneIdx] = true;
            _board.AnimatingPlayer = -1;
            _board.HighlightPlayer = doneIdx;
            // Note: _board.CurtainRevealed stays TRUE so bridges remain visible between Player 1 and Player 2!
            _board.InvalidateVisual();

            string resText = _results[_endCols[doneIdx]];
            if (LadderBoardControl.IsSpecialResultText(resText))
                SoundEffects.PlayWin(_host.IsSoundEnabled);
            else
                SoundEffects.PlayReach(_host.IsSoundEnabled);

            _statusBannerText.Text = string.Format("✔ [{0}] 도착 결과 → [{1}]", _players[doneIdx], resText);
            RefreshMatchResultListUI();
            UpdateActionButtonsUI();

            if (_autoSequenceRunning)
            {
                int next = GetNextUncompletedPlayer();
                if (next >= 0)
                {
                    StartPlayerTrace(next);
                }
                else
                {
                    StopAutoSequence();
                }
            }
        }

        private void ToggleAutoSequence()
        {
            _host.FocusMainWindowForShortcuts();
            if (_autoSequenceRunning)
            {
                StopAutoSequence();
                return;
            }

            if (GetNextUncompletedPlayer() < 0)
            {
                ResetTracesOnly();
            }

            _autoSequenceRunning = true;
            UpdateActionButtonsUI();
            if (_animPlayer < 0)
            {
                int next = GetNextUncompletedPlayer();
                if (next >= 0) StartPlayerTrace(next);
            }
        }

        public void StopAutoSequence()
        {
            _autoSequenceRunning = false;
            UpdateActionButtonsUI();
        }

        private void RevealAllImmediately()
        {
            _host.FocusMainWindowForShortcuts();
            StopAutoSequence();
            _animTimer.Stop();
            _animPlayer = -1;
            for (int i = 0; i < _playerCount; i++) _completed[i] = true;
            _board.CurtainRevealed = true;
            _board.AnimatingPlayer = -1;
            _board.HighlightPlayer = -1;
            _board.InvalidateVisual();

            SoundEffects.PlayWin(_host.IsSoundEnabled);
            _statusBannerText.Text = "✨ 모든 참가자의 사다리 결과가 공개되었습니다! (이름을 클릭하면 개별 경로가 강조됩니다)";
            RefreshMatchResultListUI();
            UpdateActionButtonsUI();
        }

        private void UpdateActionButtonsUI()
        {
            if (_nextStartBtn == null || _autoSeqBtn == null) return;
            if (_animPlayer >= 0)
            {
                _nextStartBtn.Content = "⏩ 즉시 도착 (Space)";
                _nextStartBtn.Background = MainWindow.BrushFromHex("#4F46E5");
            }
            else
            {
                int next = GetNextUncompletedPlayer();
                _nextStartBtn.Content = (next >= 0)
                    ? string.Format("▶ {0} 출발 (Space)", _players[next])
                    : "🔄 다시 출발 (Space)";
                _nextStartBtn.Background = MainWindow.BrushFromHex("#059669");
            }

            if (_autoSequenceRunning)
            {
                _autoSeqBtn.Content = "⏹ 자동 출발 중지";
                _autoSeqBtn.Background = MainWindow.BrushFromHex("#DC2626");
            }
            else
            {
                _autoSeqBtn.Content = "⚡ 순서대로 자동 출발";
                _autoSeqBtn.Background = MainWindow.BrushFromHex("#2563EB");
            }
        }

        private void RefreshMatchResultListUI()
        {
            if (_matchResultStack == null) return;
            _matchResultStack.Children.Clear();

            int shown = 0;
            for (int i = 0; i < _playerCount; i++)
            {
                if (_completed == null || i >= _completed.Length || !_completed[i]) continue;
                shown++;
                int idx = i;
                Color pCol = MainWindow.SlicePalette[i % MainWindow.SlicePalette.Length];
                string pName = _players[i];
                string rName = _results[_endCols[i]];
                bool special = LadderBoardControl.IsSpecialResultText(rName);

                Border row = new Border
                {
                    Background = MainWindow.BrushFromHex(special ? "#071F18" : "#111827"),
                    BorderBrush = MainWindow.BrushFromHex(special ? "#10B981" : "#1E293B"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(9, 7, 9, 7),
                    Margin = new Thickness(0, 0, 0, 5),
                    Cursor = Cursors.Hand
                };
                row.MouseLeftButtonDown += (s, e) =>
                {
                    _board.HighlightPlayer = idx;
                    _board.InvalidateVisual();
                };

                DockPanel dp = new DockPanel();
                Border rBadge = new Border
                {
                    Background = MainWindow.BrushFromHex(special ? "#059669" : "#334155"),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(8, 2, 8, 2),
                    Child = new TextBlock
                    {
                        Text = rName,
                        FontSize = 12,
                        FontWeight = special ? FontWeights.Bold : FontWeights.Normal,
                        Foreground = Brushes.White
                    }
                };
                DockPanel.SetDock(rBadge, Dock.Right);
                dp.Children.Add(rBadge);

                StackPanel leftSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                leftSp.Children.Add(new Ellipse
                {
                    Width = 9,
                    Height = 9,
                    Fill = new SolidColorBrush(pCol),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                leftSp.Children.Add(new TextBlock
                {
                    Text = pName,
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                });
                dp.Children.Add(leftSp);

                row.Child = dp;
                _matchResultStack.Children.Add(row);
            }

            if (shown == 0)
            {
                _matchResultStack.Children.Add(new TextBlock
                {
                    Text = "아직 도착한 참가자가 없습니다.\n상단 이름을 클릭하거나 출발 버튼을 눌러보세요.",
                    FontSize = 12,
                    Foreground = MainWindow.BrushFromHex("#64748B"),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(6, 36, 6, 20)
                });
            }
        }

        private void CopyResultsToClipboard()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[사다리 게임 결과]");
            for (int i = 0; i < _playerCount; i++)
            {
                string status = (_completed != null && i < _completed.Length && _completed[i])
                    ? _results[_endCols[i]]
                    : "(미출발)";
                sb.AppendLine(string.Format("{0}. {1} → {2}", i + 1, _players[i], status));
            }
            try
            {
                Clipboard.SetText(sb.ToString().TrimEnd());
                _copyFeedbackText.Text = "✔ 클립보드에 복사되었습니다!";
            }
            catch { }
            _host.FocusMainWindowForShortcuts();
        }
    }
}
