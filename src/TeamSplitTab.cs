using System;
using System.Collections.Generic;
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
    public class TeamMemberEntry
    {
        public string Name;
        public bool IsSeed;
        public int Tier; // 1 (1티어, 최상) ~ 5 (5티어), 기본값 3 (3티어)

        public TeamMemberEntry(string name, bool isSeed)
        {
            Name = name;
            IsSeed = isSeed;
            Tier = 3;
        }

        public TeamMemberEntry(string name, bool isSeed, int tier)
        {
            Name = name;
            IsSeed = isSeed;
            Tier = (tier < 1 || tier > 5) ? 3 : tier;
        }
    }

    public class AssignedMemberInfo
    {
        public string Name;
        public bool IsSeed;
        public int Tier;
        public bool IsLeader;
    }

    // =========================================================================
    // Tab 3: 팀 나누기 (Selectable Team Count Splitter View)
    // =========================================================================
    public class TeamSplitTabView : Grid
    {
        private readonly MainWindow _host;

        private int _teamCount = 2; // 2 ~ 12 teams
        private bool _pickLeader = false;
        private bool _useTierBalance = false; // Default OFF: preserves original functionality unless toggled ON
        private readonly List<TeamMemberEntry> _members = new List<TeamMemberEntry>();
        private List<List<AssignedMemberInfo>> _currentTeams = new List<List<AssignedMemberInfo>>();
        private int _splitRoundCount = 0;

        // Shuffle animation
        private DispatcherTimer _shuffleTimer;
        private int _shuffleTicksLeft = 0;

        // UI Elements - Left
        private TextBlock _teamCountValueText;
        private readonly List<Button> _quickTeamBtns = new List<Button>();
        private TextBlock _quotaPreviewText;
        private CheckBox _leaderCheck;
        private Border _tierModeBox;
        private TextBlock _tierModeLabel;
        private Button _tierToggleBtn;
        private DockPanel _tierHintRow;
        private DockPanel _seedStatusRow;
        private TextBlock _seedStatusText;
        private Button _resetSeedsBtn;
        private TextBox _memberInput;
        private TextBlock _memberCountBadgeText;
        private StackPanel _memberListStack;

        // UI Elements - Center
        private TextBlock _boardSubtitleText;
        private ScrollViewer _teamsBoardScroll;
        private UniformGrid _teamsGrid;
        private TextBlock _emptyBoardHintText;
        private Button _splitActionBtn;

        // UI Elements - Right
        private TextBox _resultPreviewBox;
        private TextBlock _copyFeedbackText;
        private StackPanel _historyStack;

        public TeamSplitTabView(MainWindow host)
        {
            _host = host;

            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.90, GridUnitType.Star), MinWidth = 290 });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.34, GridUnitType.Star), MinWidth = 390 });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.76, GridUnitType.Star), MinWidth = 250 });

            UIElement left = BuildLeftPanel();
            SetColumn(left, 0);
            Children.Add(left);

            UIElement center = BuildCenterBoard();
            SetColumn(center, 2);
            Children.Add(center);

            UIElement right = BuildRightSummaryCard();
            SetColumn(right, 4);
            Children.Add(right);

            _shuffleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(55) };
            _shuffleTimer.Tick += OnShuffleAnimTick;

            UpdateTeamCountControlsUI();
            UpdateTierModeUI();
            RebuildMemberListUI();
            RenderTeamsBoardUI(false);
        }

        private UIElement BuildLeftPanel()
        {
            Border card = _host.MakeCardBorder();
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Team count selector box
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: Leader option
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Tier balance ON/OFF toggle
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3: Member header & tools
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4: Add input
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 5: Tier legend strip (only when ON)
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 6: Member list

            // 1. Prominent Team Count Selector Card (2 ~ 12 Teams)
            Border teamBox = new Border
            {
                Background = MainWindow.BrushFromHex("#0F172A"),
                BorderBrush = MainWindow.BrushFromHex("#7C3AED"),
                BorderThickness = new Thickness(1.4),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(11, 9, 11, 9),
                Margin = new Thickness(0, 0, 0, 9)
            };
            StackPanel tbSp = new StackPanel();

            DockPanel topRow = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            StackPanel stepper = new StackPanel { Orientation = Orientation.Horizontal };

            Button minusBtn = _host.MakeSmallButton("−", "#334155", "#FFFFFF");
            minusBtn.Width = 32;
            minusBtn.Height = 29;
            minusBtn.Click += (s, e) => SetTeamCount(_teamCount - 1);

            _teamCountValueText = new TextBlock
            {
                Text = "2 팀",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = MainWindow.BrushFromHex("#C4B5FD"),
                Width = 58,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Button plusBtn = _host.MakeSmallButton("+", "#7C3AED", "#FFFFFF");
            plusBtn.Width = 32;
            plusBtn.Height = 29;
            plusBtn.Click += (s, e) => SetTeamCount(_teamCount + 1);

            stepper.Children.Add(minusBtn);
            stepper.Children.Add(_teamCountValueText);
            stepper.Children.Add(plusBtn);
            DockPanel.SetDock(stepper, Dock.Right);
            topRow.Children.Add(stepper);

            topRow.Children.Add(new TextBlock
            {
                Text = "팀 개수 선택 (2~12팀)",
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });
            tbSp.Children.Add(topRow);

            // Quick team count pills (2~6팀)
            UniformGrid quickRow = new UniformGrid { Rows = 1, Columns = 5, Margin = new Thickness(0, 0, 0, 6) };
            _quickTeamBtns.Clear();
            for (int t = 2; t <= 6; t++)
            {
                int capturedT = t;
                Button qb = _host.MakeSmallButton(string.Format("{0}팀", t), "#1E293B", "#CBD5E1");
                qb.Margin = new Thickness(t == 2 ? 0 : 2, 0, t == 6 ? 0 : 2, 0);
                qb.Click += (s, e) => SetTeamCount(capturedT);
                _quickTeamBtns.Add(qb);
                quickRow.Children.Add(qb);
            }
            tbSp.Children.Add(quickRow);

            _quotaPreviewText = new TextBlock
            {
                Text = "참가자를 추가하면 팀별 배정 인원이 표시됩니다",
                FontSize = 11.5,
                Foreground = MainWindow.BrushFromHex("#94A3B8"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            tbSp.Children.Add(_quotaPreviewText);
            teamBox.Child = tbSp;
            SetRow(teamBox, 0);
            g.Children.Add(teamBox);

            // 2. Leader option checkbox
            _leaderCheck = new CheckBox
            {
                Content = "👑 각 팀에서 팀장(대표) 1명 자동 뽑기",
                IsChecked = _pickLeader,
                Foreground = MainWindow.BrushFromHex("#E2E8F0"),
                FontSize = 12,
                Focusable = false,
                Margin = new Thickness(2, 0, 0, 7)
            };
            _leaderCheck.Checked += (s, e) => { _pickLeader = true; };
            _leaderCheck.Unchecked += (s, e) => { _pickLeader = false; };
            SetRow(_leaderCheck, 1);
            g.Children.Add(_leaderCheck);

            // 2-1. Tier Balance ON/OFF Toggle Card (Default OFF)
            _tierModeBox = new Border
            {
                Background = MainWindow.BrushFromHex("#0F172A"),
                BorderBrush = MainWindow.BrushFromHex("#334155"),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 9)
            };
            DockPanel tierDock = new DockPanel();
            _tierToggleBtn = _host.MakeSmallButton("OFF", "#334155", "#94A3B8");
            _tierToggleBtn.Height = 24;
            _tierToggleBtn.MinWidth = 76;
            _tierToggleBtn.Padding = new Thickness(10, 0, 10, 0);
            _tierToggleBtn.Click += (s, e) => ToggleTierBalanceMode();
            DockPanel.SetDock(_tierToggleBtn, Dock.Right);
            tierDock.Children.Add(_tierToggleBtn);

            _tierModeLabel = new TextBlock
            {
                Text = "⚖️ 등급(티어) 균등 분배",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = MainWindow.BrushFromHex("#CBD5E1"),
                VerticalAlignment = VerticalAlignment.Center
            };
            tierDock.Children.Add(_tierModeLabel);
            _tierModeBox.Child = tierDock;
            SetRow(_tierModeBox, 2);
            g.Children.Add(_tierModeBox);

            // 3. Member list header & quick buttons
            DockPanel memHdr = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            StackPanel memTools = new StackPanel { Orientation = Orientation.Horizontal };

            Button bulkBtn = _host.MakeSmallButton("일괄 입력", "#334155", "#E2E8F0");
            bulkBtn.Margin = new Thickness(0, 0, 4, 0);
            bulkBtn.Click += (s, e) => OpenBulkMembersDialog();
            memTools.Children.Add(bulkBtn);

            Button autoNumBtn = _host.MakeSmallButton("번호 생성", "#334155", "#93C5FD");
            autoNumBtn.Margin = new Thickness(0, 0, 4, 0);
            autoNumBtn.ToolTip = "이름 입력 없이 인원 수만큼 '1번 ~ N번'으로 빠르게 채웁니다";
            autoNumBtn.Click += (s, e) => PromptFillNumberedMembers();
            memTools.Children.Add(autoNumBtn);

            Button clearBtn = _host.MakeSmallButton("비우기", "#334155", "#FCA5A5");
            clearBtn.Click += (s, e) =>
            {
                _members.Clear();
                _currentTeams.Clear();
                RebuildMemberListUI();
                RenderTeamsBoardUI(false);
                _host.FocusMainWindowForShortcuts();
            };
            memTools.Children.Add(clearBtn);

            DockPanel.SetDock(memTools, Dock.Right);
            memHdr.Children.Add(memTools);

            StackPanel titleLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleLeft.Children.Add(new TextBlock
            {
                Text = "명단",
                FontSize = 14.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });

            Border cntPill = new Border
            {
                Background = MainWindow.BrushFromHex("#0F172A"),
                BorderBrush = MainWindow.BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(7, 1, 7, 1),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _memberCountBadgeText = new TextBlock
            {
                Text = "0명",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = MainWindow.BrushFromHex("#38BDF8")
            };
            cntPill.Child = _memberCountBadgeText;
            titleLeft.Children.Add(cntPill);

            memHdr.Children.Add(titleLeft);
            SetRow(memHdr, 3);
            g.Children.Add(memHdr);

            // 4. Single member input row
            Grid addRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid inputWrap = new Grid { Margin = new Thickness(0, 0, 6, 0) };
            _memberInput = new TextBox
            {
                Height = 33,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = MainWindow.BrushFromHex("#0F172A"),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                BorderBrush = MainWindow.BrushFromHex("#475569"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 0, 8, 0),
                FontSize = 13
            };
            InputMethod.SetIsInputMethodEnabled(_memberInput, true);

            TextBlock hint = new TextBlock
            {
                Text = "이름 입력 후 Enter (쉼표 구분 가능)",
                Foreground = MainWindow.BrushFromHex("#64748B"),
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 6, 0),
                IsHitTestVisible = false
            };
            _memberInput.TextChanged += (s, e) =>
            {
                hint.Visibility = string.IsNullOrEmpty(_memberInput.Text) ? Visibility.Visible : Visibility.Collapsed;
            };
            _memberInput.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    AddMembersFromInputBox();
                    e.Handled = true;
                }
            };
            inputWrap.Children.Add(_memberInput);
            inputWrap.Children.Add(hint);
            SetColumn(inputWrap, 0);
            addRow.Children.Add(inputWrap);

            Button addBtn = _host.MakeStyledButton("추가", "#2563EB", "#FFFFFF", 12.5, 33);
            addBtn.Padding = new Thickness(13, 0, 13, 0);
            addBtn.Click += (s, e) => AddMembersFromInputBox();
            SetColumn(addBtn, 1);
            addRow.Children.Add(addBtn);

            SetRow(addRow, 4);
            g.Children.Add(addRow);

            // 5. Status/guide strip above member list:
            //    - Seed mode (default OFF): shows current seed count (red when seed > teamCount)
            //    - Tier mode (ON): shows 1~5 tier guide + reset button
            Grid modeSubBar = new Grid { Margin = new Thickness(2, 0, 0, 5) };

            _seedStatusRow = new DockPanel { Visibility = Visibility.Visible };
            _resetSeedsBtn = _host.MakeSmallButton("시드 초기화", "#1E293B", "#FDE68A");
            _resetSeedsBtn.Height = 20;
            _resetSeedsBtn.FontSize = 10.5;
            _resetSeedsBtn.Padding = new Thickness(6, 0, 6, 0);
            _resetSeedsBtn.Click += (s, e) =>
            {
                for (int i = 0; i < _members.Count; i++) _members[i].IsSeed = false;
                RebuildMemberListUI();
                _host.FocusMainWindowForShortcuts();
            };
            DockPanel.SetDock(_resetSeedsBtn, Dock.Right);
            _seedStatusRow.Children.Add(_resetSeedsBtn);

            _seedStatusText = new TextBlock
            {
                Text = "★ 시드 현황: 0 / 2명 (팀 수 이하)",
                FontSize = 11,
                Foreground = MainWindow.BrushFromHex("#94A3B8"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _seedStatusRow.Children.Add(_seedStatusText);
            modeSubBar.Children.Add(_seedStatusRow);

            _tierHintRow = new DockPanel { Visibility = Visibility.Collapsed };
            Button resetTiersBtn = _host.MakeSmallButton("전원 3티어", "#1E293B", "#93C5FD");
            resetTiersBtn.Height = 20;
            resetTiersBtn.FontSize = 10.5;
            resetTiersBtn.Padding = new Thickness(6, 0, 6, 0);
            resetTiersBtn.Click += (s, e) =>
            {
                for (int i = 0; i < _members.Count; i++) _members[i].Tier = 3;
                RebuildMemberListUI();
                _host.FocusMainWindowForShortcuts();
            };
            DockPanel.SetDock(resetTiersBtn, Dock.Right);
            _tierHintRow.Children.Add(resetTiersBtn);
            _tierHintRow.Children.Add(new TextBlock
            {
                Text = "1티어(최상)~5티어(하) · 기본 3티어",
                FontSize = 11,
                Foreground = MainWindow.BrushFromHex("#7DD3FC"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            modeSubBar.Children.Add(_tierHintRow);

            SetRow(modeSubBar, 5);
            g.Children.Add(modeSubBar);

            // 6. Scrollable member list
            Border listBorder = new Border
            {
                Background = MainWindow.BrushFromHex("#0B1120"),
                BorderBrush = MainWindow.BrushFromHex("#1E293B"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6)
            };
            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _memberListStack = new StackPanel();
            sv.Content = _memberListStack;
            listBorder.Child = sv;
            SetRow(listBorder, 6);
            g.Children.Add(listBorder);

            card.Child = g;
            return card;
        }

        private UIElement BuildCenterBoard()
        {
            Border card = _host.MakeCardBorder();
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            DockPanel hdr = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            _boardSubtitleText = new TextBlock
            {
                Text = "★시드 표시된 인원은 서로 다른 팀으로 균등 분산됩니다",
                FontSize = 11.5,
                Foreground = MainWindow.BrushFromHex("#94A3B8"),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_boardSubtitleText, Dock.Right);
            hdr.Children.Add(_boardSubtitleText);

            hdr.Children.Add(new TextBlock
            {
                Text = "👥 팀 배정 결과 보드",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });
            SetRow(hdr, 0);
            g.Children.Add(hdr);

            Border boardBg = new Border
            {
                Background = MainWindow.BrushFromHex("#0B1120"),
                BorderBrush = MainWindow.BrushFromHex("#1E293B"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10)
            };
            Grid centerInner = new Grid();

            _emptyBoardHintText = new TextBlock
            {
                Text = "왼쪽에서 팀 개수를 고르고 참가자를 추가한 뒤\n아래 [🎲 랜덤 팀 나누기! (Space)] 버튼을 눌러주세요.",
                FontSize = 13.5,
                Foreground = MainWindow.BrushFromHex("#64748B"),
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            centerInner.Children.Add(_emptyBoardHintText);

            _teamsBoardScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _teamsGrid = new UniformGrid { Columns = 2, VerticalAlignment = VerticalAlignment.Top };
            _teamsBoardScroll.Content = _teamsGrid;
            centerInner.Children.Add(_teamsBoardScroll);

            boardBg.Child = centerInner;
            SetRow(boardBg, 1);
            g.Children.Add(boardBg);

            _splitActionBtn = _host.MakeStyledButton("🎲 랜덤 팀 나누기! (Space)", "#7C3AED", "#FFFFFF", 15, 46);
            _splitActionBtn.Margin = new Thickness(0, 10, 0, 0);
            ToolTipService.SetShowOnDisabled(_splitActionBtn, true);
            ToolTipService.SetInitialShowDelay(_splitActionBtn, 120);
            ToolTipService.SetShowDuration(_splitActionBtn, 12000);
            _splitActionBtn.Click += (s, e) => HandleSpaceKey();
            SetRow(_splitActionBtn, 2);
            g.Children.Add(_splitActionBtn);

            card.Child = g;
            return card;
        }

        private UIElement BuildRightSummaryCard()
        {
            Border card = _host.MakeCardBorder();
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Title
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.2, GridUnitType.Star) }); // Editable text box
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Copy btn
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // History title
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.8, GridUnitType.Star) }); // History list

            TextBlock title = new TextBlock
            {
                Text = "공유용 텍스트 & 배정 기록",
                FontSize = 15.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            SetRow(title, 0);
            g.Children.Add(title);

            _resultPreviewBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = MainWindow.BrushFromHex("#0B1120"),
                Foreground = MainWindow.BrushFromHex("#F1F5F9"),
                CaretBrush = MainWindow.BrushFromHex("#38BDF8"),
                BorderBrush = MainWindow.BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                FontSize = 12.5,
                Text = ""
            };
            InputMethod.SetIsInputMethodEnabled(_resultPreviewBox, true);
            SetRow(_resultPreviewBox, 1);
            g.Children.Add(_resultPreviewBox);

            StackPanel copyRow = new StackPanel { Margin = new Thickness(0, 6, 0, 10) };
            _copyFeedbackText = new TextBlock
            {
                Text = "",
                FontSize = 11.5,
                Foreground = MainWindow.BrushFromHex("#34D399"),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 0, 4)
            };
            copyRow.Children.Add(_copyFeedbackText);

            Button copyBtn = _host.MakeStyledButton("📋 카톡 / 디스코드용 복사", "#334155", "#34D399", 13, 36);
            copyBtn.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(_resultPreviewBox.Text ?? "");
                    _copyFeedbackText.Text = "✔ 클립보드에 복사되었습니다!";
                }
                catch { }
                _host.FocusMainWindowForShortcuts();
            };
            copyRow.Children.Add(copyBtn);
            SetRow(copyRow, 2);
            g.Children.Add(copyRow);

            TextBlock histTitle = new TextBlock
            {
                Text = "최근 팀 배정 기록 (클릭 시 복원)",
                FontSize = 12,
                Foreground = MainWindow.BrushFromHex("#94A3B8"),
                Margin = new Thickness(0, 0, 0, 5)
            };
            SetRow(histTitle, 3);
            g.Children.Add(histTitle);

            Border histBorder = new Border
            {
                Background = MainWindow.BrushFromHex("#0B1120"),
                BorderBrush = MainWindow.BrushFromHex("#1E293B"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6)
            };
            ScrollViewer hsv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _historyStack = new StackPanel();
            hsv.Content = _historyStack;
            histBorder.Child = hsv;
            SetRow(histBorder, 4);
            g.Children.Add(histBorder);

            card.Child = g;
            return card;
        }

        private void SetTeamCount(int count)
        {
            if (count < 2) count = 2;
            if (count > 12) count = 12;
            _teamCount = count;
            UpdateTeamCountControlsUI();
            RebuildMemberListUI();
            _host.FocusMainWindowForShortcuts();
        }

        private void UpdateTeamCountControlsUI()
        {
            _teamCountValueText.Text = string.Format("{0} 팀", _teamCount);
            for (int i = 0; i < _quickTeamBtns.Count; i++)
            {
                int t = i + 2;
                bool active = (t == _teamCount);
                _quickTeamBtns[i].Background = MainWindow.BrushFromHex(active ? "#7C3AED" : "#1E293B");
                _quickTeamBtns[i].Foreground = MainWindow.BrushFromHex(active ? "#FFFFFF" : "#CBD5E1");
            }

            int n = _members.Count;
            if (n == 0)
            {
                _quotaPreviewText.Text = string.Format("현재 {0}팀 선택됨 — 아래에 참가자를 추가해 주세요", _teamCount);
            }
            else if (n < _teamCount)
            {
                _quotaPreviewText.Text = string.Format("총 {0}명 / {1}팀 (일부 팀은 빈 팀이 됩니다)", n, _teamCount);
            }
            else
            {
                int baseCnt = n / _teamCount;
                int rem = n % _teamCount;
                if (rem == 0)
                    _quotaPreviewText.Text = string.Format("총 {0}명 → {1}팀 (팀당 {2}명씩 균등 배정)", n, _teamCount, baseCnt);
                else
                    _quotaPreviewText.Text = string.Format("총 {0}명 → {1}팀 ({2}명 {3}팀 · {4}명 {5}팀)",
                        n, _teamCount, baseCnt + 1, rem, baseCnt, _teamCount - rem);
            }
        }

        private void AddMembersFromInputBox()
        {
            string raw = _memberInput.Text ?? "";
            string[] tokens = raw.Split(new char[] { ',', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            bool added = false;
            for (int i = 0; i < tokens.Length; i++)
            {
                string t = tokens[i].Trim();
                if (!string.IsNullOrEmpty(t))
                {
                    _members.Add(new TeamMemberEntry(t, false));
                    added = true;
                }
            }
            _memberInput.Text = "";
            if (added)
            {
                RebuildMemberListUI();
                UpdateTeamCountControlsUI();
            }
        }

        private void ToggleTierBalanceMode()
        {
            _useTierBalance = !_useTierBalance;
            UpdateTierModeUI();
            RebuildMemberListUI();
            RenderTeamsBoardUI(false);
            if (_currentTeams != null && _currentTeams.Count > 0)
            {
                _resultPreviewBox.Text = BuildFormattedSummaryText();
            }
            _host.FocusMainWindowForShortcuts();
        }

        private int GetSeedCount()
        {
            int cnt = 0;
            for (int i = 0; i < _members.Count; i++)
            {
                if (_members[i].IsSeed) cnt++;
            }
            return cnt;
        }

        private void UpdateSeedAndSplitStateUI()
        {
            int seedCnt = GetSeedCount();
            bool seedExceeded = (!_useTierBalance && seedCnt > _teamCount);

            if (_seedStatusText != null)
            {
                if (seedCnt > _teamCount)
                {
                    _seedStatusText.Text = string.Format("⚠️ 시드 초과: {0} / {1}명 (시드 해제 필요)", seedCnt, _teamCount);
                    _seedStatusText.Foreground = MainWindow.BrushFromHex("#F87171");
                    _seedStatusText.FontWeight = FontWeights.Bold;
                }
                else
                {
                    _seedStatusText.Text = string.Format("★ 시드 현황: {0} / {1}명 (팀 수 이하)", seedCnt, _teamCount);
                    _seedStatusText.Foreground = MainWindow.BrushFromHex(seedCnt > 0 ? "#FDE68A" : "#94A3B8");
                    _seedStatusText.FontWeight = FontWeights.Normal;
                }
            }

            if (_resetSeedsBtn != null)
            {
                _resetSeedsBtn.Visibility = (seedCnt > 0) ? Visibility.Visible : Visibility.Collapsed;
            }

            if (_splitActionBtn != null)
            {
                if (seedExceeded)
                {
                    if (_shuffleTimer != null && _shuffleTimer.IsEnabled)
                    {
                        _shuffleTimer.Stop();
                    }
                    _splitActionBtn.IsEnabled = false;
                    _splitActionBtn.Background = MainWindow.BrushFromHex("#334155");
                    _splitActionBtn.Foreground = MainWindow.BrushFromHex("#64748B");
                    _splitActionBtn.Cursor = Cursors.Arrow;
                    _splitActionBtn.Content = (_splitRoundCount > 0)
                        ? "🎲 다시 랜덤 팀 나누기! (Space)"
                        : "🎲 랜덤 팀 나누기! (Space)";
                    _splitActionBtn.ToolTip = string.Format(
                        "시드가 초과하였습니다. (현재 시드 {0}명 / 최대 {1}명)\n시드를 해제하거나 팀 개수를 늘려주세요.",
                        seedCnt, _teamCount);
                }
                else
                {
                    _splitActionBtn.IsEnabled = true;
                    _splitActionBtn.Background = MainWindow.BrushFromHex("#7C3AED");
                    _splitActionBtn.Foreground = Brushes.White;
                    _splitActionBtn.Cursor = Cursors.Hand;
                    _splitActionBtn.ToolTip = null;
                    if (_shuffleTimer == null || !_shuffleTimer.IsEnabled)
                    {
                        _splitActionBtn.Content = (_splitRoundCount > 0)
                            ? "🎲 다시 랜덤 팀 나누기! (Space)"
                            : "🎲 랜덤 팀 나누기! (Space)";
                    }
                }
            }
        }

        private void UpdateTierModeUI()
        {
            if (_tierModeBox == null || _tierToggleBtn == null || _tierModeLabel == null) return;

            if (_useTierBalance)
            {
                _tierModeBox.BorderBrush = MainWindow.BrushFromHex("#0EA5E9");
                _tierModeLabel.Foreground = MainWindow.BrushFromHex("#38BDF8");
                _tierToggleBtn.Content = "ON (1~5티어)";
                _tierToggleBtn.Background = MainWindow.BrushFromHex("#0284C7");
                _tierToggleBtn.Foreground = Brushes.White;
                if (_tierHintRow != null) _tierHintRow.Visibility = Visibility.Visible;
                if (_seedStatusRow != null) _seedStatusRow.Visibility = Visibility.Collapsed;
                if (_boardSubtitleText != null)
                {
                    _boardSubtitleText.Text = "⚖️ 등급 밸런스 ON — 1~5티어 실력이 각 팀에 균등 분배됩니다";
                    _boardSubtitleText.Foreground = MainWindow.BrushFromHex("#38BDF8");
                }
            }
            else
            {
                _tierModeBox.BorderBrush = MainWindow.BrushFromHex("#334155");
                _tierModeLabel.Foreground = MainWindow.BrushFromHex("#CBD5E1");
                _tierToggleBtn.Content = "OFF";
                _tierToggleBtn.Background = MainWindow.BrushFromHex("#334155");
                _tierToggleBtn.Foreground = MainWindow.BrushFromHex("#94A3B8");
                if (_tierHintRow != null) _tierHintRow.Visibility = Visibility.Collapsed;
                if (_seedStatusRow != null) _seedStatusRow.Visibility = Visibility.Visible;
                if (_boardSubtitleText != null)
                {
                    _boardSubtitleText.Text = "★시드 표시된 인원은 서로 다른 팀으로 균등 분산됩니다";
                    _boardSubtitleText.Foreground = MainWindow.BrushFromHex("#94A3B8");
                }
            }

            UpdateSeedAndSplitStateUI();
        }

        private static string GetTierBgHex(int tier)
        {
            switch (tier)
            {
                case 1: return "#B45309";
                case 2: return "#6D28D9";
                case 3: return "#0369A1";
                case 4: return "#047857";
                case 5: return "#334155";
                default: return "#0369A1";
            }
        }

        private static string GetTierFgHex(int tier)
        {
            switch (tier)
            {
                case 1: return "#FEF08A";
                case 2: return "#EDE9FE";
                case 3: return "#E0F2FE";
                case 4: return "#D1FAE5";
                case 5: return "#E2E8F0";
                default: return "#E0F2FE";
            }
        }

        private void OpenBulkMembersDialog()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < _members.Count; i++)
            {
                sb.AppendLine(_members[i].Name);
            }
            string res = _host.ShowMultilineDialog(
                "참가자 명단 일괄 입력",
                "한 줄에 한 명씩(또는 쉼표로 구분하여) 참가자 이름을 붙여넣으세요:",
                sb.ToString());
            if (res == null) return;

            List<TeamMemberEntry> prevPool = new List<TeamMemberEntry>(_members);
            string[] tokens = res.Split(new char[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
            _members.Clear();
            for (int i = 0; i < tokens.Length; i++)
            {
                string t = tokens[i].Trim();
                if (!string.IsNullOrEmpty(t))
                {
                    bool matchedSeed = false;
                    int matchedTier = 3;
                    for (int p = 0; p < prevPool.Count; p++)
                    {
                        if (prevPool[p].Name == t)
                        {
                            matchedSeed = prevPool[p].IsSeed;
                            matchedTier = prevPool[p].Tier;
                            prevPool.RemoveAt(p);
                            break;
                        }
                    }
                    _members.Add(new TeamMemberEntry(t, matchedSeed, matchedTier));
                }
            }
            RebuildMemberListUI();
            UpdateTeamCountControlsUI();
        }

        private void PromptFillNumberedMembers()
        {
            int defaultCnt = Math.Max(_teamCount * 2, 6);
            string ans = _host.PromptInputDialog("번호로 인원 채우기", "총 몇 명으로 채울까요? (2 ~ 60명):", defaultCnt.ToString());
            _host.FocusMainWindowForShortcuts();
            if (string.IsNullOrWhiteSpace(ans)) return;

            int cnt;
            if (!int.TryParse(ans.Trim(), out cnt) || cnt < 2 || cnt > 60)
            {
                MessageBox.Show(_host, "2에서 60 사이의 숫자를 입력해 주세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _members.Clear();
            for (int i = 1; i <= cnt; i++)
            {
                _members.Add(new TeamMemberEntry(string.Format("{0}번 참가자", i), false, 3));
            }
            RebuildMemberListUI();
            UpdateTeamCountControlsUI();
        }

        private void RebuildMemberListUI()
        {
            _memberListStack.Children.Clear();
            _memberCountBadgeText.Text = string.Format("{0}명", _members.Count);
            UpdateSeedAndSplitStateUI();

            if (_members.Count == 0)
            {
                _memberListStack.Children.Add(new TextBlock
                {
                    Text = "참가자 명단이 비어 있습니다.\n위 입력창에서 추가하거나 [일괄 입력], [번호 생성]을 사용해 보세요.",
                    FontSize = 11.5,
                    Foreground = MainWindow.BrushFromHex("#64748B"),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(6, 28, 6, 16)
                });
                return;
            }

            int curSeedCnt = GetSeedCount();
            bool seedExceeded = (curSeedCnt > _teamCount);
            bool seedAtOrOverCap = (curSeedCnt >= _teamCount);

            for (int i = 0; i < _members.Count; i++)
            {
                int idx = i;
                TeamMemberEntry m = _members[i];

                string rowBorderHex = _useTierBalance
                    ? (m.Tier == 1 ? "#B45309" : (m.Tier == 2 ? "#5B21B6" : "#1E293B"))
                    : (m.IsSeed ? (seedExceeded ? "#EF4444" : "#D97706") : "#1E293B");

                Border row = new Border
                {
                    Background = MainWindow.BrushFromHex("#111827"),
                    BorderBrush = MainWindow.BrushFromHex(rowBorderHex),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 4, 6, 4),
                    Margin = new Thickness(0, 0, 0, 4)
                };

                DockPanel dp = new DockPanel();
                StackPanel rightBtns = new StackPanel { Orientation = Orientation.Horizontal };

                if (_useTierBalance)
                {
                    for (int tierVal = 1; tierVal <= 5; tierVal++)
                    {
                        int capturedTier = tierVal;
                        bool isCur = (m.Tier == tierVal);
                        Button tBtn = _host.MakeSmallButton(
                            isCur ? string.Format("{0}티어", tierVal) : tierVal.ToString(),
                            isCur ? GetTierBgHex(tierVal) : "#1E293B",
                            isCur ? GetTierFgHex(tierVal) : "#94A3B8");
                        tBtn.Height = 22;
                        tBtn.Width = isCur ? 38 : 19;
                        tBtn.FontSize = 10.5;
                        tBtn.Padding = new Thickness(0);
                        tBtn.Margin = new Thickness(0, 0, tierVal == 5 ? 4 : 2, 0);
                        tBtn.ToolTip = string.Format("{0}티어로 설정", tierVal);
                        tBtn.Click += (s, e) =>
                        {
                            _members[idx].Tier = capturedTier;
                            RebuildMemberListUI();
                            _host.FocusMainWindowForShortcuts();
                        };
                        rightBtns.Children.Add(tBtn);
                    }
                }
                else
                {
                    Button seedBtn = _host.MakeSmallButton(
                        m.IsSeed ? "★시드" : "☆시드",
                        m.IsSeed ? (seedExceeded ? "#7F1D1D" : "#92400E") : "#1E293B",
                        m.IsSeed ? (seedExceeded ? "#FECACA" : "#FDE68A") : "#94A3B8");
                    seedBtn.Height = 23;
                    seedBtn.FontSize = 10.5;
                    seedBtn.Padding = new Thickness(6, 0, 6, 0);
                    seedBtn.Margin = new Thickness(0, 0, 4, 0);
                    ToolTipService.SetShowOnDisabled(seedBtn, true);
                    ToolTipService.SetInitialShowDelay(seedBtn, 120);

                    if (!m.IsSeed && seedAtOrOverCap)
                    {
                        seedBtn.IsEnabled = false;
                        seedBtn.Opacity = 0.42;
                        seedBtn.Cursor = Cursors.Arrow;
                        seedBtn.ToolTip = string.Format("시드는 팀 수(현재 {0}팀)까지만 지정할 수 있습니다.", _teamCount);
                    }
                    else
                    {
                        seedBtn.ToolTip = m.IsSeed
                            ? "클릭하여 시드 해제"
                            : "시드로 지정된 인원들은 같은 팀에 몰리지 않고 각 팀에 골고루 분산됩니다";
                        seedBtn.Click += (s, e) =>
                        {
                            if (!_members[idx].IsSeed && GetSeedCount() >= _teamCount)
                                return;
                            _members[idx].IsSeed = !_members[idx].IsSeed;
                            RebuildMemberListUI();
                            _host.FocusMainWindowForShortcuts();
                        };
                    }
                    rightBtns.Children.Add(seedBtn);
                }

                Button delBtn = _host.MakeSmallButton("✕", "#1E293B", "#FCA5A5");
                delBtn.Height = _useTierBalance ? 22 : 23;
                delBtn.Width = _useTierBalance ? 22 : 24;
                delBtn.Padding = new Thickness(0);
                delBtn.Click += (s, e) =>
                {
                    _members.RemoveAt(idx);
                    RebuildMemberListUI();
                    UpdateTeamCountControlsUI();
                    _host.FocusMainWindowForShortcuts();
                };
                rightBtns.Children.Add(delBtn);

                DockPanel.SetDock(rightBtns, Dock.Right);
                dp.Children.Add(rightBtns);

                dp.Children.Add(new TextBlock
                {
                    Text = string.Format("{0}. {1}", i + 1, m.Name),
                    FontSize = 12.5,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                row.Child = dp;
                _memberListStack.Children.Add(row);
            }
        }

        public void HandleSpaceKey()
        {
            _host.FocusMainWindowForShortcuts();

            if (!_useTierBalance && GetSeedCount() > _teamCount)
            {
                return;
            }

            if (_members.Count < 2)
            {
                MessageBox.Show(_host, "팀을 나누려면 왼쪽에서 최소 2명 이상의 참가자를 추가해 주세요.", "참가자 부족", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_shuffleTimer.IsEnabled)
            {
                FinishTeamSplitFinal();
                return;
            }

            _shuffleTicksLeft = 6;
            _splitActionBtn.Content = "🎲 팀 섞는 중...";
            _shuffleTimer.Start();
            PerformRandomAssignmentInternal();
            RenderTeamsBoardUI(true);
            SoundEffects.PlayTick(_host.IsSoundEnabled);
        }

        private void OnShuffleAnimTick(object sender, EventArgs e)
        {
            _shuffleTicksLeft--;
            if (_shuffleTicksLeft <= 0)
            {
                FinishTeamSplitFinal();
                return;
            }

            PerformRandomAssignmentInternal();
            RenderTeamsBoardUI(true);
            SoundEffects.PlayTick(_host.IsSoundEnabled);
        }

        private void FinishTeamSplitFinal()
        {
            _shuffleTimer.Stop();
            PerformRandomAssignmentInternal();
            _splitRoundCount++;

            SoundEffects.PlayWin(_host.IsSoundEnabled);
            _splitActionBtn.Content = "🎲 다시 랜덤 팀 나누기! (Space)";
            RenderTeamsBoardUI(false);

            string summary = BuildFormattedSummaryText();
            _resultPreviewBox.Text = summary;
            _copyFeedbackText.Text = "";
            AddHistoryEntry(_splitRoundCount, summary);
        }

        private void PerformRandomAssignmentInternal()
        {
            if (_useTierBalance)
            {
                PerformTierBalancedAssignmentInternal();
                return;
            }

            int k = _teamCount;
            List<TeamMemberEntry> seeds = new List<TeamMemberEntry>();
            List<TeamMemberEntry> regs = new List<TeamMemberEntry>();

            for (int i = 0; i < _members.Count; i++)
            {
                if (_members[i].IsSeed) seeds.Add(_members[i]);
                else regs.Add(_members[i]);
            }

            ShuffleList(seeds);
            ShuffleList(regs);

            List<TeamMemberEntry> combined = new List<TeamMemberEntry>(seeds.Count + regs.Count);
            combined.AddRange(seeds);
            combined.AddRange(regs);

            // Randomize team order so extra +1 member slots are unbiased across all teams
            List<int> teamOrder = new List<int>(k);
            for (int t = 0; t < k; t++) teamOrder.Add(t);
            ShuffleList(teamOrder);

            _currentTeams = new List<List<AssignedMemberInfo>>(k);
            for (int t = 0; t < k; t++) _currentTeams.Add(new List<AssignedMemberInfo>());

            for (int i = 0; i < combined.Count; i++)
            {
                int targetTeam = teamOrder[i % k];
                _currentTeams[targetTeam].Add(new AssignedMemberInfo
                {
                    Name = combined[i].Name,
                    IsSeed = combined[i].IsSeed,
                    Tier = combined[i].Tier,
                    IsLeader = false
                });
            }

            FinalizeTeamMemberOrderAndLeaders(k);
        }

        private void PerformTierBalancedAssignmentInternal()
        {
            int k = _teamCount;
            int n = _members.Count;

            // 1. Group by tier (1..5), shuffle inside each tier for fair randomness
            List<TeamMemberEntry> orderedPool = new List<TeamMemberEntry>(n);
            for (int tier = 1; tier <= 5; tier++)
            {
                List<TeamMemberEntry> group = new List<TeamMemberEntry>();
                for (int i = 0; i < n; i++)
                {
                    int mt = (_members[i].Tier < 1 || _members[i].Tier > 5) ? 3 : _members[i].Tier;
                    if (mt == tier) group.Add(_members[i]);
                }
                ShuffleList(group);
                orderedPool.AddRange(group);
            }

            // 2. Snake draft across randomized team permutation (spreads every tier evenly & keeps team sizes within ±1)
            List<int> teamOrder = new List<int>(k);
            for (int t = 0; t < k; t++) teamOrder.Add(t);
            ShuffleList(teamOrder);

            List<List<AssignedMemberInfo>> teams = new List<List<AssignedMemberInfo>>(k);
            for (int t = 0; t < k; t++) teams.Add(new List<AssignedMemberInfo>());

            for (int i = 0; i < orderedPool.Count; i++)
            {
                int pass = i / k;
                int offset = i % k;
                int orderIdx = (pass % 2 == 0) ? offset : (k - 1 - offset);
                int targetTeam = teamOrder[orderIdx];

                teams[targetTeam].Add(new AssignedMemberInfo
                {
                    Name = orderedPool[i].Name,
                    IsSeed = orderedPool[i].IsSeed,
                    Tier = orderedPool[i].Tier,
                    IsLeader = false
                });
            }

            // 3. Local 1-for-1 swap polish: minimize weighted squared deviation from overall mean tier
            // while never concentrating 1티어 players unfairly.
            if (n >= k && k >= 2)
            {
                double totalTierSum = 0.0;
                for (int i = 0; i < n; i++) totalTierSum += _members[i].Tier;
                double overallAvg = totalTierSum / n;

                for (int iter = 0; iter < 24; iter++)
                {
                    bool improved = false;
                    double curCost = ComputeTierBalanceCost(teams, overallAvg);

                    for (int t1 = 0; t1 < k; t1++)
                    {
                        for (int t2 = t1 + 1; t2 < k; t2++)
                        {
                            for (int a = 0; a < teams[t1].Count; a++)
                            {
                                for (int b = 0; b < teams[t2].Count; b++)
                                {
                                    if (teams[t1][a].Tier == teams[t2][b].Tier) continue;

                                    AssignedMemberInfo tmp = teams[t1][a];
                                    teams[t1][a] = teams[t2][b];
                                    teams[t2][b] = tmp;

                                    double newCost = ComputeTierBalanceCost(teams, overallAvg);
                                    if (newCost < curCost - 1e-6)
                                    {
                                        curCost = newCost;
                                        improved = true;
                                    }
                                    else
                                    {
                                        // Revert
                                        tmp = teams[t1][a];
                                        teams[t1][a] = teams[t2][b];
                                        teams[t2][b] = tmp;
                                    }
                                }
                            }
                        }
                    }

                    if (!improved) break;
                }
            }

            _currentTeams = teams;
            FinalizeTeamMemberOrderAndLeaders(k);
        }

        private static double ComputeTierBalanceCost(List<List<AssignedMemberInfo>> teams, double overallAvg)
        {
            double cost = 0.0;
            int minT1 = int.MaxValue;
            int maxT1 = 0;

            for (int t = 0; t < teams.Count; t++)
            {
                List<AssignedMemberInfo> list = teams[t];
                if (list.Count == 0) continue;

                double sum = 0.0;
                int t1Cnt = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    sum += list[i].Tier;
                    if (list[i].Tier == 1) t1Cnt++;
                }
                double avg = sum / list.Count;
                double diff = avg - overallAvg;
                cost += list.Count * diff * diff;

                if (t1Cnt < minT1) minT1 = t1Cnt;
                if (t1Cnt > maxT1) maxT1 = t1Cnt;
            }

            if (maxT1 - minT1 > 1)
            {
                cost += (maxT1 - minT1 - 1) * 4.0;
            }
            return cost;
        }

        private void FinalizeTeamMemberOrderAndLeaders(int k)
        {
            for (int t = 0; t < k; t++)
            {
                List<AssignedMemberInfo> list = _currentTeams[t];
                ShuffleList(list);
                if (_pickLeader && list.Count > 0)
                {
                    int leaderIdx = _host.NextFairRandomInt(list.Count);
                    list[leaderIdx].IsLeader = true;
                    if (leaderIdx != 0)
                    {
                        AssignedMemberInfo tmp = list[0];
                        list[0] = list[leaderIdx];
                        list[leaderIdx] = tmp;
                    }
                }
            }
        }

        private void ShuffleList<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _host.NextFairRandomInt(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        private static double GetTeamAverageTier(List<AssignedMemberInfo> list)
        {
            if (list == null || list.Count == 0) return 0.0;
            double sum = 0.0;
            for (int i = 0; i < list.Count; i++) sum += list[i].Tier;
            return sum / list.Count;
        }

        private void RenderTeamsBoardUI(bool isShufflingPreview)
        {
            _teamsGrid.Children.Clear();

            if (_currentTeams == null || _currentTeams.Count == 0)
            {
                _emptyBoardHintText.Visibility = Visibility.Visible;
                _teamsBoardScroll.Visibility = Visibility.Collapsed;
                return;
            }

            _emptyBoardHintText.Visibility = Visibility.Collapsed;
            _teamsBoardScroll.Visibility = Visibility.Visible;

            int k = _currentTeams.Count;
            _teamsGrid.Columns = (k <= 4) ? 2 : 3;

            for (int t = 0; t < k; t++)
            {
                Color tCol = MainWindow.SlicePalette[t % MainWindow.SlicePalette.Length];
                List<AssignedMemberInfo> list = _currentTeams[t];

                Border tCard = new Border
                {
                    Background = MainWindow.BrushFromHex("#151F32"),
                    BorderBrush = new SolidColorBrush(isShufflingPreview ? Color.FromRgb(100, 116, 139) : tCol),
                    BorderThickness = new Thickness(1.6),
                    CornerRadius = new CornerRadius(10),
                    Margin = new Thickness(5),
                    Padding = new Thickness(11, 9, 11, 10)
                };

                StackPanel sp = new StackPanel();

                // Team Header
                DockPanel hdr = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                string badgeLabel = (_useTierBalance && list.Count > 0)
                    ? string.Format("{0}명 · 평균 {1:0.0}티어", list.Count, GetTeamAverageTier(list))
                    : string.Format("{0}명", list.Count);

                Border cntBadge = new Border
                {
                    Background = MainWindow.BrushFromHex("#0F172A"),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(7, 2, 7, 2),
                    Child = new TextBlock
                    {
                        Text = badgeLabel,
                        FontSize = 11.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = MainWindow.BrushFromHex(_useTierBalance ? "#7DD3FC" : "#CBD5E1")
                    }
                };
                DockPanel.SetDock(cntBadge, Dock.Right);
                hdr.Children.Add(cntBadge);

                StackPanel nameRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                nameRow.Children.Add(new Ellipse
                {
                    Width = 11,
                    Height = 11,
                    Fill = new SolidColorBrush(tCol),
                    Margin = new Thickness(0, 0, 7, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                nameRow.Children.Add(new TextBlock
                {
                    Text = string.Format("{0}팀", t + 1),
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                });
                hdr.Children.Add(nameRow);
                sp.Children.Add(hdr);

                // Members wrap chips
                if (list.Count == 0)
                {
                    sp.Children.Add(new TextBlock
                    {
                        Text = "(배정 인원 없음)",
                        FontSize = 12,
                        Foreground = MainWindow.BrushFromHex("#64748B")
                    });
                }
                else
                {
                    WrapPanel wp = new WrapPanel();
                    for (int m = 0; m < list.Count; m++)
                    {
                        AssignedMemberInfo info = list[m];

                        UIElement chipContent;
                        if (_useTierBalance)
                        {
                            StackPanel chipRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                            string nameLabel = info.IsLeader ? ("👑 " + info.Name) : info.Name;
                            chipRow.Children.Add(new TextBlock
                            {
                                Text = nameLabel,
                                FontSize = 13,
                                FontWeight = info.IsLeader ? FontWeights.Bold : FontWeights.SemiBold,
                                Foreground = info.IsLeader ? MainWindow.BrushFromHex("#FEF08A") : Brushes.White,
                                VerticalAlignment = VerticalAlignment.Center
                            });
                            chipRow.Children.Add(new Border
                            {
                                Background = MainWindow.BrushFromHex(GetTierBgHex(info.Tier)),
                                CornerRadius = new CornerRadius(4),
                                Padding = new Thickness(5, 1, 5, 1),
                                Margin = new Thickness(5, 0, 0, 0),
                                VerticalAlignment = VerticalAlignment.Center,
                                Child = new TextBlock
                                {
                                    Text = string.Format("{0}티어", info.Tier),
                                    FontSize = 10,
                                    FontWeight = FontWeights.Bold,
                                    Foreground = MainWindow.BrushFromHex(GetTierFgHex(info.Tier))
                                }
                            });
                            chipContent = chipRow;
                        }
                        else
                        {
                            string label = info.Name;
                            if (info.IsLeader) label = "👑 " + label;
                            else if (info.IsSeed) label = "★ " + label;

                            chipContent = new TextBlock
                            {
                                Text = label,
                                FontSize = 13,
                                FontWeight = info.IsLeader ? FontWeights.Bold : FontWeights.SemiBold,
                                Foreground = info.IsLeader ? MainWindow.BrushFromHex("#FEF08A") : Brushes.White
                            };
                        }

                        Border chip = new Border
                        {
                            Background = MainWindow.BrushFromHex(info.IsLeader ? "#3B2F0B" : "#0F172A"),
                            BorderBrush = MainWindow.BrushFromHex(info.IsLeader ? "#FBBF24" : "#334155"),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(6),
                            Padding = new Thickness(9, 4, 9, 4),
                            Margin = new Thickness(0, 0, 5, 5),
                            Child = chipContent
                        };
                        wp.Children.Add(chip);
                    }
                    sp.Children.Add(wp);
                }

                tCard.Child = sp;
                _teamsGrid.Children.Add(tCard);
            }
        }

        private string BuildFormattedSummaryText()
        {
            StringBuilder sb = new StringBuilder();
            if (_useTierBalance)
                sb.AppendLine(string.Format("[👥 팀 나누기 결과 - 총 {0}명 / {1}팀 · 등급 밸런스]", _members.Count, _teamCount));
            else
                sb.AppendLine(string.Format("[👥 팀 나누기 결과 - 총 {0}명 / {1}팀]", _members.Count, _teamCount));

            for (int t = 0; t < _currentTeams.Count; t++)
            {
                List<AssignedMemberInfo> list = _currentTeams[t];
                List<string> names = new List<string>();
                for (int m = 0; m < list.Count; m++)
                {
                    string prefix = list[m].IsLeader ? "👑" : "";
                    if (_useTierBalance)
                        names.Add(string.Format("{0}{1}({2}티어)", prefix, list[m].Name, list[m].Tier));
                    else
                        names.Add(prefix + list[m].Name);
                }

                if (_useTierBalance && list.Count > 0)
                {
                    sb.AppendLine(string.Format("• {0}팀 ({1}명 · 평균 {2:0.0}티어): {3}",
                        t + 1,
                        list.Count,
                        GetTeamAverageTier(list),
                        string.Join(", ", names.ToArray())));
                }
                else
                {
                    sb.AppendLine(string.Format("• {0}팀 ({1}명): {2}",
                        t + 1,
                        list.Count,
                        names.Count > 0 ? string.Join(", ", names.ToArray()) : "-"));
                }
            }
            return sb.ToString().TrimEnd();
        }

        private void AddHistoryEntry(int roundNum, string summaryText)
        {
            string captured = summaryText;
            Border item = new Border
            {
                Background = MainWindow.BrushFromHex("#111827"),
                BorderBrush = MainWindow.BrushFromHex("#1E293B"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.Hand
            };
            item.MouseLeftButtonDown += (s, e) =>
            {
                _resultPreviewBox.Text = captured;
                _copyFeedbackText.Text = string.Format("#{0}회차 결과를 불러왔습니다", roundNum);
            };

            item.Child = new TextBlock
            {
                Text = string.Format("#{0}회차 ({1:HH:mm:ss}) — {2}명 / {3}팀 배정",
                    roundNum, DateTime.Now, _members.Count, _teamCount),
                FontSize = 11.5,
                Foreground = MainWindow.BrushFromHex("#CBD5E1"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            _historyStack.Children.Insert(0, item);
        }
    }
}
