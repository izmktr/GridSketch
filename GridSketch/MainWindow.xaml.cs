using Microsoft.Win32;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace GridSketch;
public partial class MainWindow : Window
{
    private SceneDocument document = new();
    private string? path, selectedId;
    private string savedState = "";
    private bool refreshing;
    private bool cellEditMode = true;
    private readonly RecentFiles recentFiles = new();
    private readonly Stack<string> undo = new(), redo = new();

    private GridPaintStroke? paintStroke;
    private SceneObject? paintGrid;
    private Point? dragStart;
    private Point dragPosition;
    private string? dragSnapshot;
    private SceneObject? dragContentOrigin;
    private SceneObject Selected => document.Find(selectedId) ?? document.Canvas;
    private bool Dirty => savedState != document.Snapshot();
    public MainWindow()
    {
        InitializeComponent();
        Layers.PreviewMouseDown += LayerMouseDown;
        Surface.MouseRightButtonDown += GridContextMenu;
        PreviewKeyDown += CancelLineHandle;
        PreviewKeyDown += GuidePreviewKeyDown; PreviewKeyUp += GuidePreviewKeyUp;
        Deactivated += (_, _) => SetCleanPreview(false);
        TextCompositionManager.AddPreviewTextInputStartHandler(GridInput, (_, _) => gridComposing = true);
        GridInput.AddHandler(TextCompositionManager.PreviewTextInputEvent, new TextCompositionEventHandler(GridInputText), true);
        GridInput.AddHandler(TextCompositionManager.TextInputEvent, new TextCompositionEventHandler(GridInputText), true);
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => Save(false), (_, e) => e.CanExecute = activeTab is not null));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (_, _) => OpenClick(this, new RoutedEventArgs())));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.New, (_, _) => NewClick(this, new RoutedEventArgs())));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (_, _) => Undo(), (_, e) => e.CanExecute = undo.Count > 0 && dragStart is null && paintStroke is null));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, (_, _) => Redo(), (_, e) => e.CanExecute = redo.Count > 0 && dragStart is null && paintStroke is null));
        ShowStartScreen(); RefreshRecentFilesMenu(); Loaded += (_, _) => { if (activeTab is not null) CenterClick(this, new RoutedEventArgs()); };
    }
    private void Try(Action action)
    {
        try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "操作を完了できませんでした", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void RememberUndo(string state)
    {
        RecordUndo(state, null);
    }
    private void Change(Action action) => Try(() =>
    {
        CommitPropertyEdit();
        var before = document.Snapshot();
        try { action(); document.PrepareGroupLayouts(); document.ClearMissingStateObjects(); document.EnableParentLayouts(); document.Validate(); }
        catch { document = SceneDocument.Restore(before); RefreshAll(); throw; }
        if (document.Snapshot() != before) RememberUndo(before);
        Surface.Preview = []; Surface.HoveredEdge = null; RefreshAll(); Status.Text = "変更しました。Ctrl+Sで保存できます。";
    });
    private void Undo() { CommitPropertyEdit(); BreakUndoMerge(); if (undo.Count == 0) return; redo.Push(document.Snapshot()); document = SceneDocument.Restore(undo.Pop()); RefreshAll(); }
    private void Redo() { CommitPropertyEdit(); BreakUndoMerge(); if (redo.Count == 0) return; undo.Push(document.Snapshot()); document = SceneDocument.Restore(redo.Pop()); RefreshAll(); }
    private void RefreshAll()
    {
        CommitPropertyEdit(); document.EnableParentLayouts(); refreshing = true; SyncLayerSelection();
        if (Surface.Selected?.Id != Selected.Id) { Surface.ActiveCell = null; Surface.RangeEnd = null; }
        var selectedGrid = document.GridFor(Selected);
        if (!document.Shown(Selected) || selectedGrid is null || Surface.ActiveCell is { } cell && (cell.Column >= selectedGrid.Columns || cell.Row >= selectedGrid.Rows)) { Surface.ActiveCell = null; Surface.RangeEnd = null; }
        if (selectedGrid is not null && Surface.RangeEnd is { } end && (end.Column >= selectedGrid.Columns || end.Row >= selectedGrid.Rows)) Surface.RangeEnd = null;
        Surface.Selected = Selected; Surface.Preview = []; RefreshSurface(); Layers.Items.Clear();
        TreeViewItem Item(SceneObject node)
        {
            var item = new TreeViewItem { Header = node.Label, Tag = node.Id, IsExpanded = node.LayerExpanded };
            item.Expanded += LayerExpansionChanged; item.Collapsed += LayerExpansionChanged;
            AttachLayerMenu(item);
            foreach (var child in node.Children) item.Items.Add(Item(child)); if (node == Selected) item.IsSelected = true; return item;
        }
        Layers.Items.Add(Item(document.Canvas)); refreshing = false; PaintLayerSelection(); BuildProperties(); UpdateTabHeaders();
        Title = $"{(Dirty ? "* " : "")}{(path is null ? "無題" : Path.GetFileName(path))} — GridSketch"; CommandManager.InvalidateRequerySuggested();
    }
    private void LayerSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (refreshing || selectingLayers || e.NewValue is not TreeViewItem item) return;
        if (!Equals(item.Tag, selectedId)) commitLayerNameEdit?.Invoke();
        if (!Equals(item.Tag, selectedId)) { CommitPropertyEdit(); BreakUndoMerge(); }
        layerSelection.Clear(); layerSelection.Add((string)item.Tag); layerAnchor = (string)item.Tag; PaintLayerSelection();
        selectedId = (string)item.Tag; Surface.Selected = Selected; Surface.ActiveCell = null; Surface.RangeEnd = null; Surface.Preview = []; Surface.HoveredEdge = null; Surface.InvalidateVisual(); BuildProperties();
    }
    private void LayerExpansionChanged(object sender, RoutedEventArgs e)
    {
        if (refreshing || !ReferenceEquals(sender, e.OriginalSource) || sender is not TreeViewItem item
            || item.Tag is not string id || document.Find(id) is not { } node) return;
        node.LayerExpanded = item.IsExpanded; UpdateTabHeaders();
        Title = $"{(Dirty ? "* " : "")}{(path is null ? "無題" : Path.GetFileName(path))} — GridSketch";
    }
    private void RefreshSurface()
    {
        foreach (var update in propertySectionSummaries) update();
        double oldX = Surface.OriginX, oldY = Surface.OriginY;
        double scrollX = Viewport.HorizontalOffset, scrollY = Viewport.VerticalOffset;
        Surface.ShowSelectionBounds = !cellEditMode;
        Surface.Refresh(document);
        if (oldX != Surface.OriginX || oldY != Surface.OriginY)
        {
            Viewport.UpdateLayout();
            Viewport.ScrollToHorizontalOffset(scrollX + (Surface.OriginX - oldX) * Zoom.Value);
            Viewport.ScrollToVerticalOffset(scrollY + (Surface.OriginY - oldY) * Zoom.Value);
        }
    }
    private void Label(string text) => Properties.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 2) });
    private void TextField(string key, string value, Action<string> setter, bool multiline = false)
    {
        Label(key); var box = new TextBox { Text = value, AcceptsReturn = multiline, MinHeight = multiline ? 70 : 0, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap };
        Properties.Children.Add(box); LiveText(box, () => setter(box.Text));
    }
    private void Number(string key, double value, Action<double> setter, double? minimum = null, double maximum = 1000)
    {
        Properties.Children.Add(NumericEditor(key, value, out var box, minimum, maximum));
        LiveText(box, () => setter(double.Parse(box.Text, CultureInfo.CurrentCulture)));
    }
    private void ColorField(string key, string value, Action<string> setter)
    {
        Label(key); Properties.Children.Add(ColorEditor(value, out var box)); LiveText(box, () => setter(box.Text));
    }
    private FrameworkElement ColorEditor(string value, out TextBox textBox)
    {
        var row = new DockPanel(); var box = new TextBox { Text = value }; textBox = box;
        var swatch = ColorPicker.Swatch(); swatch.Width = 38; swatch.Height = 24;
        var button = new Button { Content = swatch, Padding = new Thickness(3), Margin = new Thickness(6, 3, 0, 8), ToolTip = "色を選択…" };
        System.Windows.Automation.AutomationProperties.SetName(button, "色を選択");
        DockPanel.SetDock(button, Dock.Right); row.Children.Add(button); row.Children.Add(box);
        void UpdatePreview() { swatch.Child = ColorPicker.TryColor(box.Text, out var color) ? new Border { Background = new SolidColorBrush(color) } : new TextBlock { Text = "?", HorizontalAlignment = HorizontalAlignment.Center }; }
        box.TextChanged += (_, _) => UpdatePreview(); UpdatePreview();
        button.Click += (_, _) =>
        {
            ColorPicker.TryColor(box.Text, out var initial);
            var picker = new ColorPicker(initial) { Owner = Window.GetWindow(button) ?? this };
            if (picker.ShowDialog() == true) box.Text = ColorPicker.Format(picker.SelectedColor);
        };
        return row;
    }
    private void Integer(string key, int value, Action<int> setter) => TextField(key, value.ToString(), s => setter(int.Parse(s, CultureInfo.CurrentCulture)));
    private void Check(string key, bool value, Action<bool> setter)
    {
        var box = new CheckBox { Content = key, IsChecked = value, Margin = new Thickness(0, 6, 0, 6) }; Properties.Children.Add(box);
        box.Click += (_, _) =>
        {
            bool snap = Selected.SnapToCell;
            ImmediateProperty(box, () => setter(box.IsChecked == true));
            if (snap != Selected.SnapToCell) BuildProperties();
        };
    }
    private void Choice(string key, string value, IEnumerable<string> options, Action<string> setter, bool editable = false)
    {
        Label(key); var combo = new ComboBox { ItemsSource = options.ToList(), IsEditable = editable, Text = value }; combo.SelectedItem = value; System.Windows.Automation.AutomationProperties.SetName(combo, key); Properties.Children.Add(combo); LiveChoice(combo, () => setter(combo.IsEditable ? combo.Text : (string)combo.SelectedItem));
    }
    private void FontField(SceneObject node)
    {
        Label("フォント");
        var original = node.Font;
        var current = FontNames.Installed.FirstOrDefault(font => font.Matches(original));
        var combo = new ComboBox { ItemsSource = FontNames.Installed, DisplayMemberPath = nameof(FontName.DisplayName), IsEditable = true };
        combo.SelectedItem = current; combo.Text = current?.DisplayName ?? original;
        Properties.Children.Add(combo);
        LiveChoice(combo, () =>
        {
            var matched = combo.SelectedItem is FontName selected && combo.Text == selected.DisplayName
                ? selected : FontNames.Installed.FirstOrDefault(font => font.Matches(combo.Text));
            node.Font = matched == current && matched is not null ? original : matched?.Source ?? combo.Text;
        });
    }
    private void Button(string label, Action action) { var button = new Button { Content = label }; button.Click += (_, _) => Try(action); Properties.Children.Add(button); }
    private void RequireEditable(SceneObject node) { if (!document.Editable(node)) throw new InvalidOperationException("オブジェクトまたは親がロックされています。"); }
    private void BuildProperties()
    {
        UpdatePropertyHelp();
        CommitPropertyEdit(); propertySectionSummaries.Clear(); Properties.Children.Clear();
        if (SelectedLayers().Count > 1)
        {
            Properties.IsEnabled = SelectedLayers().All(document.Editable);
            VisibilityToggle.Visibility = LockToggle.Visibility = Visibility.Collapsed; PropertyLockNotice.Visibility = Visibility.Collapsed;
            Label($"{SelectedLayers().Count}個のレイヤーを選択中");
            if (GroupSelectionError() is { } error) Label(error);
            Button("グループ化", () => WrapClick(this, new RoutedEventArgs()));
            return;
        }
        var node = Selected; Label(node.Kind == ObjectKind.Border && node.ConnectCellCenters ? "中心罫線" : SceneObject.KindName(node.Kind)); TextField("名前", node.Name, v => node.Name = v);
        UpdatePropertyToggles();

        if (node.Kind == ObjectKind.Text) TextField("文章", node.Text, v => node.Text = v, true);
        var sections = new List<PropertySection>();
        void Section(string title, bool initial = true, Func<string>? summary = null)
            => sections.Add(new(title, Properties.Children.Count, initial, summary));
        bool gridCoordinates = document.UsesGridCoordinates(node);
        if (gridCoordinates)
        {
            Section("マス座標");
            Pair("列（マス座標）", node.GridColumn!.Value, v => node.GridColumn = v,
                "行（マス座標）", node.GridRow!.Value, v => node.GridRow = v);
            Number("回転（度）", node.Angle, v => node.Angle = v);
        }
        Section(gridCoordinates ? "マス内配置" : "配置", true, () => node.LayoutAnchor);
        ObjectLayoutProperties(node);
        Section("見本の配置");
        TemplateProperties(node);
        if (node.Kind == ObjectKind.Canvas)
        {
            Section("キャンバス");
            CanvasSettings(Properties, node, Surface.Renderer!.OutputBounds);
        }
        else if (!gridCoordinates && !node.IsGridContent && !(node.Kind == ObjectKind.Group && node.OrganizeOnly)) { Section("位置・回転"); Pair(document.SelectionOwner(node)?.UseCellLayout == true ? "位置 X（ずらし量）" : "位置 X（px）", node.X, v => node.X = v, document.SelectionOwner(node)?.UseCellLayout == true ? "位置 Y（ずらし量）" : "位置 Y（px）", node.Y, v => node.Y = v); Number("回転（度 / オブジェクト中央）", node.Angle, v => node.Angle = v); }
        Section(node.Kind switch
        {
            ObjectKind.Group => "グループ", ObjectKind.Grid => "グリッド", ObjectKind.Border => "線の見た目",
            ObjectKind.GridText or ObjectKind.Text => "文字", ObjectKind.CellSelection or ObjectKind.EdgeSelection => "状態",
            ObjectKind.Image => "サイズ・配置", ObjectKind.Shape => "図形", ObjectKind.Line => "経路",
            ObjectKind.Fill => "塗り", _ => "設定"
        });
        switch (node.Kind)
        {
            case ObjectKind.Group:
                GroupProperties(node); break;
            case ObjectKind.EdgeSelection:
                EdgeSelectionProperties(node);
                break;
            case ObjectKind.CellSelection:
                CellSelectionProperties(node); break;
            case ObjectKind.Grid:
                Pair("列数", node.Columns, v => node.Columns = (int)v, "行数", node.Rows, v => node.Rows = (int)v, integer: true);
                GridCellSizeProperties(node);
                Pair("マス内余白・左右（px）", node.Margin, v => node.Margin = v, "マス内余白・上下（px）", node.MarginY, v => node.MarginY = v);
                 break;
            case ObjectKind.Border:
                if (node.ConnectCellCenters) CenterDirectionIcons(node);
                ColorField("色", node.Color, v => node.Color = v); Number("太さ（0～64px）", node.Thickness, v => node.Thickness = v, 0, 64); StrokeIconChoice("線種", node.LineStyle, ["実線", "破線", "点線"], v => node.LineStyle = v); StrokeIconChoice("端・角の形", node.EffectiveBorderEndCap, ["中央まで", "四角", "丸"], v => node.BorderEndCap = v, caps: true); Label($"対象：{node.Pattern}"); break;
            case ObjectKind.Fill:
                Choice("塗る範囲", node.UseCellMargin ? "余白の内側" : "マス全体", ["マス全体", "余白の内側"], v => node.UseCellMargin = v == "余白の内側");
                ColorField("塗り色", node.Color, v => node.Color = v);  break;
            case ObjectKind.GridText:
            case ObjectKind.Text:
                FontField(node);
                Number("文字サイズ（px）", node.FontSize, v => node.FontSize = v, 1, 512); ColorField("文字色", node.Color, v => node.Color = v);
                if (node.Kind == ObjectKind.GridText)
                {
                    Section("マス内配置");
                    RegionIcons("基準", node.UseCellMargin ? "余白の内側" : "マス全体", "マス全体", "余白の内側",
                        v => node.UseCellMargin = v == "余白の内側");
                    AlignmentIcons("位置合わせ", node.Alignment, v => node.Alignment = v);
                    Pair("ずらし量 X（px）", node.PlacementOffsetX, v => node.PlacementOffsetX = v,
                        "ずらし量 Y（px）", node.PlacementOffsetY, v => node.PlacementOffsetY = v);
                }
                Section("縁取り", node.Style.OutlineEnabled, () => node.Style.OutlineEnabled ? "有効" : "無効");
                Check("縁取りを有効にする", node.Style.OutlineEnabled, v => node.Style.OutlineEnabled = v);
                ColorField("縁の色・不透明度", node.Style.OutlineColor, v => node.Style.OutlineColor = v);
                Number("縁の太さ（外側 / 0～64px）", node.Style.OutlineWidth, v => node.Style.OutlineWidth = v, 0, 64);
                break;
            case ObjectKind.CellFrame:
                Section("外周線", summary: () => node.RegionOuterStyle);
                ColorField("外周線の色", node.Color, v => { node.RegionInnerColor = node.RegionInnerColor; node.Color = v; });
                StrokeIconChoice("外周線", node.RegionOuterStyle, ["なし", "実線", "破線", "点線"], v => node.RegionOuterStyle = v);
                Number("外周線の太さ（px）", node.Thickness, v => node.Thickness = v, 0, 64);
                Number("外周線の引き込み（px）", node.RegionInset, v => node.RegionInset = v);
                Label("＋で内側、−で外側。0はグリッド罫線上です。");
                Section("内部線", summary: () => node.RegionInnerStyle);
                ColorField("内部線の色", node.RegionInnerColor, v => node.RegionInnerColor = v);
                StrokeIconChoice("内部線", node.RegionInnerStyle, ["なし", "実線", "破線", "点線"], v => node.RegionInnerStyle = v);
                Number("内部線の太さ（px）", node.RegionInnerThickness, v => node.RegionInnerThickness = v, 0, 64);
                Label("空のマスからドラッグして領域を作成。対象マス間の辺をクリック・ドラッグすると結合を切り替えます。");
                 break;
            case ObjectKind.Image:
                if (node.SnapToCell) { Pair("列（1から）", node.Column + 1, v => node.Column = (int)v - 1, "行（1から）", node.Row + 1, v => node.Row = (int)v - 1, integer: true); }
                else ImageSizeProperties(node);
                break;
            case ObjectKind.Shape:
                Choice("形", node.Shape, ["四角", "角丸", "楕円"], v => node.Shape = v); Pair("幅（px）", node.Width, v => node.Width = v, "高さ（px）", node.Height, v => node.Height = v, 1, 1000);
                ColorField("塗り色", node.FillColor, v => node.FillColor = v); ColorField("線の色", node.Color, v => node.Color = v); Number("線の太さ", node.Thickness, v => node.Thickness = v, 0, 64); break;
            case ObjectKind.Line:
                Choice("経路", node.PathKind, ["直線", "折れ線", "曲線"], v => node.PathKind = v);
                Pair("終点 X（始点から）", node.EndX, v => node.EndX = v, "終点 Y（始点から）", node.EndY, v => node.EndY = v);
                Pair("制御点 X（始点から）", node.ControlX, v => node.ControlX = v, "制御点 Y（始点から）", node.ControlY, v => node.ControlY = v);
                Section("矢印");
                Choice("始点の矢印", node.StartArrow, ["なし", "三角", "開いた矢印"], v => node.StartArrow = v); Choice("終点の矢印", node.EndArrow, ["なし", "三角", "開いた矢印"], v => node.EndArrow = v);
                Number("矢印の大きさ", node.ArrowSize, v => node.ArrowSize = v, 1, 200); Section("線の見た目"); ColorField("線の色", node.Color, v => node.Color = v); Number("線の太さ", node.Thickness, v => node.Thickness = v, 0, 64); StrokeIconChoice("線種", node.LineStyle, ["実線", "破線", "点線"], v => node.LineStyle = v); break;
        }
        Section("範囲の操作");
        if (node.Kind == ObjectKind.Border) BorderTools(node);
        if (node.Kind == ObjectKind.GridText) TextTools(node);
        if (node.Kind == ObjectKind.Fill) Button("選択範囲の塗りを削除", () => Change(() => { RequireEditable(node); foreach (var cell in CellRange(node)) node.FilledCells.Remove(cell); }));
        if (node.Kind == ObjectKind.CellFrame)
        {
            Button("選択範囲を結合して領域を作成", () => Change(() => { RequireEditable(node); document.JoinRegionCells(node, CellRange(node)); }));
            Button("選択範囲の結合を解除", () => Change(() => { RequireEditable(node); var cells = CellRange(node).ToHashSet(); node.RegionLinks.RemoveWhere(e => { var (a, b) = SceneDocument.RegionNeighbors(e); return cells.Contains(a) && cells.Contains(b); }); }));
            Button("選択範囲の領域を削除", () => Change(() => { RequireEditable(node); document.SetFrameCells(node, CellRange(node), false); }));
        }
        if (node.Kind == ObjectKind.CellSelection) Button("選択範囲を空にする", () => Change(() => { RequireEditable(node); var cells = CellRange(node).ToHashSet(); node.StateCells.RemoveAll(c => cells.Contains(new(c.Column, c.Row))); }));
        if (node.Kind == ObjectKind.Image) Button("画像を読み込み / 置換…", () => ReplaceImage(node));
        if (node != document.Canvas)
        {
            Section("レイヤーの操作");
            Button("親を変更…", () => ChooseParent(node));
            ((Button)Properties.Children[Properties.Children.Count - 1]).IsEnabled = document.Editable(node);
        }

        WrapPropertySections(node, sections);
    }
    private IEnumerable<SceneObject> Descendants(SceneObject node) => node.Children.SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private IEnumerable<CellPosition> CellRange(SceneObject node)
    {
        var grid = document.GridFor(node) ?? throw new InvalidOperationException("グリッドを選択してください。");
        var start = Surface.ActiveCell ?? new CellPosition(0, 0); var end = Surface.RangeEnd ?? (Surface.ActiveCell ?? new CellPosition(grid.Columns - 1, grid.Rows - 1));
        for (int r = Math.Min(start.Row, end.Row); r <= Math.Max(start.Row, end.Row); r++) for (int c = Math.Min(start.Column, end.Column); c <= Math.Max(start.Column, end.Column); c++) yield return new(c, r);
    }
    private void BorderTools(SceneObject node)
    {
        Label("辺の一括設定");
        var preview = new Canvas { Width = 190, Height = 145, Margin = new Thickness(0, 8, 0, 8) };
        var labels = new[] { "上辺", "左辺", "内側の縦", "右辺", "内側の横", "下辺" };
        var positions = new[] { new Rect(20, 10, 140, 14), new Rect(10, 24, 14, 96), new Rect(83, 24, 14, 96), new Rect(156, 24, 14, 96), new Rect(24, 65, 132, 14), new Rect(20, 120, 140, 14) };
        var toggles = new List<System.Windows.Controls.Primitives.ToggleButton>();
        for (int i = 0; i < labels.Length; i++)
        {
            var rect = positions[i]; var check = new System.Windows.Controls.Primitives.ToggleButton { ToolTip = labels[i], IsChecked = true, Width = rect.Width, Height = rect.Height };
            System.Windows.Automation.AutomationProperties.SetName(check, labels[i]); Canvas.SetLeft(check, rect.X); Canvas.SetTop(check, rect.Y); preview.Children.Add(check); toggles.Add(check);
        }
        Properties.Children.Add(preview);
        var downDiagonal = new CheckBox { Content = "斜め ↘", IsChecked = true, Margin = new Thickness(0, 0, 12, 0) };
        var upDiagonal = new CheckBox { Content = "斜め ↗", IsChecked = true };
        if (node.ConnectCellCenters)
        {
            var diagonals = new WrapPanel(); diagonals.Children.Add(downDiagonal); diagonals.Children.Add(upDiagonal);
            Properties.Children.Add(diagonals);
            Label("選んだ接続方向の線を追加・削除します。");
        }
        var presets = new WrapPanel();
        foreach (var name in new[] { "外枠", "内側", "すべて", "なし" })
        {
            var b = new Button { Content = name }; b.Click += (_, _) => Change(() => { RequireEditable(node); node.Pattern = name == "なし" ? "個別" : name; node.Edges.Clear(); }); presets.Children.Add(b);
        }
        Properties.Children.Add(presets);
        void Apply(bool remove) => Change(() =>
        {
            RequireEditable(node); var grid = document.GridFor(node)!; var cells = CellRange(node).ToList();
            int left = cells.Min(c => c.Column), right = cells.Max(c => c.Column) + 1, top = cells.Min(c => c.Row), bottom = cells.Max(c => c.Row) + 1; var edges = new HashSet<GridEdge>();
            if (node.ConnectCellCenters) { right--; bottom--; }
            for (int c = left; c < right; c++)
            {
                if (toggles[0].IsChecked == true) edges.Add(new(c, top, false)); if (toggles[5].IsChecked == true) edges.Add(new(c, bottom, false));
                if (toggles[4].IsChecked == true) for (int r = top + 1; r < bottom; r++) edges.Add(new(c, r, false));
            }
            for (int r = top; r < bottom; r++)
            {
                if (toggles[1].IsChecked == true) edges.Add(new(left, r, true)); if (toggles[3].IsChecked == true) edges.Add(new(right, r, true));
                if (toggles[2].IsChecked == true) for (int c = left + 1; c < right; c++) edges.Add(new(c, r, true));
            }
            if (node.ConnectCellCenters && node.CenterDirections != "縦横")
                for (int c = left; c < right; c++) for (int r = top; r < bottom; r++)
                {
                    if (downDiagonal.IsChecked == true) edges.Add(new(c, r, false) { Diagonal = 1 });
                    if (upDiagonal.IsChecked == true) edges.Add(new(c, r, false) { Diagonal = -1 });
                }
            edges.RemoveWhere(e => !SceneDocument.BorderEdgeInside(node, grid, e) || !SceneDocument.BorderDirectionAllowed(node, e));
            node.Edges = SceneDocument.BorderEdges(node, grid).ToHashSet(); node.Pattern = "個別"; if (remove) node.Edges.ExceptWith(edges); else node.Edges.UnionWith(edges);
        });
        Button("チェックした辺を追加", () => Apply(false)); Button("チェックした辺を削除", () => Apply(true));
    }
    private void TextTools(SceneObject node)
    {
        Button("個別書式を共通フォント・サイズへ統一", () => Change(() => { RequireEditable(node); foreach (var g in node.Glyphs) { g.Font = null; g.Size = null; } }));
    }
    private SceneObject AddParent(bool gridRequired)
    {
        var node = Selected;
        if (gridRequired) return document.GridFor(node) ?? throw new InvalidOperationException("グリッドまたはグリッド直下のレイヤーを選択してください。");
        return node.IsContainer ? node : document.Parent(node) ?? document.Canvas;
    }
    private (SceneObject Parent, int Index) AddPlacement(bool gridRequired)
    {
        var selected = Selected;
        bool asChild = selected == document.Canvas ||
            (selected.IsContainer && (selected.LayerExpanded || selected.Children.Count == 0));
        var parent = asChild ? selected : document.Parent(selected)!;
        if (gridRequired && document.GridContainer(parent) is null)
            throw new InvalidOperationException("このオブジェクトはグリッドの子として追加します。グリッドを開いて選択するか、グリッド直下のレイヤーを選択してください。");
        return (parent, asChild ? parent.Children.Count : parent.Children.IndexOf(selected) + 1);
    }
    private void InsertAddedObject(SceneObject parent, int index, SceneObject node)
    {
        parent.Children.Insert(index, node);
        parent.LayerExpanded = true;
        selectedId = node.Id;
    }
    private void AddClick(object sender, RoutedEventArgs e) => Try(() =>
    {
        if (sender is not MenuItem { Tag: string tag }) return;
        bool independent = tag == "IndependentGrid", centerBorder = tag == "CenterBorder";
        if (!Enum.TryParse<ObjectKind>(independent ? "Grid" : centerBorder ? "Border" : tag, out var kind)) return;
        if (kind == ObjectKind.Grid)
        {
            var grid = GridDialog(false, independent ? "独立マス" : "基本の方眼紙"); if (grid is null) return;
            Change(() => { var (parent, index) = AddPlacement(false); RequireEditable(parent); InsertAddedObject(parent, index, grid); }); return;
        }
        byte[]? image = kind == ObjectKind.Image ? ReadImage() : null; if (kind == ObjectKind.Image && image is null) return;
        Change(() =>
        {
            var (parent, index) = AddPlacement(kind is ObjectKind.Border or ObjectKind.GridText or ObjectKind.Fill or ObjectKind.CellFrame or ObjectKind.CellSelection or ObjectKind.EdgeSelection); RequireEditable(parent);
            var node = new SceneObject { Kind = kind, Name = centerBorder ? "中心罫線" : SceneObject.KindName(kind), ConnectCellCenters = centerBorder, UseCellLayout = kind is ObjectKind.CellSelection or ObjectKind.EdgeSelection, UseObjectLayout = kind is ObjectKind.CellSelection or ObjectKind.EdgeSelection, ImageData = image };
            InitializeObjectLayout(node, parent);
            if (kind == ObjectKind.Image && parent.Kind == ObjectKind.Grid && !node.GridColumn.HasValue) { node.SnapToCell = true; node.Column = Surface.ActiveCell?.Column ?? 0; node.Row = Surface.ActiveCell?.Row ?? 0; }
            if (kind == ObjectKind.Fill) node.Color = "#000000";
            InsertAddedObject(parent, index, node);
            if (parent.IsStateSelector)
            {
                var baseName = node.Name; int suffix = 1;
                while (parent.Children.Any(c => c != node && c.Name == node.Name)) node.Name = $"{baseName} {++suffix}";
                SceneDocument.AssignNewState(parent, node);
            }
            selectedId = node.Id;
        });
    });
    private byte[]? ReadImage()
    {
        var dialog = new OpenFileDialog { Filter = "画像|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" }; if (dialog.ShowDialog(this) != true) return null;
        if (new FileInfo(dialog.FileName).Length > 20_000_000) throw new InvalidOperationException("画像は20MB以下にしてください。");
        var bytes = File.ReadAllBytes(dialog.FileName); _ = Document.DecodeImage(bytes); return bytes;
    }
    private void ReplaceImage(SceneObject node) { RequireEditable(node); var data = ReadImage(); if (data is not null) Change(() => node.ImageData = data); }
    private SceneObject? GridDialog(bool isNew, string initialPreset = "基本の方眼紙")
    {
        var dialog = new Window { Owner = this, Title = isNew ? "新規キャンバスとグリッド" : "グリッドを追加", Width = 390, Height = isNew ? 760 : 540, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20) }; dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        TextBox Field(string title, string value) { panel.Children.Add(new TextBlock { Text = title }); var box = new TextBox { Text = value }; panel.Children.Add(box); return box; }
        var canvasDraft = new SceneObject { Kind = ObjectKind.Canvas, Name = "キャンバス", Width = 800, Height = 600 };
        Action? applyCanvas = isNew ? CanvasSettings(panel, canvasDraft, new Rect(0, 0, 800, 600)) : null;
        panel.Children.Add(new TextBlock { Text = "プリセット" });
        var options = new List<string> { "基本の方眼紙", "クロスワード", "将棋盤", "空のグリッド", "独立マス" }; if (isNew) options.Add("キャンバスのみ");
        var preset = new ComboBox { ItemsSource = options, SelectedItem = initialPreset }; panel.Children.Add(preset);
        var cols = Field("列数", "16"); var rows = Field("行数", "12"); var size = Field("マスの大きさ（px）", "48");
        preset.SelectionChanged += (_, _) => { if (preset.SelectedIndex == 2) cols.Text = rows.Text = "9"; };
        var button = new Button { Content = "作成" }; panel.Children.Add(button); SceneObject? result = null;
        button.Click += (_, _) => Try(() =>
        {
            var test = new SceneDocument();
            if (preset.Text != "キャンバスのみ") test.Canvas.Children.Add(SceneDocument.CreateGrid(preset.Text, int.Parse(cols.Text), int.Parse(rows.Text), double.Parse(size.Text)));
            if (isNew)
            {
                applyCanvas!(); canvasDraft.Children = test.Canvas.Children; test.Canvas = canvasDraft;
            }
            test.Validate(); result = isNew ? test.Canvas : test.Canvas.Children[0]; dialog.DialogResult = true;
        });
        return dialog.ShowDialog() == true ? result : null;
    }
    private void LayerUp(object sender, RoutedEventArgs e) => MoveLayer(-1);
    private void LayerDown(object sender, RoutedEventArgs e) => MoveLayer(1);
    private void MoveLayer(int offset) => Change(() =>
    {
        RequireSingleLayer(); var node = Selected; RequireEditable(node); var parent = document.Parent(node); if (parent is null) return;
        int index = parent.Children.IndexOf(node), next = index + offset; if (next < 0 || next >= parent.Children.Count) return; parent.Children.RemoveAt(index); parent.Children.Insert(next, node);
    });
    private void DeleteClick(object sender, RoutedEventArgs e) => Try(() =>
    {
        RequireSingleLayer(); var node = Selected; if (node == document.Canvas) return; RequireEditable(node);
        if (node.Children.Count > 0 && MessageBox.Show(this, "子オブジェクトも削除しますか？", "削除", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        Change(() => { var parent = document.Parent(node)!; parent.Children.Remove(node); selectedId = parent.Id; Surface.ActiveCell = null; Surface.RangeEnd = null; });
    });
    private void WrapClick(object sender, RoutedEventArgs e) => Change(() =>
    {
        if (GroupSelectionError() is { } error) throw new InvalidOperationException(error);
        var nodes = SelectedLayers(); var parent = document.Parent(nodes[0])!;
        nodes = parent.Children.Where(n => nodes.Contains(n)).ToList();
        int index = parent.Children.IndexOf(nodes[0]);
        var group = new SceneObject { Kind = ObjectKind.Group, Name = "グループ" };
        InitializeWrappingFrame(group, nodes.FirstOrDefault(n => n.UseParentLayout) ?? nodes[0]);
        parent.Children.Insert(index, group);
        document.AdoptGridCoordinates(group);
        foreach (var node in nodes) document.Reparent(node, group);
        group.Children = nodes;
        if (parent.IsStateSelector)
            foreach (var state in parent.States.Where(s => s.ObjectId == nodes[0].Id)) state.ObjectId = group.Id;
        layerSelection.Clear(); layerSelection.Add(group.Id); selectedId = group.Id;
    });
    private void UngroupClick(object sender, RoutedEventArgs e) => Change(() =>
    {
        RequireSingleLayer(); var node = Selected; RequireEditable(node); if (node.Kind != ObjectKind.Group) throw new InvalidOperationException("通常のグループを選択してください。");
        var parent = document.Parent(node)!;
        if (parent.IsStateSelector) throw new InvalidOperationException("見本グループは解除せず、子を個別に移動して割り当て直してください。");
        var children = node.Children.ToList(); int index = parent.Children.IndexOf(node);
        foreach (var child in children) document.Reparent(child, parent); foreach (var child in children) parent.Children.Remove(child); parent.Children.Remove(node); parent.Children.InsertRange(index, children); selectedId = parent.Id;
    });
    private void ModeChanged(object sender, RoutedEventArgs e)
    {
        FinishDrag(); Surface.ReleaseMouseCapture();
        cellEditMode = ReferenceEquals(sender, CellModeButton);
        MoveModeButton.IsChecked = !cellEditMode; CellModeButton.IsChecked = cellEditMode;
        Surface.ShowSelectionBounds = !cellEditMode;
        Surface.HoveredEdge = null; Surface.Preview = [];
        if (!cellEditMode) GridInput.Visibility = Visibility.Collapsed;
        Surface.InvalidateVisual();
        Status.Text = cellEditMode ? "マス・辺をクリック・ドラッグして編集します。" : "オブジェクトを選択・ドラッグして移動します。";
    }
    private void SurfaceDown(object sender, MouseButtonEventArgs e) => Try(() =>
    {
        if (Surface.CleanPreview) { e.Handled = true; return; }
        var p = e.GetPosition(Surface); var node = Selected;
        if (cellEditMode)
        {
            if (!document.Shown(node)) { Status.Text = "表示してからマス・辺を編集してください。"; return; }
            RequireEditable(node); var grid = document.GridFor(node); if (grid is null) { Status.Text = "グリッド直下のレイヤーを選択してください。"; return; }
            var cell = Surface.CellAt(grid, p);
            if (node.Kind == ObjectKind.CellFrame && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                && Surface.EdgeAt(grid, p, Zoom.Value) is { } regionEdge)
            {
                var (first, second) = SceneDocument.RegionNeighbors(regionEdge);
                var cells = SceneDocument.FramedCells(node, grid).ToHashSet();
                if (cells.Contains(first) && cells.Contains(second))
                { StartPaint(node, grid, p, regionEdge); e.Handled = true; return; }
            }
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && cell is { } rangeEnd && node.Kind is not (ObjectKind.Line or ObjectKind.EdgeSelection)) { Surface.ActiveCell ??= rangeEnd; Surface.RangeEnd = rangeEnd; Surface.InvalidateVisual(); return; }
            if (node.Kind is ObjectKind.Border or ObjectKind.EdgeSelection && Surface.EdgeAt(grid, p, Zoom.Value) is { } edge) { StartPaint(node, grid, p, edge); e.Handled = true; return; }
            if (node.Kind == ObjectKind.EdgeSelection || cell is not { } c) return;
            if (node.Kind == ObjectKind.GridText) { SelectTextCell(c); e.Handled = true; return; }
            Surface.ActiveCell = c; Surface.RangeEnd = null; Surface.Preview = [];
            if (node.Kind is ObjectKind.Fill or ObjectKind.CellFrame or ObjectKind.CellSelection) { StartPaint(node, grid, p); e.Handled = true; return; }
            else if (node.Kind == ObjectKind.Image && node.SnapToCell) Change(() => { node.Column = c.Column; node.Row = c.Row; });
            else if (node.Kind == ObjectKind.Line) Change(() =>
            {
                var center = new Point((c.Column + 0.5) * grid.CellSize, (c.Row + 0.5) * grid.CellHeight);
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { var inverse = document.EffectiveLocalMatrix(node); inverse.Invert(); var end = inverse.Transform(center); node.EndX = end.X; node.EndY = end.Y; }
                else if (document.UsesGridCoordinates(node)) { node.GridColumn = c.Column + 1; node.GridRow = c.Row + 1; }
                else { node.X = center.X; node.Y = center.Y; }
            });
            Surface.InvalidateVisual(); Status.Text = $"{c.Column + 1}列・{c.Row + 1}行を選択。Shift+クリックで範囲選択。"; return;
        }
        if (StartLineHandle(p)) { e.Handled = true; return; }
        var world = Surface.ToWorld(p);
        if (node == document.Canvas || !document.Shown(node) || !Surface.Renderer!.Bounds.TryGetValue(node.Id, out var selectedBounds) || !selectedBounds.Contains(world))
        {
            SceneObject? Hit(SceneObject root)
            {
                foreach (var child in root.Children)
                {
                    if (!document.Shown(child)) continue; var found = Hit(child); if (found is not null) return found;
                    if (Surface.Renderer!.Bounds.TryGetValue(child.Id, out var b) && b.Contains(world)) return child;
                }
                return null;
            }
            var hit = Hit(document.Canvas); if (hit is null) return; selectedId = hit.Id; RefreshAll(); node = Selected;
        }
        if (node == document.Canvas || node.Kind == ObjectKind.Group && node.OrganizeOnly || !document.Editable(node)) return;
        dragSnapshot = document.Snapshot(); dragContentOrigin = SceneDocument.MovesCellContents(node) ? SceneDocument.Restore(dragSnapshot).Find(node.Id) : null;
        dragStart = world; dragPosition = document.UsesGridCoordinates(node)
            ? new Point(node.GridColumn!.Value, node.GridRow!.Value) : new Point(node.X, node.Y); Surface.CaptureMouse();
    });
    private void SurfaceMove(object sender, MouseEventArgs e)
    {
        var node = Selected; var p = e.GetPosition(Surface);
        if (paintStroke is not null)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { FinishDrag(); Surface.ReleaseMouseCapture(); return; }
            paintStroke.Move(Surface.EditLocal(paintGrid!, p)); RefreshSurface(); return;
        }
        if (dragStart is { } start)
        {
            if (lineHandleDrag is not null) { lineHandleDrag.Move(Surface.ToWorld(p) - start, Zoom.Value); RefreshSurface(); return; }
            var current = Surface.ToWorld(p); var parent = document.Parent(node)!; var inverse = document.World(parent); inverse.Invert(); var delta = inverse.Transform(current) - inverse.Transform(start);
            document.MoveOnGrid(node, dragPosition, delta, dragContentOrigin, Zoom.Value); RefreshSurface(); return;
        }
        var handle = cellEditMode ? null : Surface.LineHandleAt(p);
        if (handle != Surface.HoveredLineHandle) { Surface.HoveredLineHandle = handle; Surface.Cursor = handle is null ? null : Cursors.SizeAll; Surface.InvalidateVisual(); }
        var grid = document.GridFor(node); var edge = cellEditMode && node.Kind is ObjectKind.Border or ObjectKind.EdgeSelection or ObjectKind.CellFrame && grid is not null && document.Shown(node) ? Surface.EdgeAt(grid, p, Zoom.Value) : null;
        if (node.Kind == ObjectKind.CellFrame && edge is { } regionEdge)
        {
            var (first, second) = SceneDocument.RegionNeighbors(regionEdge);
            var cells = SceneDocument.FramedCells(node, grid!).ToHashSet();
            if (!cells.Contains(first) || !cells.Contains(second)) edge = null;
        }
        var hoverCell = cellEditMode && node.Kind == ObjectKind.GridText && grid is not null ? Surface.CellAt(grid, p) : null;
        if (hoverCell != Surface.HoveredCell) { Surface.HoveredCell = hoverCell; Surface.InvalidateVisual(); }
        if (edge != Surface.HoveredEdge) { Surface.HoveredEdge = edge; Surface.InvalidateVisual(); }
    }
    private void StartPaint(SceneObject node, SceneObject grid, Point point, GridEdge? edge = null)
    {
        if (node.IsStateSelector && node.States.Count == 0) { Status.Text = "子に画像などを追加するか、プロパティで状態を追加してください。"; return; }
        dragSnapshot = document.Snapshot(); paintGrid = grid;
        paintStroke = new GridPaintStroke(document, node, grid, Surface.EditLocal(grid, point), edge);
        Surface.Preview = []; Surface.RangeEnd = null; Surface.HoveredEdge = null;
        Surface.CaptureMouse(); RefreshSurface();
        Status.Text = "ドラッグで連続編集。開始時の追加／削除を維持します。";
    }
    private void FinishDrag()
    {
        string? mergeKey = lineHandleDrag is not null ? "line-handle:" + Selected.Id + ":" + lineHandleDrag.Handle : dragStart is not null ? "move:" + Selected.Id : null;
        if (dragStart is null && paintStroke is null) return; dragStart = null; paintStroke = null; paintGrid = null;
        if (dragSnapshot is { } snapshot) { try { document.EnableParentLayouts(); document.Validate(); if (snapshot != document.Snapshot()) RecordUndo(snapshot, mergeKey); } catch { document = SceneDocument.Restore(snapshot); BreakUndoMerge(); } }
        dragSnapshot = null; dragContentOrigin = null; lineHandleDrag = null; Surface.Cursor = null; RefreshAll();
    }
    private void SurfaceUp(object sender, MouseButtonEventArgs e) { if (paintStroke is not null) paintStroke.Move(Surface.EditLocal(paintGrid!, e.GetPosition(Surface))); FinishDrag(); Surface.ReleaseMouseCapture(); }
    private void SurfaceLostCapture(object sender, MouseEventArgs e) => FinishDrag();
    private void SurfaceLeave(object sender, MouseEventArgs e) { Surface.HoveredCell = null; Surface.HoveredEdge = null; if (lineHandleDrag is null) { Surface.HoveredLineHandle = null; Surface.Cursor = null; } Surface.InvalidateVisual(); }
    private void ZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Surface is null || Viewport is null) return;
        Viewport.UpdateLayout();
        double previous = Surface.LayoutTransform is ScaleTransform transform ? transform.ScaleX : 1;
        var center = new Point((Viewport.HorizontalOffset + Viewport.ViewportWidth / 2) / previous - Surface.OriginX,
            (Viewport.VerticalOffset + Viewport.ViewportHeight / 2) / previous - Surface.OriginY);
        if (Surface.Renderer is not null)
            Surface.EnsureZoomSpace(center, new Size(Viewport.ViewportWidth / e.NewValue, Viewport.ViewportHeight / e.NewValue));
        Surface.LayoutTransform = new ScaleTransform(e.NewValue, e.NewValue);
        Viewport.UpdateLayout();
        Viewport.ScrollToHorizontalOffset((center.X + Surface.OriginX) * e.NewValue - Viewport.ViewportWidth / 2);
        Viewport.ScrollToVerticalOffset((center.Y + Surface.OriginY) * e.NewValue - Viewport.ViewportHeight / 2);
        Viewport.UpdateLayout();
    }
    private void CenterClick(object sender, RoutedEventArgs e)
    {
        var bounds = Surface.Renderer!.OutputBounds; Viewport.ScrollToHorizontalOffset(Math.Max(0, (Surface.OriginX + bounds.X) * Zoom.Value - 40)); Viewport.ScrollToVerticalOffset(Math.Max(0, (Surface.OriginY + bounds.Y) * Zoom.Value - 40));
    }
    private bool CanDiscard()
    {
        CommitPropertyEdit();
        FinishDrag(); Surface.ReleaseMouseCapture();
        if (!Dirty) return true; var answer = MessageBox.Show(this, activeTab?.Owner is not null ? "部品の変更をライブラリへ反映しますか？" : $"「{System.IO.Path.GetFileName(path ?? "無題") }」の変更を保存しますか？", "GridSketch", MessageBoxButton.YesNoCancel, MessageBoxImage.Question); return answer == MessageBoxResult.No || answer == MessageBoxResult.Yes && Save(false);
    }
    private bool Save(bool saveAs)
    {
        if (activeTab is null) return false;
        CommitPropertyEdit();
        FinishDrag(); Surface.ReleaseMouseCapture();
        if (!saveAs && activeTab?.Owner is not null) return ApplyPartEdit();
        string? destination = path;
        if (saveAs || destination is null)
        {
            var dialog = new SaveFileDialog { Filter = "GridSketchプロジェクト|*.gridsketch", DefaultExt = ".gridsketch", FileName = Path.GetFileName(path ?? "無題.gridsketch") };
            if (dialog.ShowDialog(this) != true) return false; destination = dialog.FileName;
        }
        if (editorTabs.Any(t => t != activeTab && t.Path is not null && string.Equals(System.IO.Path.GetFullPath(t.Path), System.IO.Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))) { MessageBox.Show(this, "この保存先は別のタブで開いています。別の名前を指定してください。"); return false; }
        bool saved = false; Try(() => { document.Save(destination); if (activeTab?.Owner is null) { path = destination; savedState = document.Snapshot(); } saved = true; RefreshAll(); Status.Text = "プロジェクトを保存しました。"; RememberFile(destination); }); return saved;
    }
    private void SaveClick(object sender, RoutedEventArgs e) => Save(false);
    private void SaveAsClick(object sender, RoutedEventArgs e) => Save(true);
    private void ExitClick(object sender, RoutedEventArgs e) => Close();
    private void NewClick(object sender, RoutedEventArgs e) => Try(() =>
    {
        var grid = GridDialog(true); if (grid is null) return;
        AddProjectTab(new SceneDocument { Canvas = grid });
    });
    private void OpenClick(object sender, RoutedEventArgs e) => Try(() =>
    {
        var dialog = new OpenFileDialog { Filter = "GridSketchプロジェクト|*.gridsketch" }; if (dialog.ShowDialog(this) == true) OpenProject(dialog.FileName);
    });
    private void OpenProject(string filePath)
    {
        CaptureTab();
        var fullPath = System.IO.Path.GetFullPath(filePath);
        var existing = editorTabs.FirstOrDefault(t => t.Owner is null && t.Path is not null && string.Equals(System.IO.Path.GetFullPath(t.Path), fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) { SwitchTab(existing); return; }
        var loaded = SceneDocument.Load(fullPath); AddProjectTab(loaded, fullPath);
        Status.Text = "プロジェクトを新しいタブで開きました。"; RememberFile(fullPath);
    }
    private void RememberFile(string filePath) { if (!recentFiles.Remember(filePath)) Status.Text += " 履歴を保存できませんでした。"; RefreshRecentFilesMenu(); }
    private void RefreshRecentFilesMenu()
    {
        RefreshStartRecent();
        RecentFilesMenu.Items.Clear(); if (recentFiles.Entries.Count == 0) { RecentFilesMenu.Items.Add(new MenuItem { Header = "履歴はありません", IsEnabled = false }); return; }
        foreach (var filePath in recentFiles.Entries)
        {
            var item = new MenuItem { Header = new TextBlock { Text = filePath }, ToolTip = filePath };
            item.Click += (_, _) => Try(() =>
            {
                try { OpenProject(filePath); }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { recentFiles.Remove(filePath); RefreshRecentFilesMenu(); throw new FileNotFoundException($"ファイルが見つかりません。履歴から削除しました。\n{filePath}", ex); }
            }); RecentFilesMenu.Items.Add(item);
        }
        RecentFilesMenu.Items.Add(new Separator()); var clear = new MenuItem { Header = "履歴をクリア(_C)" }; clear.Click += (_, _) => { Status.Text = recentFiles.Clear() ? "履歴をクリアしました。" : "履歴を保存できませんでした。"; RefreshRecentFilesMenu(); }; RecentFilesMenu.Items.Add(clear);
    }
    private void ExportClick(object sender, RoutedEventArgs e) => Try(() =>
    {
        var dialog = new SaveFileDialog { Filter = "PNG画像|*.png", DefaultExt = ".png", FileName = Path.GetFileNameWithoutExtension(path ?? "無題") + ".png" }; if (dialog.ShowDialog(this) != true) return;
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(new SceneRenderer(document).Export(1))); using var stream = new MemoryStream(); encoder.Save(stream); File.WriteAllBytes(dialog.FileName, stream.ToArray()); Status.Text = "PNGを書き出しました。";
    });
    private void WindowClosing(object? sender, CancelEventArgs e) => e.Cancel = !CanCloseAllTabs();
}


