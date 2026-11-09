using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace StrikeRoulette
{
    public class LotTicketItem
    {
        public int SlotNumber;
        public string Text;
        public bool IsSpecial;
        public bool IsOrderNumber; // True in 순서 뽑기 mode so 2번~N번 render clearly (not like dim '꽝')
        public bool IsRevealed;
        public int PickOrder; // 0 if revealed via "Reveal All", >=1 if picked
        public string DrawerName;
    }

    // =========================================================================
    // Tab 2: 제비뽑기 (Drawing Lots View — Separated Mode Sections & Dividers)
    // =========================================================================
    public class LotsTabView : Grid
    {
        private readonly MainWindow _host;

        // 0 = 🎯 당첨·꽝 뽑기, 1 = 🔢 순서 뽑기, 2 = ✏️ 자유 문구
        private int _lotMode = 0;
        private bool _orderUseRankSuffix = false; // false = "1번~N번", true = "1등~N등"

        private int _totalCount = 6;
        private int _specialCount = 1;
        private string _specialLabel = "🎉 당첨!";
        private string _normalLabel = "꽝";
        private readonly List<LotTicketItem> _customTemplateItems = new List<LotTicketItem>();
        private readonly List<string> _drawerNames = new List<string>();

        private readonly List<LotTicketItem> _tickets = new List<LotTicketItem>();
        private int _pickedSequence = 0;
        private bool _showRemainingSpecialHint = true;

        // UI Elements - Mode & Left Panels
        private readonly List<Button> _modeButtons = new List<Button>();
        private TextBlock _totalCountCapText;
        private TextBlock _totalCountText;

        private StackPanel _winLoseConfigPanel;
        private TextBlock _specialCountText;
        private TextBox _specialLabelInput;
        private TextBox _normalLabelInput;

        private StackPanel _orderConfigPanel;
        private Button _orderNumFmtBtn;
        private Button _orderRankFmtBtn;
        private TextBlock _orderPreviewDescText;

        private StackPanel _customConfigPanel;
        private TextBlock _customSummaryText;

        private TextBlock _drawerInfoText;

        // UI Elements - Center Board & Right Log
        private TextBlock _boardStatusText;
        private Border _remainingSpecialBadge;
        private TextBlock _remainingSpecialText;
        private Button _toggleHintBtn;
        private WrapPanel _ticketsWrap;
        private Button _drawOneBtn;

        private StackPanel _logStack;
        private ScrollViewer _logScroll;
        private TextBlock _copyFeedbackText;
        private bool _suppressInputEvents = false;

        public LotsTabView(MainWindow host)
        {
            _host = host;

            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.86, GridUnitType.Star), MinWidth = 280 });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.38, GridUnitType.Star), MinWidth = 395 });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.76, GridUnitType.Star), MinWidth = 250 });

            InitDefaultCustomTemplate();

            UIElement left = BuildLeftSetupCard();
            SetColumn(left, 0);
            Children.Add(left);

            UIElement center = BuildCenterBoardCard();
            SetColumn(center, 2);
            Children.Add(center);

            UIElement right = BuildRightLogCard();
            SetColumn(right, 4);
            Children.Add(right);

            UpdateModeVisibilityUI();
            ResetAndShuffleLots();
        }

        private void InitDefaultCustomTemplate()
        {
            _customTemplateItems.Clear();
            _customTemplateItems.Add(new LotTicketItem { Text = "🥇 1등 당첨", IsSpecial = true });
            _customTemplateItems.Add(new LotTicketItem { Text = "🥈 2등 당첨", IsSpecial = true });
            _customTemplateItems.Add(new LotTicketItem { Text = "꽝", IsSpecial = false });
            _customTemplateItems.Add(new LotTicketItem { Text = "꽝", IsSpecial = false });
            _customTemplateItems.Add(new LotTicketItem { Text = "꽝", IsSpecial = false });
        }

        private UIElement BuildSectionLine()
        {
            return new Border
            {
                Height = 1,
                Background = MainWindow.BrushFromHex("#334155"),
                Margin = new Thickness(0, 12, 0, 10)
            };
        }

        private UIElement BuildSectionHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = MainWindow.BrushFromHex("#CBD5E1"),
                Margin = new Thickness(2, 0, 0, 6)
            };
        }

        private UIElement BuildLeftSetupCard()
        {
            Border card = _host.MakeCardBorder();
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock title = new TextBlock
            {
                Text = "제비 구성 설정",
                FontSize = 15.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            SetRow(title, 0);
            g.Children.Add(title);

            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            StackPanel sp = new StackPanel();

            // --- Section 1: Mode Selector ---
            sp.Children.Add(BuildSectionHeader("뽑기 방식 선택"));

            Border modeBar = new Border
            {
                Background = MainWindow.BrushFromHex("#0F172A"),
                BorderBrush = MainWindow.BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(4)
            };
            UniformGrid modeGrid = new UniformGrid { Rows = 1, Columns = 3 };
            string[] modeTitles = new string[] { "🎯 당첨·꽝", "🔢 순서 뽑기", "✏️ 직접 편집" };
            _modeButtons.Clear();
            for (int m = 0; m < 3; m++)
            {
                int capturedM = m;
                Button mb = _host.MakeSmallButton(modeTitles[m], "#0F172A", "#94A3B8");
                mb.Height = 30;
                mb.FontSize = 12;
                mb.Margin = new Thickness(m == 0 ? 0 : 2, 0, m == 2 ? 0 : 2, 0);
                mb.Click += (s, e) => SelectLotMode(capturedM);
                _modeButtons.Add(mb);
                modeGrid.Children.Add(mb);
            }
            modeBar.Child = modeGrid;
            sp.Children.Add(modeBar);

            // --- Divider + Section 2: Detailed Mode Config ---
            sp.Children.Add(BuildSectionLine());
            sp.Children.Add(BuildSectionHeader("세부 제비 설정"));

            // Common count stepper (used by Mode 0 & Mode 1)
            Border totalBox = BuildStepperBox(
                "전체 제비 수 (2~36개)",
                out _totalCountCapText,
                out _totalCountText,
                "6개",
                () => ChangeTotalCount(-1),
                () => ChangeTotalCount(1));
            sp.Children.Add(totalBox);

            // [Mode 0 Panel]: 당첨 / 벌칙 뽑기
            _winLoseConfigPanel = new StackPanel();
            TextBlock dummyCap;
            _winLoseConfigPanel.Children.Add(BuildStepperBox(
                "당첨 / 벌칙 개수",
                out dummyCap,
                out _specialCountText,
                "1개",
                () => ChangeSpecialCount(-1),
                () => ChangeSpecialCount(1)));

            _winLoseConfigPanel.Children.Add(new TextBlock
            {
                Text = "주요 제비 문구 (당첨 / 벌칙 등)",
                FontSize = 11.5,
                Foreground = MainWindow.BrushFromHex("#94A3B8"),
                Margin = new Thickness(2, 2, 0, 4)
            });

            _specialLabelInput = new TextBox
            {
                Text = _specialLabel,
                Height = 30,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = MainWindow.BrushFromHex("#0F172A"),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                BorderBrush = MainWindow.BrushFromHex("#475569"),
                Padding = new Thickness(8, 0, 8, 0),
                FontSize = 12.5,
                Margin = new Thickness(0, 0, 0, 6)
            };
            InputMethod.SetIsInputMethodEnabled(_specialLabelInput, true);
            _specialLabelInput.TextChanged += (s, e) =>
            {
                if (_suppressInputEvents) return;
                _specialLabel = _specialLabelInput.Text;
            };
            _winLoseConfigPanel.Children.Add(_specialLabelInput);

            WrapPanel quickChips = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            string[] chipTexts = new string[] { "🎉 당첨!", "💣 벌칙!", "☕ 커피 쏘기", "👑 술래" };
            string[] chipNormal = new string[] { "꽝", "통과", "잘 마실게요", "생존" };
            for (int i = 0; i < chipTexts.Length; i++)
            {
                int ci = i;
                Button cb = _host.MakeSmallButton(chipTexts[i], "#334155", "#FDE68A");
                cb.Margin = new Thickness(0, 0, 5, 4);
                cb.Click += (s, e) =>
                {
                    _suppressInputEvents = true;
                    _specialLabel = chipTexts[ci];
                    _normalLabel = chipNormal[ci];
                    _specialLabelInput.Text = _specialLabel;
                    _normalLabelInput.Text = _normalLabel;
                    _suppressInputEvents = false;
                    ResetAndShuffleLots();
                    _host.FocusMainWindowForShortcuts();
                };
                quickChips.Children.Add(cb);
            }
            _winLoseConfigPanel.Children.Add(quickChips);

            _winLoseConfigPanel.Children.Add(new TextBlock
            {
                Text = "기본(나머지) 제비 문구",
                FontSize = 11.5,
                Foreground = MainWindow.BrushFromHex("#94A3B8"),
                Margin = new Thickness(2, 0, 0, 4)
            });

            _normalLabelInput = new TextBox
            {
                Text = _normalLabel,
                Height = 30,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = MainWindow.BrushFromHex("#0F172A"),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                BorderBrush = MainWindow.BrushFromHex("#475569"),
                Padding = new Thickness(8, 0, 8, 0),
                FontSize = 12.5
            };
            InputMethod.SetIsInputMethodEnabled(_normalLabelInput, true);
            _normalLabelInput.TextChanged += (s, e) =>
            {
                if (_suppressInputEvents) return;
                _normalLabel = _normalLabelInput.Text;
            };
            _winLoseConfigPanel.Children.Add(_normalLabelInput);
            sp.Children.Add(_winLoseConfigPanel);

            // [Mode 1 Panel]: 1번 ~ N번 순서 뽑기
            _orderConfigPanel = new StackPanel();
            Border orderCard = new Border
            {
                Background = MainWindow.BrushFromHex("#0F172A"),
                BorderBrush = MainWindow.BrushFromHex("#3B82F6"),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(11, 10, 11, 10)
            };
            StackPanel orderInner = new StackPanel();
            orderInner.Children.Add(new TextBlock
            {
                Text = "표기 형식 선택",
                FontSize = 11.5,
                Foreground = MainWindow.BrushFromHex("#93C5FD"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            UniformGrid fmtRow = new UniformGrid { Rows = 1, Columns = 2, Margin = new Thickness(0, 0, 0, 8) };
            _orderNumFmtBtn = _host.MakeSmallButton("1번 ~ N번 (순서)", "#2563EB", "#FFFFFF");
            _orderNumFmtBtn.Height = 29;
            _orderNumFmtBtn.Margin = new Thickness(0, 0, 4, 0);
            _orderNumFmtBtn.Click += (s, e) =>
            {
                _orderUseRankSuffix = false;
                UpdateModeVisibilityUI();
                ResetAndShuffleLots();
                _host.FocusMainWindowForShortcuts();
            };

            _orderRankFmtBtn = _host.MakeSmallButton("1등 ~ N등 (순위)", "#1E293B", "#CBD5E1");
            _orderRankFmtBtn.Height = 29;
            _orderRankFmtBtn.Margin = new Thickness(4, 0, 0, 0);
            _orderRankFmtBtn.Click += (s, e) =>
            {
                _orderUseRankSuffix = true;
                UpdateModeVisibilityUI();
                ResetAndShuffleLots();
                _host.FocusMainWindowForShortcuts();
            };
            fmtRow.Children.Add(_orderNumFmtBtn);
            fmtRow.Children.Add(_orderRankFmtBtn);
            orderInner.Children.Add(fmtRow);

            _orderPreviewDescText = new TextBlock
            {
                Text = "1번부터 6번까지 번호표 제비가 1장씩 무작위로 섞입니다.",
                FontSize = 12,
                Foreground = MainWindow.BrushFromHex("#CBD5E1"),
                TextWrapping = TextWrapping.Wrap
            };
            orderInner.Children.Add(_orderPreviewDescText);
            orderCard.Child = orderInner;
            _orderConfigPanel.Children.Add(orderCard);
            sp.Children.Add(_orderConfigPanel);

            // [Mode 2 Panel]: 직접 자유 문구 편집
            _customConfigPanel = new StackPanel();
            Border customCard = new Border
            {
                Background = MainWindow.BrushFromHex("#0F172A"),
                BorderBrush = MainWindow.BrushFromHex("#8B5CF6"),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(11, 10, 11, 10)
            };
            StackPanel customInner = new StackPanel();
            _customSummaryText = new TextBlock
            {
                Text = "",
                FontSize = 12,
                Foreground = MainWindow.BrushFromHex("#E2E8F0"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            customInner.Children.Add(_customSummaryText);

            Button openCustomBtn = _host.MakeStyledButton("📝 제비 문구 목록 편집하기", "#6D28D9", "#FFFFFF", 12.5, 34);
            openCustomBtn.Click += (s, e) => OpenCustomLotsDialog();
            customInner.Children.Add(openCustomBtn);
            customCard.Child = customInner;
            _customConfigPanel.Children.Add(customCard);
            sp.Children.Add(_customConfigPanel);

            // --- Divider + Section 3: Optional Participant Names ---
            sp.Children.Add(BuildSectionLine());
            sp.Children.Add(BuildSectionHeader("(선택) 뽑는 사람 기록"));

            Border drawerBox = new Border
            {
                Background = MainWindow.BrushFromHex("#0F172A"),
                BorderBrush = MainWindow.BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8)
            };
            DockPanel dDock = new DockPanel();
            Button editDrawerBtn = _host.MakeSmallButton("명단 설정", "#334155", "#93C5FD");
            editDrawerBtn.Click += (s, e) => OpenDrawerNamesDialog();
            DockPanel.SetDock(editDrawerBtn, Dock.Right);
            dDock.Children.Add(editDrawerBtn);

            _drawerInfoText = new TextBlock
            {
                Text = "미지정 (1번째, 2번째 순서로 기록)",
                FontSize = 11.5,
                Foreground = MainWindow.BrushFromHex("#94A3B8"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            dDock.Children.Add(_drawerInfoText);
            drawerBox.Child = dDock;
            sp.Children.Add(drawerBox);

            sv.Content = sp;
            SetRow(sv, 1);
            g.Children.Add(sv);

            Button applyShuffleBtn = _host.MakeStyledButton("🎲 설정대로 제비 새로 섞기", "#D97706", "#FFFFFF", 13.5, 40);
            applyShuffleBtn.Margin = new Thickness(0, 10, 0, 0);
            applyShuffleBtn.Click += (s, e) =>
            {
                ResetAndShuffleLots();
                _host.FocusMainWindowForShortcuts();
            };
            SetRow(applyShuffleBtn, 2);
            g.Children.Add(applyShuffleBtn);

            card.Child = g;
            return card;
        }

        private Border BuildStepperBox(string label, out TextBlock capBlock, out TextBlock valBlock, string initVal, Action onMinus, Action onPlus)
        {
            Border box = new Border
            {
                Background = MainWindow.BrushFromHex("#0F172A"),
                BorderBrush = MainWindow.BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel dp = new DockPanel();
            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal };
            Button m = _host.MakeSmallButton("−", "#334155", "#FFFFFF");
            m.Width = 30;
            m.Click += (s, e) => onMinus();

            valBlock = new TextBlock
            {
                Text = initVal,
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = MainWindow.BrushFromHex("#FBBF24"),
                Width = 50,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Button p = _host.MakeSmallButton("+", "#2563EB", "#FFFFFF");
            p.Width = 30;
            p.Click += (s, e) => onPlus();

            btns.Children.Add(m);
            btns.Children.Add(valBlock);
            btns.Children.Add(p);
            DockPanel.SetDock(btns, Dock.Right);
            dp.Children.Add(btns);

            capBlock = new TextBlock
            {
                Text = label,
                FontSize = 12.5,
                Foreground = MainWindow.BrushFromHex("#CBD5E1"),
                VerticalAlignment = VerticalAlignment.Center
            };
            dp.Children.Add(capBlock);
            box.Child = dp;
            return box;
        }

        private UIElement BuildCenterBoardCard()
        {
            Border card = _host.MakeCardBorder();
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Status bar
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Tickets grid
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Bottom actions

            DockPanel hdr = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            TextBlock title = new TextBlock
            {
                Text = "🎫 제비뽑기 보드",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };

            _toggleHintBtn = _host.MakeSmallButton("남은 당첨 수 표시: ON", "#334155", "#E2E8F0");
            _toggleHintBtn.Click += (s, e) =>
            {
                _showRemainingSpecialHint = !_showRemainingSpecialHint;
                _toggleHintBtn.Content = _showRemainingSpecialHint ? "남은 당첨 수 표시: ON" : "남은 당첨 수 표시: OFF";
                UpdateBoardStatusUI();
                _host.FocusMainWindowForShortcuts();
            };
            DockPanel.SetDock(_toggleHintBtn, Dock.Right);
            hdr.Children.Add(_toggleHintBtn);
            hdr.Children.Add(title);
            SetRow(hdr, 0);
            g.Children.Add(hdr);

            Border statusBox = new Border
            {
                Background = MainWindow.BrushFromHex("#0F172A"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 7, 12, 7),
                Margin = new Thickness(0, 0, 0, 10)
            };
            DockPanel sDock = new DockPanel();
            _remainingSpecialBadge = new Border
            {
                Background = MainWindow.BrushFromHex("#78350F"),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(9, 2, 9, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            _remainingSpecialText = new TextBlock
            {
                Text = "남은 당첨: 1개",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = MainWindow.BrushFromHex("#FDE68A")
            };
            _remainingSpecialBadge.Child = _remainingSpecialText;
            DockPanel.SetDock(_remainingSpecialBadge, Dock.Right);
            sDock.Children.Add(_remainingSpecialBadge);

            _boardStatusText = new TextBlock
            {
                Text = "원하는 제비 카드를 직접 클릭하거나 Space를 눌러 뽑으세요!",
                FontSize = 12.5,
                Foreground = MainWindow.BrushFromHex("#E2E8F0"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            sDock.Children.Add(_boardStatusText);
            statusBox.Child = sDock;
            SetRow(statusBox, 1);
            g.Children.Add(statusBox);

            Border boardBg = new Border
            {
                Background = MainWindow.BrushFromHex("#0B1120"),
                BorderBrush = MainWindow.BrushFromHex("#1E293B"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10)
            };
            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _ticketsWrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sv.Content = _ticketsWrap;
            boardBg.Child = sv;
            SetRow(boardBg, 2);
            g.Children.Add(boardBg);

            Grid botGrid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            botGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
            botGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            botGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.95, GridUnitType.Star) });
            botGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            botGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });

            _drawOneBtn = _host.MakeStyledButton("👆 랜덤 1개 뽑기 (Space)", "#D97706", "#FFFFFF", 13.5, 42);
            _drawOneBtn.Click += (s, e) => HandleSpaceKey();
            SetColumn(_drawOneBtn, 0);
            botGrid.Children.Add(_drawOneBtn);

            Button revealAllBtn = _host.MakeStyledButton("👀 남은 제비 모두 공개", "#334155", "#FFFFFF", 13, 42);
            revealAllBtn.Click += (s, e) => RevealAllRemainingLots();
            SetColumn(revealAllBtn, 2);
            botGrid.Children.Add(revealAllBtn);

            Button reshuffleBtn = _host.MakeStyledButton("🔄 다시 섞기", "#2563EB", "#FFFFFF", 13, 42);
            reshuffleBtn.Click += (s, e) =>
            {
                ResetAndShuffleLots();
                _host.FocusMainWindowForShortcuts();
            };
            SetColumn(reshuffleBtn, 4);
            botGrid.Children.Add(reshuffleBtn);

            SetRow(botGrid, 3);
            g.Children.Add(botGrid);

            card.Child = g;
            return card;
        }

        private UIElement BuildRightLogCard()
        {
            Border card = _host.MakeCardBorder();
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            DockPanel hdr = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            hdr.Children.Add(new TextBlock
            {
                Text = "제비 뽑기 기록",
                FontSize = 15.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });
            SetRow(hdr, 0);
            g.Children.Add(hdr);

            _logScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8)
            };
            _logStack = new StackPanel();
            _logScroll.Content = _logStack;

            Border logWrap = new Border
            {
                Background = MainWindow.BrushFromHex("#0B1120"),
                BorderBrush = MainWindow.BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = _logScroll
            };
            SetRow(logWrap, 1);
            g.Children.Add(logWrap);

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

            Button copyBtn = _host.MakeStyledButton("📋 제비뽑기 결과 복사", "#334155", "#34D399", 13, 38);
            copyBtn.Click += (s, e) => CopyLotsResultToClipboard();
            footer.Children.Add(copyBtn);
            SetRow(footer, 2);
            g.Children.Add(footer);

            card.Child = g;
            return card;
        }

        private void SelectLotMode(int mode)
        {
            if (mode < 0 || mode > 2) return;
            _lotMode = mode;
            UpdateModeVisibilityUI();
            ResetAndShuffleLots();
            _host.FocusMainWindowForShortcuts();
        }

        private void UpdateModeVisibilityUI()
        {
            string[] activeColors = new string[] { "#D97706", "#2563EB", "#7C3AED" };
            for (int i = 0; i < _modeButtons.Count; i++)
            {
                bool active = (i == _lotMode);
                _modeButtons[i].Background = MainWindow.BrushFromHex(active ? activeColors[i] : "#0F172A");
                _modeButtons[i].Foreground = MainWindow.BrushFromHex(active ? "#FFFFFF" : "#94A3B8");
            }

            // Total count stepper is shown in Mode 0 (당첨·꽝) & Mode 1 (순서 뽑기), hidden in Mode 2 (자유 편집)
            FrameworkElement totalStepperParent = _totalCountText.Parent as FrameworkElement;
            while (totalStepperParent != null && !(totalStepperParent is Border))
            {
                totalStepperParent = totalStepperParent.Parent as FrameworkElement;
            }
            if (totalStepperParent != null)
            {
                totalStepperParent.Visibility = (_lotMode == 2) ? Visibility.Collapsed : Visibility.Visible;
            }
            _totalCountCapText.Text = (_lotMode == 1) ? "순서 제비 수 (2~36명)" : "전체 제비 수 (2~36개)";

            _winLoseConfigPanel.Visibility = (_lotMode == 0) ? Visibility.Visible : Visibility.Collapsed;
            _orderConfigPanel.Visibility = (_lotMode == 1) ? Visibility.Visible : Visibility.Collapsed;
            _customConfigPanel.Visibility = (_lotMode == 2) ? Visibility.Visible : Visibility.Collapsed;

            _orderNumFmtBtn.Background = MainWindow.BrushFromHex(!_orderUseRankSuffix ? "#2563EB" : "#1E293B");
            _orderNumFmtBtn.Foreground = MainWindow.BrushFromHex(!_orderUseRankSuffix ? "#FFFFFF" : "#CBD5E1");
            _orderRankFmtBtn.Background = MainWindow.BrushFromHex(_orderUseRankSuffix ? "#2563EB" : "#1E293B");
            _orderRankFmtBtn.Foreground = MainWindow.BrushFromHex(_orderUseRankSuffix ? "#FFFFFF" : "#CBD5E1");

            string unit = _orderUseRankSuffix ? "등" : "번";
            _orderPreviewDescText.Text = string.Format(
                "총 {0}장의 제비에 [1{1} ~ {0}{1}] 번호가 한 장씩 무작위로 섞여 배치됩니다.",
                _totalCount, unit);

            List<string> customPreview = new List<string>();
            for (int i = 0; i < _customTemplateItems.Count && i < 6; i++)
            {
                customPreview.Add(_customTemplateItems[i].Text);
            }
            string extra = (_customTemplateItems.Count > 6) ? string.Format(" 외 {0}장", _customTemplateItems.Count - 6) : "";
            _customSummaryText.Text = string.Format(
                "현재 총 {0}장 구성됨:\n{1}{2}",
                _customTemplateItems.Count,
                string.Join(", ", customPreview.ToArray()),
                extra);

            if (_toggleHintBtn != null)
            {
                _toggleHintBtn.Visibility = (_lotMode == 1) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void ChangeTotalCount(int delta)
        {
            int next = _totalCount + delta;
            if (next < 2 || next > 36) return;
            _totalCount = next;
            if (_specialCount >= _totalCount) _specialCount = _totalCount - 1;
            UpdateLeftLabelsUI();
            UpdateModeVisibilityUI();
            ResetAndShuffleLots();
            _host.FocusMainWindowForShortcuts();
        }

        private void ChangeSpecialCount(int delta)
        {
            int next = _specialCount + delta;
            if (next < 1 || next >= _totalCount) return;
            _specialCount = next;
            UpdateLeftLabelsUI();
            ResetAndShuffleLots();
            _host.FocusMainWindowForShortcuts();
        }

        private void UpdateLeftLabelsUI()
        {
            _totalCountText.Text = string.Format("{0}개", _totalCount);
            _specialCountText.Text = string.Format("{0}개", _specialCount);
        }

        private void OpenCustomLotsDialog()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < _customTemplateItems.Count; i++)
            {
                sb.AppendLine(_customTemplateItems[i].Text);
            }
            string res = _host.ShowMultilineDialog(
                "제비 문구 직접 편집",
                "한 줄에 제비 하나씩 문구를 입력하세요 (2~36개).\n'꽝', '통과' 외의 문구는 자동으로 당첨(강조) 제비로 표시됩니다:",
                sb.ToString());
            if (res == null) return;

            string[] lines = res.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            List<LotTicketItem> list = new List<LotTicketItem>();
            for (int i = 0; i < lines.Length && list.Count < 36; i++)
            {
                string t = lines[i].Trim();
                if (string.IsNullOrEmpty(t)) continue;
                list.Add(new LotTicketItem
                {
                    Text = t,
                    IsSpecial = LadderBoardControl.IsSpecialResultText(t)
                });
            }

            if (list.Count < 2)
            {
                MessageBox.Show(_host, "제비는 최소 2개 이상 입력해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _customTemplateItems.Clear();
            _customTemplateItems.AddRange(list);
            _lotMode = 2;
            UpdateModeVisibilityUI();
            ResetAndShuffleLots();
        }

        private void OpenDrawerNamesDialog()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < _drawerNames.Count; i++) sb.AppendLine(_drawerNames[i]);
            string res = _host.ShowMultilineDialog(
                "뽑는 사람 명단 설정 (선택 사항)",
                "제비를 뽑을 순서대로 이름을 한 줄에 한 명씩 입력하세요.\n비워두면 이름 없이 1번째, 2번째 순서로만 깔끔하게 기록됩니다:",
                sb.ToString());
            if (res == null) return;

            _drawerNames.Clear();
            string[] lines = res.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (!string.IsNullOrEmpty(t)) _drawerNames.Add(t);
            }

            _drawerInfoText.Text = (_drawerNames.Count > 0)
                ? string.Format("명단 지정됨: 총 {0}명", _drawerNames.Count)
                : "미지정 (1번째, 2번째 순서로 기록)";
        }

        public void ResetAndShuffleLots()
        {
            _tickets.Clear();
            _pickedSequence = 0;

            if (_lotMode == 1)
            {
                string unit = _orderUseRankSuffix ? "등" : "번";
                for (int i = 1; i <= _totalCount; i++)
                {
                    _tickets.Add(new LotTicketItem
                    {
                        Text = string.Format("{0}{1}", i, unit),
                        IsSpecial = (i == 1),
                        IsOrderNumber = true,
                        IsRevealed = false,
                        PickOrder = 0,
                        DrawerName = null
                    });
                }
            }
            else if (_lotMode == 2 && _customTemplateItems.Count >= 2)
            {
                for (int i = 0; i < _customTemplateItems.Count; i++)
                {
                    _tickets.Add(new LotTicketItem
                    {
                        Text = _customTemplateItems[i].Text,
                        IsSpecial = _customTemplateItems[i].IsSpecial,
                        IsOrderNumber = false,
                        IsRevealed = false,
                        PickOrder = 0,
                        DrawerName = null
                    });
                }
            }
            else
            {
                string spTxt = string.IsNullOrWhiteSpace(_specialLabel) ? "🎉 당첨!" : _specialLabel.Trim();
                string nmTxt = string.IsNullOrWhiteSpace(_normalLabel) ? "꽝" : _normalLabel.Trim();
                for (int i = 0; i < _totalCount; i++)
                {
                    bool sp = (i < _specialCount);
                    _tickets.Add(new LotTicketItem
                    {
                        Text = sp ? spTxt : nmTxt,
                        IsSpecial = sp,
                        IsOrderNumber = false,
                        IsRevealed = false,
                        PickOrder = 0,
                        DrawerName = null
                    });
                }
            }

            // Fair Fisher-Yates shuffle
            for (int i = _tickets.Count - 1; i > 0; i--)
            {
                int j = _host.NextFairRandomInt(i + 1);
                LotTicketItem tmp = _tickets[i];
                _tickets[i] = _tickets[j];
                _tickets[j] = tmp;
            }

            for (int i = 0; i < _tickets.Count; i++)
            {
                _tickets[i].SlotNumber = i + 1;
            }

            _copyFeedbackText.Text = "";
            UpdateLeftLabelsUI();
            RebuildTicketsBoardUI();
            RebuildLogUI();
            UpdateBoardStatusUI();
        }

        public void HandleSpaceKey()
        {
            _host.FocusMainWindowForShortcuts();

            List<int> unpicked = new List<int>();
            for (int i = 0; i < _tickets.Count; i++)
            {
                if (!_tickets[i].IsRevealed) unpicked.Add(i);
            }

            if (unpicked.Count == 0)
            {
                ResetAndShuffleLots();
                return;
            }

            int pickIdx = unpicked[_host.NextFairRandomInt(unpicked.Count)];
            PickTicketAt(pickIdx);
        }

        private void PickTicketAt(int idx)
        {
            if (idx < 0 || idx >= _tickets.Count) return;
            LotTicketItem item = _tickets[idx];
            if (item.IsRevealed) return;

            _pickedSequence++;
            item.IsRevealed = true;
            item.PickOrder = _pickedSequence;
            if (_pickedSequence - 1 < _drawerNames.Count)
            {
                item.DrawerName = _drawerNames[_pickedSequence - 1];
            }

            if (item.IsSpecial)
                SoundEffects.PlayWin(_host.IsSoundEnabled);
            else if (item.IsOrderNumber)
                SoundEffects.PlayReach(_host.IsSoundEnabled);
            else
                SoundEffects.PlayTick(_host.IsSoundEnabled);

            RebuildTicketsBoardUI();
            RebuildLogUI();
            UpdateBoardStatusUI();
        }

        private void RevealAllRemainingLots()
        {
            _host.FocusMainWindowForShortcuts();
            bool anyUnrevealed = false;
            for (int i = 0; i < _tickets.Count; i++)
            {
                if (!_tickets[i].IsRevealed)
                {
                    _tickets[i].IsRevealed = true;
                    _tickets[i].PickOrder = 0;
                    anyUnrevealed = true;
                }
            }
            if (anyUnrevealed)
            {
                SoundEffects.PlayReach(_host.IsSoundEnabled);
                RebuildTicketsBoardUI();
                UpdateBoardStatusUI();
            }
        }

        private void UpdateBoardStatusUI()
        {
            int remTotal = 0;
            int remSpecial = 0;
            for (int i = 0; i < _tickets.Count; i++)
            {
                if (!_tickets[i].IsRevealed)
                {
                    remTotal++;
                    if (_tickets[i].IsSpecial) remSpecial++;
                }
            }

            if (_lotMode == 1)
            {
                _remainingSpecialBadge.Visibility = Visibility.Visible;
                string unit = _orderUseRankSuffix ? "등" : "번";
                _remainingSpecialText.Text = string.Format("🔢 순서 뽑기 (1{0}~{1}{0})", unit, _tickets.Count);
                _remainingSpecialBadge.Background = MainWindow.BrushFromHex("#1E3A8A");
                _remainingSpecialText.Foreground = MainWindow.BrushFromHex("#93C5FD");
            }
            else
            {
                _remainingSpecialBadge.Visibility = _showRemainingSpecialHint ? Visibility.Visible : Visibility.Collapsed;
                _remainingSpecialText.Text = string.Format("남은 주요 제비: {0}개", remSpecial);
                _remainingSpecialBadge.Background = MainWindow.BrushFromHex(remSpecial > 0 ? "#78350F" : "#1E293B");
                _remainingSpecialText.Foreground = MainWindow.BrushFromHex(remSpecial > 0 ? "#FDE68A" : "#94A3B8");
            }

            if (remTotal == 0)
            {
                _boardStatusText.Text = "모든 제비가 공개되었습니다! [다시 섞기] 또는 Space로 새 판을 시작하세요.";
                _drawOneBtn.Content = "🔄 새로 섞어서 다시 시작 (Space)";
                _drawOneBtn.Background = MainWindow.BrushFromHex("#059669");
            }
            else
            {
                string nextWho = (_pickedSequence < _drawerNames.Count)
                    ? string.Format(" · 다음 차례: [{0}]", _drawerNames[_pickedSequence])
                    : "";
                _boardStatusText.Text = string.Format("남은 제비: {0} / {1}개{2}", remTotal, _tickets.Count, nextWho);
                _drawOneBtn.Content = "👆 랜덤 1개 뽑기 (Space)";
                _drawOneBtn.Background = MainWindow.BrushFromHex("#D97706");
            }
        }

        private void RebuildTicketsBoardUI()
        {
            _ticketsWrap.Children.Clear();
            double cardW = (_tickets.Count > 20) ? 98 : 118;
            double cardH = (_tickets.Count > 20) ? 78 : 92;

            for (int i = 0; i < _tickets.Count; i++)
            {
                int idx = i;
                LotTicketItem item = _tickets[i];

                Border card = new Border
                {
                    Width = cardW,
                    Height = cardH,
                    CornerRadius = new CornerRadius(10),
                    Margin = new Thickness(5),
                    Padding = new Thickness(8, 6, 8, 6),
                    BorderThickness = new Thickness(item.IsRevealed && (item.IsSpecial || item.IsOrderNumber) ? 2.0 : 1.3),
                    Cursor = item.IsRevealed ? Cursors.Arrow : Cursors.Hand
                };

                Grid cg = new Grid();
                cg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                cg.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                cg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                if (!item.IsRevealed)
                {
                    card.Background = MainWindow.BrushFromHex("#1E293B");
                    card.BorderBrush = MainWindow.BrushFromHex("#475569");
                    card.MouseEnter += (s, e) => card.BorderBrush = MainWindow.BrushFromHex("#FBBF24");
                    card.MouseLeave += (s, e) => card.BorderBrush = MainWindow.BrushFromHex("#475569");
                    card.MouseLeftButtonDown += (s, e) =>
                    {
                        _host.FocusMainWindowForShortcuts();
                        PickTicketAt(idx);
                        e.Handled = true;
                    };

                    TextBlock numTxt = new TextBlock
                    {
                        Text = string.Format("#{0}번 제비", item.SlotNumber),
                        FontSize = 11,
                        Foreground = MainWindow.BrushFromHex("#94A3B8"),
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    SetRow(numTxt, 0);
                    cg.Children.Add(numTxt);

                    TextBlock iconTxt = new TextBlock
                    {
                        Text = "🎫",
                        FontSize = (_tickets.Count > 20) ? 20 : 26,
                        Foreground = MainWindow.BrushFromHex("#38BDF8"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    SetRow(iconTxt, 1);
                    cg.Children.Add(iconTxt);

                    TextBlock hintTxt = new TextBlock
                    {
                        Text = "클릭해 뽑기",
                        FontSize = 10.5,
                        Foreground = MainWindow.BrushFromHex("#FBBF24"),
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    SetRow(hintTxt, 2);
                    cg.Children.Add(hintTxt);
                }
                else
                {
                    string mainFgHex;
                    string subFgHex;
                    if (item.IsSpecial)
                    {
                        card.Background = MainWindow.BrushFromHex("#064E3B");
                        card.BorderBrush = MainWindow.BrushFromHex("#34D399");
                        mainFgHex = "#FEF08A";
                        subFgHex = "#A7F3D0";
                    }
                    else if (item.IsOrderNumber)
                    {
                        card.Background = MainWindow.BrushFromHex("#172554");
                        card.BorderBrush = MainWindow.BrushFromHex("#3B82F6");
                        mainFgHex = "#FFFFFF";
                        subFgHex = "#93C5FD";
                    }
                    else
                    {
                        card.Background = MainWindow.BrushFromHex("#111827");
                        card.BorderBrush = MainWindow.BrushFromHex("#1E293B");
                        mainFgHex = "#94A3B8";
                        subFgHex = "#64748B";
                    }

                    string topCaption = (item.PickOrder > 0)
                        ? (string.IsNullOrEmpty(item.DrawerName)
                            ? string.Format("#{0} · {1}번째", item.SlotNumber, item.PickOrder)
                            : string.Format("{0}", item.DrawerName))
                        : string.Format("#{0} · 전체공개", item.SlotNumber);

                    TextBlock topTxt = new TextBlock
                    {
                        Text = topCaption,
                        FontSize = 10.5,
                        Foreground = MainWindow.BrushFromHex(subFgHex),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    SetRow(topTxt, 0);
                    cg.Children.Add(topTxt);

                    TextBlock mainTxt = new TextBlock
                    {
                        Text = item.Text,
                        FontSize = (_tickets.Count > 20) ? 13 : (item.IsOrderNumber ? 16 : 14),
                        FontWeight = (item.IsSpecial || item.IsOrderNumber) ? FontWeights.Bold : FontWeights.SemiBold,
                        Foreground = MainWindow.BrushFromHex(mainFgHex),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    };
                    SetRow(mainTxt, 1);
                    cg.Children.Add(mainTxt);
                }

                card.Child = cg;
                _ticketsWrap.Children.Add(card);
            }
        }

        private void RebuildLogUI()
        {
            _logStack.Children.Clear();

            List<LotTicketItem> picked = new List<LotTicketItem>();
            for (int i = 0; i < _tickets.Count; i++)
            {
                if (_tickets[i].IsRevealed && _tickets[i].PickOrder > 0)
                {
                    picked.Add(_tickets[i]);
                }
            }
            picked.Sort((a, b) => a.PickOrder.CompareTo(b.PickOrder));

            if (picked.Count == 0)
            {
                _logStack.Children.Add(new TextBlock
                {
                    Text = "아직 뽑은 제비가 없습니다.\n가운데 카드를 클릭하거나 Space를 눌러 뽑아보세요!",
                    FontSize = 12,
                    Foreground = MainWindow.BrushFromHex("#64748B"),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(6, 36, 6, 20)
                });
                return;
            }

            for (int i = 0; i < picked.Count; i++)
            {
                LotTicketItem it = picked[i];
                string badgeBg = it.IsSpecial ? "#059669" : (it.IsOrderNumber ? "#2563EB" : "#334155");

                Border row = new Border
                {
                    Background = MainWindow.BrushFromHex(it.IsSpecial ? "#071F18" : (it.IsOrderNumber ? "#0F1E3A" : "#111827")),
                    BorderBrush = MainWindow.BrushFromHex(it.IsSpecial ? "#10B981" : (it.IsOrderNumber ? "#2563EB" : "#1E293B")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(9, 6, 9, 6),
                    Margin = new Thickness(0, 0, 0, 5)
                };

                DockPanel dp = new DockPanel();
                Border badge = new Border
                {
                    Background = MainWindow.BrushFromHex(badgeBg),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(8, 2, 8, 2),
                    Child = new TextBlock
                    {
                        Text = it.Text,
                        FontSize = 12,
                        FontWeight = (it.IsSpecial || it.IsOrderNumber) ? FontWeights.Bold : FontWeights.Normal,
                        Foreground = Brushes.White
                    }
                };
                DockPanel.SetDock(badge, Dock.Right);
                dp.Children.Add(badge);

                string who = string.IsNullOrEmpty(it.DrawerName)
                    ? string.Format("{0}번째 (#{1}번 제비)", it.PickOrder, it.SlotNumber)
                    : string.Format("{0}. {1} (#{2}번)", it.PickOrder, it.DrawerName, it.SlotNumber);

                dp.Children.Add(new TextBlock
                {
                    Text = who,
                    FontSize = 12.5,
                    Foreground = MainWindow.BrushFromHex("#E2E8F0"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                row.Child = dp;
                _logStack.Children.Add(row);
            }
            _logScroll.ScrollToEnd();
        }

        private void CopyLotsResultToClipboard()
        {
            List<LotTicketItem> picked = new List<LotTicketItem>();
            for (int i = 0; i < _tickets.Count; i++)
            {
                if (_tickets[i].IsRevealed && _tickets[i].PickOrder > 0) picked.Add(_tickets[i]);
            }
            picked.Sort((a, b) => a.PickOrder.CompareTo(b.PickOrder));

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[제비뽑기 결과]");
            if (picked.Count == 0)
            {
                for (int i = 0; i < _tickets.Count; i++)
                {
                    sb.AppendLine(string.Format("#{0}번 제비: {1}", _tickets[i].SlotNumber, _tickets[i].IsRevealed ? _tickets[i].Text : "(미공개)"));
                }
            }
            else
            {
                for (int i = 0; i < picked.Count; i++)
                {
                    LotTicketItem it = picked[i];
                    string who = string.IsNullOrEmpty(it.DrawerName)
                        ? string.Format("{0}번째 (#{1}번)", it.PickOrder, it.SlotNumber)
                        : string.Format("{0}번째 {1}", it.PickOrder, it.DrawerName);
                    sb.AppendLine(string.Format("{0} → {1}", who, it.Text));
                }
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
