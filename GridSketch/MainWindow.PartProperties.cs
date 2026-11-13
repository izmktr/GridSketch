using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GridSketch;

public partial class MainWindow
{
    private static readonly Dictionary<string, string> PartPropertyLabels = new()
    {
        ["ImageData"] = "画像", ["Text"] = "文章", ["Font"] = "フォント", ["FontSize"] = "文字サイズ",
        ["Color"] = "文字・線の色", ["FillColor"] = "塗り色", ["Width"] = "幅", ["Height"] = "高さ",
        ["X"] = "位置 X", ["Y"] = "位置 Y", ["Angle"] = "回転", ["Visible"] = "表示", ["Locked"] = "ロック",
        ["Name"] = "名前", ["Thickness"] = "線の太さ", ["Style.OutlineEnabled"] = "縁取り",
        ["Style.OutlineColor"] = "縁取りの色", ["Style.OutlineWidth"] = "縁取りの太さ",
        ["LayoutAnchor"] = "位置合わせ", ["LayoutSizing"] = "サイズの扱い", ["LayoutRegion"] = "配置先",
        ["PlacementOffsetX"] = "ずらし量 X", ["PlacementOffsetY"] = "ずらし量 Y",
        ["GridColumn"] = "マス座標・列", ["GridRow"] = "マス座標・行", ["Glyphs"] = "グリッド文字",
        ["Edges"] = "罫線", ["FilledCells"] = "塗りマス", ["States"] = "状態", ["StateCells"] = "マスの割り当て",
        ["LineStyle"] = "線種", ["Shape"] = "図形の種類", ["PathKind"] = "ラインの経路",
        ["EndX"] = "終点 X", ["EndY"] = "終点 Y", ["ControlX"] = "制御点 X", ["ControlY"] = "制御点 Y"
    };
    private static string PartPropertyLabel(string property) => PartPropertyLabels.GetValueOrDefault(property, "その他の設定（" + property + "）");
    private IEnumerable<(SceneObject Node, string Path)> DefinitionNodes(LibraryPart definition)
    {
        var preview = new SceneDocument { Parts = document.Parts };
        var instance = new SceneObject { Kind = ObjectKind.Group, HasLayoutFrame = true, Part = new() { DefinitionId = definition.Id } };
        preview.Canvas.Children.Add(instance); preview.ResolveParts();
        return preview.All().Where(n => n.PartOwnerId == instance.Id).Select(n => (n, n.PartSourcePath!)).ToList();
    }
    private void PartProperties(SceneObject instance)
    {
        var definition = document.Parts.Single(p => p.Id == instance.Part!.DefinitionId);
        var owner = document.PartOwner(instance) ?? instance;
        string prefix = owner == instance ? "" : instance.PartSourcePath + "/";
        Label("参照元：" + definition.Name);
        Button("定義を別タブで編集", () => EditPartTab(definition));
        foreach (var published in definition.Published)
        {
            string sourcePath = prefix + published.Path;
            var target = document.All().FirstOrDefault(n => n.PartOwnerId == owner.Id && n.PartSourcePath == sourcePath);
            if (target is null) { Label(published.Label + "：定義側の対象がありません"); continue; }
            var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var mark = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            var reset = new Button { Content = "↶", ToolTip = "定義の値に戻す", MinWidth = 30 };
            DockPanel.SetDock(reset, Dock.Right); row.Children.Add(reset); row.Children.Add(mark); Properties.Children.Add(row);
            void Indicator()
            {
                bool overridden = owner.Part!.Overrides.TryGetValue(sourcePath, out var values) && values.ContainsKey(published.Property);
                mark.Text = published.Label + (overridden ? "  ［上書き］" : "  ［定義の値］");
                mark.Foreground = overridden ? Brushes.DarkOrange : Brushes.DimGray;
                reset.IsEnabled = overridden && document.Editable(instance) && document.Editable(target);
            }
            reset.Click += (_, _) => Change(() => { RequireEditable(instance); RequireEditable(target); document.ResetPartOverride(owner, sourcePath, published.Property); });
            propertySectionSummaries.Add(Indicator); Indicator();
            void Set(object? value)
            {
                RequireEditable(instance); RequireEditable(target);
                document.SetPartOverride(owner, sourcePath, published.Property, JsonSerializer.SerializeToElement(value, SceneDocument.JsonOptions));
            }
            var value = SceneDocument.PartValue(target, published.Property);
            int start = Properties.Children.Count;
            if (published.Property == "ImageData")
            {
                if (target.ImageData is { } bytes) Properties.Children.Add(new Image { Source = Document.DecodeImage(bytes), Height = 90, Stretch = Stretch.Uniform });
                Button(published.Label + "を変更…", () => { var image = ReadImage(); if (image is not null) Change(() => Set(image)); });
            }
            else if (published.Property.EndsWith("Color")) ColorField(published.Label, value.GetString()!, v => Set(v));
            else if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) Check(published.Label, value.GetBoolean(), v => Set(v));
            else if (value.ValueKind == JsonValueKind.Number) Number(published.Label, value.GetDouble(), v => Set(v));
            else TextField(published.Label, value.ValueKind == JsonValueKind.Null ? "" : value.GetString()!, v => Set(v), published.Property == "Text");
            if (!document.Editable(target)) foreach (var child in Properties.Children.Cast<UIElement>().Skip(start)) child.IsEnabled = false;
        }
        PartOverrideList(instance);
        if (owner == instance) Button("パーツを独立化", () => Change(() => document.DetachPart(instance)));
    }
    private void PartOverrideList(SceneObject node)
    {
        var instance = node.IsPart && node.PartOwnerId is null ? node : document.PartOwner(node);
        if (instance?.Part is null) return;
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 8) }; Properties.Children.Add(panel);
        void Refresh()
        {
            panel.Children.Clear();
            var entries = instance.Part.Overrides.SelectMany(p => p.Value.Keys.Select(k => (Path: p.Key, Property: k)))
                .Where(e => node == instance || e.Path == node.PartSourcePath || node.IsPart && e.Path.StartsWith(node.PartSourcePath + "/")).ToList();
            panel.Children.Add(new TextBlock { Text = $"上書き：{entries.Count}項目", FontWeight = FontWeights.SemiBold });
            foreach (var entry in entries)
            {
                var target = document.All().FirstOrDefault(n => n.PartOwnerId == instance.Id && n.PartSourcePath == entry.Path);
                var reset = new Button { Content = $"↶ {target?.Name ?? "削除された対象"}：{PartPropertyLabel(entry.Property)}", ToolTip = "定義の値に戻す",
                    HorizontalContentAlignment = HorizontalAlignment.Left };
                reset.Click += (_, _) => Change(() => { RequireEditable(instance); document.ResetPartOverride(instance, entry.Path, entry.Property); });
                panel.Children.Add(reset);
            }
            if (entries.Count > 0)
            {
                var resetAll = new Button { Content = node == instance ? "すべて定義に戻す" : "このオブジェクトを定義に戻す" };
                resetAll.Click += (_, _) => Change(() =>
                {
                    RequireEditable(instance);
                    foreach (var entry in entries) document.ResetPartOverride(instance, entry.Path, entry.Property);
                });
                panel.Children.Add(resetAll);
            }
        }
        propertySectionSummaries.Add(Refresh); Refresh();
    }
    private void EditPublishedProperties(LibraryPart definition, Window owner)
    {
        var dialog = new Window { Owner = owner, Title = "公開プロパティ — " + definition.Name, Width = 650, Height = 550,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new Thickness(16) }; dialog.Content = root;
        var help = new TextBlock { Text = "よく変更する項目を、キャンバス上のパーツ自身に表示します。対象・項目・表示名を指定してください。", TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(help, Dock.Top); root.Children.Add(help);
        var fields = new StackPanel(); DockPanel.SetDock(fields, Dock.Bottom); root.Children.Add(fields);
        var list = new ListBox { MinHeight = 130 }; root.Children.Add(list);
        var draft = definition.Published.Select(p => new PublishedPartProperty { Path = p.Path, Property = p.Property, Label = p.Label }).ToList();
        var targets = DefinitionNodes(definition).ToList();
        fields.Children.Add(new TextBlock { Text = "対象のオブジェクト" });
        var target = new ComboBox { ItemsSource = targets.Select(p => p.Node.Name + " ［" + p.Node.DisplayKindName + "］").ToList(), SelectedIndex = targets.Count > 0 ? 0 : -1 }; fields.Children.Add(target);
        var keys = new List<string>(); var property = new ComboBox(); fields.Children.Add(new TextBlock { Text = "公開する項目" }); fields.Children.Add(property);
        var name = new TextBox { MaxLength = 200 }; fields.Children.Add(new TextBlock { Text = "表示名" }); fields.Children.Add(name);
        void UpdateProperties()
        {
            if (target.SelectedIndex < 0) return;
            var n = targets[target.SelectedIndex].Node;
            keys = ["Visible"];
            if (n.Kind is ObjectKind.Image or ObjectKind.Shape || n.IsFrame) keys.AddRange(["Width", "Height"]);
            if (!n.IsGridContent && !n.IsOrganizer && n.Kind != ObjectKind.Line) keys.AddRange(["X", "Y", "Angle"]);
            if (n.Kind == ObjectKind.Image) keys.Insert(0, "ImageData");
            if (n.Kind is ObjectKind.Text or ObjectKind.GridText) keys.InsertRange(0, ["Font", "FontSize", "Color", "Style.OutlineEnabled", "Style.OutlineColor", "Style.OutlineWidth"]);
            if (n.Kind == ObjectKind.Text) keys.Insert(0, "Text");
            if (n.Kind is ObjectKind.Shape or ObjectKind.Line or ObjectKind.Border) keys.InsertRange(0, ["Color", "Thickness"]);
            if (n.Kind == ObjectKind.Shape) keys.Insert(0, "FillColor");
            property.ItemsSource = keys.Select(PartPropertyLabel).ToList(); property.SelectedIndex = 0;
        }
        target.SelectionChanged += (_, _) => UpdateProperties();
        property.SelectionChanged += (_, _) => { if (property.SelectedIndex >= 0) name.Text = PartPropertyLabel(keys[property.SelectedIndex]); };
        void Reload(int selected = -1) { list.ItemsSource = null; list.ItemsSource = draft.Select(p => p.Label + " — " + PartPropertyLabel(p.Property)).ToList(); list.SelectedIndex = selected; }
        var buttons = new WrapPanel(); fields.Children.Add(buttons);
        void ActionButton(string label, Action action) { var b = new Button { Content = label }; b.Click += (_, _) => Try(action); buttons.Children.Add(b); }
        ActionButton("追加", () =>
        {
            if (target.SelectedIndex < 0 || property.SelectedIndex < 0 || string.IsNullOrWhiteSpace(name.Text)) return;
            var p = new PublishedPartProperty { Path = targets[target.SelectedIndex].Path, Property = keys[property.SelectedIndex], Label = name.Text.Trim() };
            if (draft.Any(d => d.Path == p.Path && d.Property == p.Property)) throw new InvalidOperationException("この項目は公開済みです。");
            draft.Add(p); Reload(draft.Count - 1);
        });
        ActionButton("表示名を変更", () => { if (list.SelectedIndex >= 0 && !string.IsNullOrWhiteSpace(name.Text)) { draft[list.SelectedIndex].Label = name.Text.Trim(); Reload(list.SelectedIndex); } });
        ActionButton("削除", () => { if (list.SelectedIndex >= 0) { draft.RemoveAt(list.SelectedIndex); Reload(); } });
        void Move(int delta) { int i = list.SelectedIndex, next = i + delta; if (i < 0 || next < 0 || next >= draft.Count) return; (draft[i], draft[next]) = (draft[next], draft[i]); Reload(next); }
        ActionButton("↑", () => Move(-1)); ActionButton("↓", () => Move(1));
        ActionButton("適用", () => { Change(() => definition.Published = draft); dialog.Close(); });
        ActionButton("キャンセル", dialog.Close);
        UpdateProperties(); Reload(); dialog.ShowDialog();
    }
    private PartConflictPolicy? ChoosePartConflict(SceneDocument target, IEnumerable<string> ids, IEnumerable<LibraryPart> source, Window owner)
    {
        var conflicts = target.PartConflicts(ids, source);
        if (conflicts.Count == 0) return PartConflictPolicy.Separate;
        var dialog = new Window { Owner = owner, Title = "パーツ定義の違い", Width = 540, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(16) }; dialog.Content = panel;
        panel.Children.Add(new TextBlock { Text = "同じIDで内容が異なる定義があります：\n" + string.Join("、", conflicts) + "\n追加先での扱いを選んでください。", TextWrapping = TextWrapping.Wrap });
        PartConflictPolicy? choice = null;
        foreach (var (label, policy) in new[] { ("別パーツとして取り込む（コピー元を維持）", PartConflictPolicy.Separate),
            ("追加先の定義を使う", PartConflictPolicy.UseDestination), ("追加先の定義を更新する（配置済みパーツにも反映）", PartConflictPolicy.UpdateDestination) })
        {
            var button = new Button { Content = label, IsDefault = policy == PartConflictPolicy.Separate };
            button.Click += (_, _) => { choice = policy; dialog.DialogResult = true; }; panel.Children.Add(button);
        }
        var cancel = new Button { Content = "キャンセル", IsCancel = true }; panel.Children.Add(cancel);
        dialog.ShowDialog(); return choice;
    }
}
