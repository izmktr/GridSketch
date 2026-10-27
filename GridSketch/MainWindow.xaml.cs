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
    private readonly List<Action> setters = [];
    private GridPaintStroke? paintStroke;
    private SceneObject? paintGrid;
    private Point? dragStart;
    private Point dragPosition;
    private string? dragSnapshot;
    private SceneObject Selected => document.Find(selectedId) ?? document.Canvas;
    private bool Dirty => savedState != document.Snapshot();
    public MainWindow()
    {
        InitializeComponent();
        TextCompositionManager.AddPreviewTextInputStartHandler(GridInput, (_, _) => gridComposing = true);
        GridInput.AddHandler(TextCompositionManager.PreviewTextInputEvent, new TextCompositionEventHandler(GridInputText), true);
        GridInput.AddHandler(TextCompositionManager.TextInputEvent, new TextCompositionEventHandler(GridInputText), true);
        document.Canvas.Children.Add(SceneDocument.CreateGrid("基本の方眼紙"));
        selectedId = document.Canvas.Id; savedState = document.Snapshot();
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => Save(false)));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (_, _) => OpenClick(this, new RoutedEventArgs())));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.New, (_, _) => NewClick(this, new RoutedEventArgs())));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (_, _) => Undo(), (_, e) => e.CanExecute = undo.Count > 0 && dragStart is null && paintStroke is null));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, (_, _) => Redo(), (_, e) => e.CanExecute = redo.Count > 0 && dragStart is null && paintStroke is null));
        RefreshAll(); RefreshRecentFilesMenu(); Loaded += (_, _) => CenterClick(this, new RoutedEventArgs());
    }
    private void Try(Action action)
    {
        try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "操作を完了できませんでした", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void RememberUndo(string state)
    {
        undo.Push(state);
        if (undo.Count > 30) { var keep = undo.Take(30).Reverse().ToList(); undo.Clear(); foreach (var item in keep) undo.Push(item); }
        redo.Clear();
    }
    private void Change(Action action) => Try(() =>
    {
        var before = document.Snapshot();
        try { action(); document.ClearMissingStateObjects(); document.Validate(); }
        catch { document = SceneDocument.Restore(before); RefreshAll(); throw; }
        if (document.Snapshot() != before) RememberUndo(before);
        Surface.Preview = []; Surface.HoveredEdge = null; RefreshAll(); Status.Text = "変更しました。Ctrl+Sで保存できます。";
    });
    private void Undo() { if (undo.Count == 0) return; redo.Push(document.Snapshot()); document = SceneDocument.Restore(undo.Pop()); RefreshAll(); }
    private void Redo() { if (redo.Count == 0) return; undo.Push(document.Snapshot()); document = SceneDocument.Restore(redo.Pop()); RefreshAll(); }
    private void RefreshAll()
    {
        refreshing = true;
        if (Surface.Selected?.Id != Selected.Id) { Surface.ActiveCell = null; Surface.RangeEnd = null; }
        var selectedGrid = document.GridFor(Selected);
        if (selectedGrid is null || Surface.ActiveCell is { } cell && (cell.Column >= selectedGrid.Columns || cell.Row >= selectedGrid.Rows)) { Surface.ActiveCell = null; Surface.RangeEnd = null; }
        if (selectedGrid is not null && Surface.RangeEnd is { } end && (end.Column >= selectedGrid.Columns || end.Row >= selectedGrid.Rows)) Surface.RangeEnd = null;
        Surface.Selected = Selected; Surface.Preview = []; RefreshSurface(); Layers.Items.Clear();
        TreeViewItem Item(SceneObject node)
        {
            var item = new TreeViewItem { Header = node.Label, Tag = node.Id, IsExpanded = node.LayerExpanded };
            item.Expanded += LayerExpansionChanged; item.Collapsed += LayerExpansionChanged;
            foreach (var child in node.Children) item.Items.Add(Item(child)); if (node == Selected) item.IsSelected = true; return item;
        }
        Layers.Items.Add(Item(document.Canvas)); refreshing = false; BuildProperties();
        Title = $"{(Dirty ? "* " : "")}{(path is null ? "無題" : Path.GetFileName(path))} — GridSketch"; CommandManager.InvalidateRequerySuggested();
    }
    private void LayerSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (refreshing || e.NewValue is not TreeViewItem item) return;
        selectedId = (string)item.Tag; Surface.Selected = Selected; Surface.ActiveCell = null; Surface.RangeEnd = null; Surface.Preview = []; Surface.HoveredEdge = null; Surface.InvalidateVisual(); BuildProperties();
    }
    private void LayerExpansionChanged(object sender, RoutedEventArgs e)
    {
        if (refreshing || !ReferenceEquals(sender, e.OriginalSource) || sender is not TreeViewItem item
            || item.Tag is not string id || document.Find(id) is not { } node) return;
        node.LayerExpanded = item.IsExpanded;
        Title = $"{(Dirty ? "* " : "")}{(path is null ? "無題" : Path.GetFileName(path))} — GridSketch";
    }
    private void RefreshSurface()
    {
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
        Properties.Children.Add(box); setters.Add(() => setter(box.Text));
    }
    private void Number(string key, double value, Action<double> setter, double? minimum = null, double maximum = 1000)
    {
        Properties.Children.Add(NumericEditor(key, value, out var box, minimum, maximum));
        setters.Add(() => setter(double.Parse(box.Text, CultureInfo.CurrentCulture)));
    }
    private void ColorField(string key, string value, Action<string> setter)
    {
        Label(key); Properties.Children.Add(ColorEditor(value, out var box)); setters.Add(() => setter(box.Text));
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
        var box = new CheckBox { Content = key, IsChecked = value, Margin = new Thickness(0, 6, 0, 6) }; Properties.Children.Add(box); setters.Add(() => setter(box.IsChecked == true));
    }
    private void Choice(string key, string value, IEnumerable<string> options, Action<string> setter, bool editable = false)
    {
        Label(key); var combo = new ComboBox { ItemsSource = options.ToList(), IsEditable = editable, Text = value }; combo.SelectedItem = value; System.Windows.Automation.AutomationProperties.SetName(combo, key); Properties.Children.Add(combo); setters.Add(() => setter(combo.Text));
    }
    private void FontField(SceneObject node)
    {
        Label("フォント");
        var original = node.Font;
        var current = FontNames.Installed.FirstOrDefault(font => font.Matches(original));
        var combo = new ComboBox { ItemsSource = FontNames.Installed, DisplayMemberPath = nameof(FontName.DisplayName), IsEditable = true };
        combo.SelectedItem = current; combo.Text = current?.DisplayName ?? original;
        Properties.Children.Add(combo);
        setters.Add(() =>
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
        Properties.Children.Clear(); setters.Clear(); var node = Selected; Label(node.Kind == ObjectKind.Border && node.ConnectCellCenters ? "中心罫線" : SceneObject.KindName(node.Kind)); TextField("名前", node.Name, v => node.Name = v);
        UpdatePropertyToggles();
        int metadataCount = setters.Count;
        TemplateProperties(node);
        if (node.Kind == ObjectKind.Canvas)
        {
            setters.Add(CanvasSettings(Properties, node, Surface.Renderer!.OutputBounds));
        }
        else if (!node.IsGridContent) { Pair(document.SelectionOwner(node)?.UseCellLayout == true ? "位置 X（ずらし量）" : "位置 X", node.X, v => node.X = v, document.SelectionOwner(node)?.UseCellLayout == true ? "位置 Y（ずらし量）" : "位置 Y", node.Y, v => node.Y = v); Number(node.Kind == ObjectKind.Image ? "回転（度 / 画像中央）" : "回転（度 / 左上中心）", node.Angle, v => node.Angle = v); }
        switch (node.Kind)
        {
            case ObjectKind.CellSelection:
                CellSelectionProperties(node); break;
            case ObjectKind.Grid:
                Pair("列数", node.Columns, v => node.Columns = (int)v, "行数", node.Rows, v => node.Rows = (int)v, integer: true); Number("マスの大きさ（px）", node.CellSize, v => node.CellSize = v, 12, 256);
                Pair("マス内余白・左右（px）", node.Margin, v => node.Margin = v, "マス内余白・上下（px）", node.MarginY, v => node.MarginY = v);
                Label("外枠・内枠はマス数に追従します。縮小時は個別罫線も範囲内に収めます。文字・画像・塗りは削除しません。"); break;
            case ObjectKind.Border:
                if (node.ConnectCellCenters) Label("隣り合うマスの中心を縦・横につなぎます。中心間の線をクリック・ドラッグして編集します。");
                ColorField("色", node.Color, v => node.Color = v); Number("太さ（0～64px）", node.Thickness, v => node.Thickness = v, 0, 64); Choice("線種", node.LineStyle, ["実線", "破線", "点線"], v => node.LineStyle = v); Choice("端・角の形", node.EffectiveBorderEndCap, ["中央まで", "四角", "丸"], v => node.BorderEndCap = v); Label("四角：端を線幅の半分延長して接続します。丸：線幅を直径とする丸い端にします。"); Label($"対象：{node.Pattern}。辺のクリックで個別編集になります。"); break;
            case ObjectKind.Fill:
                Choice("塗る範囲", node.UseCellMargin ? "余白の内側" : "マス全体", ["マス全体", "余白の内側"], v => node.UseCellMargin = v == "余白の内側");
                ColorField("塗り色", node.Color, v => node.Color = v); Check("この塗りのマスを文字入力から除外", node.BlockInput, v => node.BlockInput = v); Label("非表示でも入力除外を維持します。編集モードでマスをクリックして塗りを切り替えます。"); break;
            case ObjectKind.GridText:
            case ObjectKind.Text:
                FontField(node);
                if (node.Kind == ObjectKind.GridText) Choice("配置領域", node.UseCellMargin ? "余白の内側" : "マス全体", ["マス全体", "余白の内側"], v => node.UseCellMargin = v == "余白の内側");
                Number("文字サイズ（px）", node.FontSize, v => node.FontSize = v, 1, 512); ColorField("文字色", node.Color, v => node.Color = v);
                if (node.Kind == ObjectKind.GridText) Choice("配置", node.Alignment, ["中央", "左上"], v => node.Alignment = v); else TextField("文章", node.Text, v => node.Text = v, true);
                Label("レイヤースタイル ― 縁取り");
                Check("縁取りを有効にする", node.Style.OutlineEnabled, v => node.Style.OutlineEnabled = v);
                ColorField("縁の色・不透明度", node.Style.OutlineColor, v => node.Style.OutlineColor = v);
                Number("縁の太さ（外側 / 0～64px）", node.Style.OutlineWidth, v => node.Style.OutlineWidth = v, 0, 64);
                if (node.Kind == ObjectKind.GridText) Label("すべての文字に適用します。縁も含めてマスに収まるように縮小します。");
                break;
            case ObjectKind.CellFrame:
                ColorField("枠の色", node.Color, v => node.Color = v); Number("枠の太さ（0～64px）", node.Thickness, v => node.Thickness = v, 0, 64);
                Choice("線種", node.LineStyle, ["実線", "破線", "点線"], v => node.LineStyle = v);
                Choice("対象", node.FrameAllCells ? "全マス" : "指定マス", ["全マス", "指定マス"], v =>
                {
                    if (v == "指定マス" && node.FrameAllCells) document.SetFrameCells(node, [], true);
                    else if (v == "全マス") { node.FrameAllCells = true; node.FrameCells.Clear(); }
                });
                Label("マスをクリックして枠を切り替えます。余白は親グリッドで設定し、線の太さも余白の内側に収めます。"); break;
            case ObjectKind.Image:
                if (document.Parent(node)?.Kind == ObjectKind.Grid) Check("マス中央に配置", node.SnapToCell, v => { node.SnapToCell = v; if (v) node.X = node.Y = node.Angle = 0; });
                if (node.SnapToCell) { Pair("列（1から）", node.Column + 1, v => node.Column = (int)v - 1, "行（1から）", node.Row + 1, v => node.Row = (int)v - 1, integer: true); }
                else { Pair("幅（px）", node.Width, v => node.Width = v, "高さ（px）", node.Height, v => node.Height = v, 1, 1000); } break;
            case ObjectKind.Shape:
                Choice("形", node.Shape, ["四角", "角丸", "楕円"], v => node.Shape = v); Pair("幅（px）", node.Width, v => node.Width = v, "高さ（px）", node.Height, v => node.Height = v, 1, 1000);
                ColorField("塗り色", node.FillColor, v => node.FillColor = v); ColorField("線の色", node.Color, v => node.Color = v); Number("線の太さ", node.Thickness, v => node.Thickness = v, 0, 64); break;
            case ObjectKind.Line:
                Choice("経路", node.PathKind, ["直線", "折れ線", "曲線"], v => node.PathKind = v);
                Pair("終点 X（始点から）", node.EndX, v => node.EndX = v, "終点 Y（始点から）", node.EndY, v => node.EndY = v);
                Pair("制御点 X（始点から）", node.ControlX, v => node.ControlX = v, "制御点 Y（始点から）", node.ControlY, v => node.ControlY = v);
                Choice("始点の矢印", node.StartArrow, ["なし", "三角", "開いた矢印"], v => node.StartArrow = v); Choice("終点の矢印", node.EndArrow, ["なし", "三角", "開いた矢印"], v => node.EndArrow = v);
                Number("矢印の大きさ", node.ArrowSize, v => node.ArrowSize = v, 1, 200); ColorField("線の色", node.Color, v => node.Color = v); Number("線の太さ", node.Thickness, v => node.Thickness = v, 0, 64); Choice("線種", node.LineStyle, ["実線", "破線", "点線"], v => node.LineStyle = v); break;
        }
        var apply = setters.ToArray();
        Button("プロパティを適用", () => Change(() =>
        {
            if (document.Parent(node) is { } parent) RequireEditable(parent);
            var oldGrid = (node.Columns, node.Rows, node.CellSize, node.Margin, node.MarginY);
            bool editable = document.Editable(node); foreach (var set in apply.Take(metadataCount)) set();
            if (editable) foreach (var set in apply.Skip(metadataCount)) set();
            if (editable && node.Kind == ObjectKind.Grid)
            {
                var requested = (node.Columns, node.Rows, node.CellSize, node.Margin, node.MarginY);
                (node.Columns, node.Rows, node.CellSize, node.Margin, node.MarginY) = oldGrid;
                document.ResizeGrid(node, requested.Columns, requested.Rows, requested.CellSize, requested.Margin, requested.MarginY);
            }
        }));
        if (node.Kind == ObjectKind.Border) BorderTools(node);
        if (node.Kind == ObjectKind.GridText) TextTools(node);
        if (node.Kind == ObjectKind.Fill) Button("選択範囲の塗りを削除", () => Change(() => { RequireEditable(node); foreach (var cell in CellRange(node)) node.FilledCells.Remove(cell); }));
        if (node.Kind == ObjectKind.CellFrame)
        {
            Button("選択範囲に枠を追加", () => Change(() => { RequireEditable(node); document.SetFrameCells(node, CellRange(node), true); }));
            Button("選択範囲の枠を削除", () => Change(() => { RequireEditable(node); document.SetFrameCells(node, CellRange(node), false); }));
        }
        if (node.Kind == ObjectKind.CellSelection) Button("選択範囲を空にする", () => Change(() => { RequireEditable(node); var cells = CellRange(node).ToHashSet(); node.StateCells.RemoveAll(c => cells.Contains(new(c.Column, c.Row))); }));
        if (node.Kind == ObjectKind.Image) Button("画像を読み込み / 置換…", () => ReplaceImage(node));
        if (node.Kind == ObjectKind.Line && document.GridFor(node) is not null) Label("編集モード：クリックで始点、Shift+クリックで終点をマス中央へ配置します。");
        if (node != document.Canvas && !node.IsGridContent)
        {
            Label("親の変更（見た目の位置・回転を維持）");
            var candidates = document.All().Where(n => n.IsContainer && n != node && !Descendants(node).Contains(n)).ToList();
            var parents = new ComboBox { ItemsSource = candidates, DisplayMemberPath = "Name", SelectedItem = document.Parent(node) }; Properties.Children.Add(parents);
            Button("選択した親へ移動", () => Change(() => { if (parents.SelectedItem is SceneObject parent) document.Reparent(node, parent); }));
        }

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
        Label("辺の一括設定（未選択なら全体 / Shift+クリックで範囲）");
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
        var presets = new WrapPanel();
        foreach (var name in new[] { "外枠", "内側", "すべて", "なし" })
        {
            var b = new Button { Content = name }; b.Click += (_, _) => Change(() => { RequireEditable(node); node.Pattern = name == "なし" ? "個別" : name; node.Edges.Clear(); }); presets.Children.Add(b);
        }
        Properties.Children.Add(presets); Label("プリセットは全体を置換し、マス数に追従します。下の操作は指定範囲へ適用します。");
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
            edges.RemoveWhere(e => !SceneDocument.BorderEdgeInside(node, grid, e));
            node.Edges = SceneDocument.BorderEdges(node, grid).ToHashSet(); node.Pattern = "個別"; if (remove) node.Edges.ExceptWith(edges); else node.Edges.UnionWith(edges);
        });
        Button("チェックした辺を追加", () => Apply(false)); Button("チェックした辺を削除", () => Apply(true));
    }
    private void TextTools(SceneObject node)
    {
        Label("文字列を一括入力（編集モードで開始マスをクリック）");
        var entry = new TextBox(); Properties.Children.Add(entry);
        var orientation = new ComboBox { ItemsSource = new[] { "横（右へ）", "縦（下へ）" }, SelectedIndex = 0 }; Properties.Children.Add(orientation);
        List<Glyph> Plan()
        {
            var cell = Surface.ActiveCell ?? throw new InvalidOperationException("開始マスをクリックしてください。");
            return document.PlanText(node, cell.Column, cell.Row, entry.Text, orientation.SelectedIndex == 1);
        }
        Button("配置をプレビュー", () => { Surface.Preview = Plan(); Surface.InvalidateVisual(); });
        Button("文字を配置", () =>
        {
            RequireEditable(node); var plan = Plan(); var conflicts = plan.Count(p => node.Glyphs.Any(g => g.Column == p.Column && g.Row == p.Row && g.Text != p.Text));
            if (conflicts > 0 && MessageBox.Show(this, $"{conflicts}マスの文字を上書きしますか？", "文字の上書き", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            Change(() => { foreach (var p in plan) { var old = node.Glyphs.Find(g => g.Column == p.Column && g.Row == p.Row); if (old is null) node.Glyphs.Add(p); else if (old.Text != p.Text) { old.Text = p.Text; old.Font = null; old.Size = null; } } });
        });
        Button("選択範囲の文字を削除", () => Change(() => { RequireEditable(node); var cells = CellRange(node).ToHashSet(); node.Glyphs.RemoveAll(g => cells.Contains(new(g.Column, g.Row))); }));
        Button("個別書式を共通フォント・サイズへ統一", () => Change(() => { RequireEditable(node); foreach (var g in node.Glyphs) { g.Font = null; g.Size = null; } }));
    }
    private SceneObject AddParent(bool gridRequired)
    {
        var node = Selected;
        if (gridRequired) return document.GridFor(node) ?? throw new InvalidOperationException("グリッドまたはグリッド直下のレイヤーを選択してください。");
        return node.IsContainer ? node : document.Parent(node) ?? document.Canvas;
    }
    private void AddClick(object sender, RoutedEventArgs e) => Try(() =>
    {
        if (sender is not MenuItem { Tag: string tag }) return;
        bool independent = tag == "IndependentGrid", centerBorder = tag == "CenterBorder";
        if (!Enum.TryParse<ObjectKind>(independent ? "Grid" : centerBorder ? "Border" : tag, out var kind)) return;
        if (kind == ObjectKind.Grid)
        {
            var grid = GridDialog(false, independent ? "独立マス" : "基本の方眼紙"); if (grid is null) return;
            Change(() => { var parent = AddParent(false); RequireEditable(parent); parent.Children.Insert(0, grid); selectedId = grid.Id; }); return;
        }
        byte[]? image = kind == ObjectKind.Image ? ReadImage() : null; if (kind == ObjectKind.Image && image is null) return;
        Change(() =>
        {
            var parent = AddParent(kind is ObjectKind.Border or ObjectKind.GridText or ObjectKind.Fill or ObjectKind.CellFrame or ObjectKind.CellSelection); RequireEditable(parent);
            var node = new SceneObject { Kind = kind, Name = centerBorder ? "中心罫線" : SceneObject.KindName(kind), ConnectCellCenters = centerBorder, UseCellLayout = kind == ObjectKind.CellSelection, ImageData = image };
            if (kind == ObjectKind.Image && parent.Kind == ObjectKind.Grid) { node.SnapToCell = true; node.Column = Surface.ActiveCell?.Column ?? 0; node.Row = Surface.ActiveCell?.Row ?? 0; }
            if (kind == ObjectKind.Fill) { node.Color = "#000000"; parent.Children.Add(node); } else parent.Children.Insert(0, node);
            if (parent.Kind == ObjectKind.CellSelection)
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
        var node = Selected; RequireEditable(node); var parent = document.Parent(node); if (parent is null) return;
        int index = parent.Children.IndexOf(node), next = index + offset; if (next < 0 || next >= parent.Children.Count) return; parent.Children.RemoveAt(index); parent.Children.Insert(next, node);
    });
    private void DeleteClick(object sender, RoutedEventArgs e) => Try(() =>
    {
        var node = Selected; if (node == document.Canvas) return; RequireEditable(node);
        if (node.Children.Count > 0 && MessageBox.Show(this, "子オブジェクトも削除しますか？", "削除", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        Change(() => { var parent = document.Parent(node)!; parent.Children.Remove(node); selectedId = parent.Id; Surface.ActiveCell = null; Surface.RangeEnd = null; });
    });
    private void WrapClick(object sender, RoutedEventArgs e) => Change(() =>
    {
        var node = Selected; RequireEditable(node); if (node == document.Canvas || node.IsGridContent) throw new InvalidOperationException("グリッド依存の内容はグリッドごとグループ化してください。");
        var parent = document.Parent(node)!; int index = parent.Children.IndexOf(node); var group = new SceneObject { Kind = ObjectKind.Group, Name = "グループ" }; parent.Children.Insert(index, group); document.Reparent(node, group);
        if (parent.Kind == ObjectKind.CellSelection) foreach (var state in parent.States.Where(s => s.ObjectId == node.Id)) state.ObjectId = group.Id;
        selectedId = group.Id;
    });
    private void UngroupClick(object sender, RoutedEventArgs e) => Change(() =>
    {
        var node = Selected; RequireEditable(node); if (node.Kind != ObjectKind.Group) throw new InvalidOperationException("通常のグループを選択してください。");
        var parent = document.Parent(node)!;
        if (parent.Kind == ObjectKind.CellSelection) throw new InvalidOperationException("見本グループは解除せず、子を個別に移動して割り当て直してください。");
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
        var p = e.GetPosition(Surface); var node = Selected;
        if (cellEditMode)
        {
            RequireEditable(node); var grid = document.GridFor(node); if (grid is null) { Status.Text = "グリッド直下のレイヤーを選択してください。"; return; }
            var cell = Surface.CellAt(grid, p);
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && cell is { } rangeEnd && node.Kind != ObjectKind.Line) { Surface.ActiveCell ??= rangeEnd; Surface.RangeEnd = rangeEnd; Surface.InvalidateVisual(); return; }
            if (node.Kind == ObjectKind.Border && Surface.EdgeAt(grid, p, Zoom.Value) is { } edge) { StartPaint(node, grid, p, edge); e.Handled = true; return; }
            if (cell is not { } c) return;
            if (node.Kind == ObjectKind.GridText) { SelectTextCell(c); e.Handled = true; return; }
            Surface.ActiveCell = c; Surface.RangeEnd = null; Surface.Preview = [];
            if (node.Kind is ObjectKind.Fill or ObjectKind.CellFrame or ObjectKind.CellSelection) { StartPaint(node, grid, p); e.Handled = true; return; }
            else if (node.Kind == ObjectKind.Image && node.SnapToCell) Change(() => { node.Column = c.Column; node.Row = c.Row; });
            else if (node.Kind == ObjectKind.Line) Change(() =>
            {
                var center = new Point((c.Column + 0.5) * grid.CellSize, (c.Row + 0.5) * grid.CellSize);
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { var inverse = SceneRenderer.LocalTransform(node).Value; inverse.Invert(); var end = inverse.Transform(center); node.EndX = end.X; node.EndY = end.Y; }
                else { node.X = center.X; node.Y = center.Y; }
            });
            Surface.InvalidateVisual(); Status.Text = $"{c.Column + 1}列・{c.Row + 1}行を選択。Shift+クリックで範囲選択。"; return;
        }
        var world = Surface.ToWorld(p);
        if (node == document.Canvas || !Surface.Renderer!.Bounds.TryGetValue(node.Id, out var selectedBounds) || !selectedBounds.Contains(world))
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
        if (node == document.Canvas || node.IsGridContent || !document.Editable(node)) return;
        dragSnapshot = document.Snapshot(); dragStart = world; dragPosition = new Point(node.X, node.Y); Surface.CaptureMouse();
    });
    private void SurfaceMove(object sender, MouseEventArgs e)
    {
        var node = Selected; var p = e.GetPosition(Surface);
        if (paintStroke is not null)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { FinishDrag(); Surface.ReleaseMouseCapture(); return; }
            paintStroke.Move(Surface.ToLocal(paintGrid!, p)); RefreshSurface(); return;
        }
        if (dragStart is { } start)
        {
            var current = Surface.ToWorld(p); var parent = document.Parent(node)!; var inverse = document.World(parent); inverse.Invert(); var delta = inverse.Transform(current) - inverse.Transform(start);
            node.X = dragPosition.X + delta.X; node.Y = dragPosition.Y + delta.Y; RefreshSurface(); return;
        }
        var grid = document.GridFor(node); var edge = cellEditMode && node.Kind == ObjectKind.Border && grid is not null ? Surface.EdgeAt(grid, p, Zoom.Value) : null;
        if (edge != Surface.HoveredEdge) { Surface.HoveredEdge = edge; Surface.InvalidateVisual(); }
    }
    private void StartPaint(SceneObject node, SceneObject grid, Point point, GridEdge? edge = null)
    {
        if (node.Kind == ObjectKind.CellSelection && node.States.Count == 0) { Status.Text = "子に画像などを追加するか、プロパティで状態を追加してください。"; return; }
        dragSnapshot = document.Snapshot(); paintGrid = grid;
        paintStroke = new GridPaintStroke(document, node, grid, Surface.ToLocal(grid, point), edge);
        Surface.Preview = []; Surface.RangeEnd = null; Surface.HoveredEdge = null;
        Surface.CaptureMouse(); RefreshSurface();
        Status.Text = "ドラッグで連続編集。開始時の追加／削除を維持します。";
    }
    private void FinishDrag()
    {
        if (dragStart is null && paintStroke is null) return; dragStart = null; paintStroke = null; paintGrid = null;
        if (dragSnapshot is { } snapshot) { try { document.Validate(); if (snapshot != document.Snapshot()) RememberUndo(snapshot); } catch { document = SceneDocument.Restore(snapshot); } }
        dragSnapshot = null; RefreshAll();
    }
    private void SurfaceUp(object sender, MouseButtonEventArgs e) { if (paintStroke is not null) paintStroke.Move(Surface.ToLocal(paintGrid!, e.GetPosition(Surface))); FinishDrag(); Surface.ReleaseMouseCapture(); }
    private void SurfaceLostCapture(object sender, MouseEventArgs e) => FinishDrag();
    private void SurfaceLeave(object sender, MouseEventArgs e) { Surface.HoveredEdge = null; Surface.InvalidateVisual(); }
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
        FinishDrag(); Surface.ReleaseMouseCapture();
        if (!Dirty) return true; var answer = MessageBox.Show(this, "変更を保存しますか？", "GridSketch", MessageBoxButton.YesNoCancel, MessageBoxImage.Question); return answer == MessageBoxResult.No || answer == MessageBoxResult.Yes && Save(false);
    }
    private bool Save(bool saveAs)
    {
        FinishDrag(); Surface.ReleaseMouseCapture();
        string? destination = path;
        if (saveAs || destination is null)
        {
            var dialog = new SaveFileDialog { Filter = "GridSketchプロジェクト|*.gridsketch", DefaultExt = ".gridsketch", FileName = Path.GetFileName(path ?? "無題.gridsketch") };
            if (dialog.ShowDialog(this) != true) return false; destination = dialog.FileName;
        }
        bool saved = false; Try(() => { document.Save(destination); path = destination; savedState = document.Snapshot(); saved = true; RefreshAll(); Status.Text = "プロジェクトを保存しました。"; RememberFile(destination); }); return saved;
    }
    private void SaveClick(object sender, RoutedEventArgs e) => Save(false);
    private void SaveAsClick(object sender, RoutedEventArgs e) => Save(true);
    private void ExitClick(object sender, RoutedEventArgs e) => Close();
    private void NewClick(object sender, RoutedEventArgs e) => Try(() =>
    {
        if (!CanDiscard()) return; var grid = GridDialog(true); if (grid is null) return;
        document = new SceneDocument { Canvas = grid }; path = null; selectedId = document.Canvas.Id; savedState = document.Snapshot(); undo.Clear(); redo.Clear(); Surface.ActiveCell = null; Surface.RangeEnd = null; RefreshAll(); CenterClick(this, new RoutedEventArgs());
    });
    private void OpenClick(object sender, RoutedEventArgs e) => Try(() =>
    {
        var dialog = new OpenFileDialog { Filter = "GridSketchプロジェクト|*.gridsketch" }; if (dialog.ShowDialog(this) == true) OpenProject(dialog.FileName);
    });
    private void OpenProject(string filePath)
    {
        if (!CanDiscard()) return; var loaded = SceneDocument.Load(filePath); document = loaded; path = filePath; selectedId = document.Canvas.Id; savedState = document.Snapshot(); undo.Clear(); redo.Clear(); Surface.ActiveCell = null; Surface.RangeEnd = null;
        RefreshAll(); CenterClick(this, new RoutedEventArgs()); Status.Text = "プロジェクトを開きました。保存するとオブジェクト形式（v3）になります。"; RememberFile(filePath);
    }
    private void RememberFile(string filePath) { if (!recentFiles.Remember(filePath)) Status.Text += " 履歴を保存できませんでした。"; RefreshRecentFilesMenu(); }
    private void RefreshRecentFilesMenu()
    {
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
    private void WindowClosing(object? sender, CancelEventArgs e) => e.Cancel = !CanDiscard();
}


