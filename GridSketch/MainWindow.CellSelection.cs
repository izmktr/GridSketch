using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GridSketch;

public partial class MainWindow
{
    private sealed record StateTarget(string? Id, string Name);
    private void CellSelectionProperties(SceneObject node)
    {
        Label("状態");
        var targets = new List<StateTarget> { new(null, "未割り当て") };
        targets.AddRange(node.Children.Select(c => new StateTarget(c.Id, c.Name + " [" + SceneObject.KindName(c.Kind) + "]")));
        var refreshRotations = new List<Action>();
        Border? activeMarker = null;
        void SetInsertionMarker(Border? next)
        {
            if (activeMarker != next && activeMarker is not null) activeMarker.Visibility = Visibility.Hidden;
            activeMarker = next;
            if (next is not null) next.Visibility = Visibility.Visible;
        }
        for (int i = 0; i < node.States.Count; i++)
        {
            var state = node.States[i];
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            if (node.Kind == ObjectKind.EdgeSelection)
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            // Draw the insertion marker over the row without changing its size or hit target.
            var marker = new Border { Height = 2, Background = Brushes.DodgerBlue, VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false, Visibility = Visibility.Hidden };
            var overlay = new Grid(); overlay.Children.Add(row); overlay.Children.Add(marker);
            var frame = new Border { Child = overlay, Tag = marker, Margin = new Thickness(8, 0, 0, 0), Background = Brushes.Transparent, AllowDrop = true };
            Properties.Children.Add(frame);
            var grip = new Button { Content = "≡", Width = 32, Height = 32, Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, ToolTip = "ドラッグで並べ替え（Alt＋↑／↓でも移動）", Padding = new Thickness(2), Cursor = System.Windows.Input.Cursors.SizeAll };
            row.Children.Add(grip);
            var number = new TextBlock { Text = $"{i + 1}：", VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(number, 1); row.Children.Add(number);
            var target = new ComboBox { ItemsSource = targets, DisplayMemberPath = "Name", SelectedItem = targets.FirstOrDefault(t => t.Id == state.ObjectId) ?? targets[0], MinWidth = 60, Height = 32, Margin = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(target, $"状態{i + 1}の表示オブジェクト");
            Grid.SetColumn(target, 2); row.Children.Add(target);
            LiveChoice(target, () => node.States.Single(s => s.Id == state.Id).ObjectId = (target.SelectedItem as StateTarget)?.Id);
            if (node.Kind == ObjectKind.EdgeSelection)
            {
                var rotation = new Button { Width = 32, Height = 32, Margin = new Thickness(0), Padding = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                System.Windows.Automation.AutomationProperties.SetName(rotation, $"状態{i + 1}の垂直辺での向き");
                SceneObject? Template() => node.Children.Find(c => c.Id == state.ObjectId);
                void RefreshRotation()
                {
                    var template = Template();
                    rotation.IsEnabled = template is not null && document.Editable(template);
                    rotation.Content = EdgeRotationIcon(template?.VerticalRotation ?? "回転なし");
                    rotation.ToolTip = template is null ? "表示オブジェクトを選択してください" : $"垂直辺での向き：{template.VerticalRotation}（クリックで切り替え）";
                }
                refreshRotations.Add(RefreshRotation);
                RefreshRotation();
                target.SelectionChanged += (_, _) => { foreach (var refresh in refreshRotations) refresh(); };
                rotation.Click += (_, _) =>
                {
                    if (Template() is not { } template || !document.Editable(template)) return;
                    ImmediateProperty(rotation, () => template.VerticalRotation = template.VerticalRotation switch
                    { "回転なし" => "右90°", "右90°" => "左90°", _ => "回転なし" });
                    foreach (var refresh in refreshRotations) refresh();
                };
                Grid.SetColumn(rotation, 3); row.Children.Add(rotation);
            }
            var trash = new Button { Content = new System.Windows.Shapes.Path { Data = Geometry.Parse("M3,5 L17,5 M6,5 L7,19 13,19 14,5 M8,5 L8,2 12,2 12,5 M9,8 L9,16 M11,8 L11,16"), Stroke = Brushes.DimGray, StrokeThickness = 1.5, Width = 18, Height = 20, Stretch = Stretch.Uniform }, ToolTip = "状態を削除", Width = 32, Height = 32, Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(5) };
            System.Windows.Automation.AutomationProperties.SetName(trash, $"状態{i + 1}を削除");
            Grid.SetColumn(trash, node.Kind == ObjectKind.EdgeSelection ? 4 : 3); row.Children.Add(trash);
            row.IsEnabled = document.Editable(node);
            trash.Click += (_, _) => Change(() => { RequireEditable(node); node.States.RemoveAll(s => s.Id == state.Id); node.StateCells.RemoveAll(c => c.StateId == state.Id); node.StateEdges.RemoveAll(c => c.StateId == state.Id); });
            Point? start = null;
            grip.PreviewMouseLeftButtonDown += (_, e) => start = e.GetPosition(grip);
            // ButtonBase handles bubbling MouseMove while pressed. Observe the preview
            // event before it does, then hand mouse capture to the drag/drop operation.
            grip.PreviewMouseMove += (_, e) =>
            {
                if (start is not { } origin || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
                var d = e.GetPosition(grip) - origin;
                if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                start = null; CommitPropertyEdit();
                grip.ReleaseMouseCapture(); e.Handled = true;
                try { DragDrop.DoDragDrop(grip, new DataObject("GridSketch.State", state.Id), DragDropEffects.Move); }
                finally
                {
                    SetInsertionMarker(null);
                }
            };
            grip.PreviewMouseLeftButtonUp += (_, _) => start = null;
            grip.LostMouseCapture += (_, _) => start = null;
            frame.PreviewDragOver += (_, e) =>
            {
                bool valid = e.Data.GetData("GridSketch.State") is string id && node.States.Any(s => s.Id == id);
                e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;
                marker.VerticalAlignment = e.GetPosition(frame).Y < frame.ActualHeight / 2 ? VerticalAlignment.Top : VerticalAlignment.Bottom;
                SetInsertionMarker(valid ? marker : null);
                e.Handled = true;
            };
            frame.PreviewDragLeave += (_, e) =>
            {
                // Moving between child controls also raises DragLeave. Keep the marker
                // until the pointer actually leaves the row.
                var point = e.GetPosition(frame);
                if (activeMarker == marker && !new Rect(frame.RenderSize).Contains(point)) SetInsertionMarker(null);
            };
            frame.PreviewDrop += (_, e) =>
            {
                SetInsertionMarker(null);
                if (e.Data.GetData("GridSketch.State") is string id) MoveState(node, id, state.Id, e.GetPosition(frame).Y >= frame.ActualHeight / 2);
                e.Handled = true;
            };
            grip.PreviewKeyDown += (_, e) =>
            {
                if (System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Alt || e.SystemKey is not (System.Windows.Input.Key.Up or System.Windows.Input.Key.Down)) return;
                int index = node.States.FindIndex(s => s.Id == state.Id); int next = index + (e.SystemKey == System.Windows.Input.Key.Up ? -1 : 1);
                if (next >= 0 && next < node.States.Count) MoveState(node, state.Id, node.States[next].Id, next > index);
                e.Handled = true;
            };
        }
        Button("＋ 状態を追加", () => Change(() => { RequireEditable(node); node.States.Add(new CellState { Name = $"状態 {node.States.Count + 1}" }); }));
        var addButton = (Button)Properties.Children[Properties.Children.Count - 1];
        addButton.Margin = new Thickness(8, 3, 0, 3);
        addButton.IsEnabled = node.States.Count < 128 && document.Editable(node);
    }
    private void MoveState(SceneObject node, string id, string targetId, bool after)
    {
        if (id == targetId || !node.States.Any(s => s.Id == id) || !node.States.Any(s => s.Id == targetId)) return;
        Change(() => { RequireEditable(node); var state = node.States.Single(s => s.Id == id); node.States.Remove(state); int index = node.States.FindIndex(s => s.Id == targetId); node.States.Insert(index + (after ? 1 : 0), state); });
    }
    private void TemplateProperties(SceneObject node)
    {
        if (document.SelectionOwner(node) is not { } owner) return;
        if (node.Kind != ObjectKind.Group && !owner.UseObjectLayout)
        {
            RegionIcons("配置領域", node.TemplateRegion, "マス全体", "余白の内側", v => node.TemplateRegion = v);
            SizeIcons("領域への収め方", node.TemplateSizing, v => node.TemplateSizing = v, template: true);
            AlignmentIcons("配置基準", node.TemplateAnchor, v => node.TemplateAnchor = v);
        }
        var grid = document.GridFor(owner)!;
        var previewRoot = node;
        if (owner.UseObjectLayout)
            while (document.Parent(previewRoot) is { } parent && parent != owner) previewRoot = parent;
        var drawing = owner.UseObjectLayout ? Surface.Renderer!.ObjectTemplate(previewRoot) : owner.UseCellLayout ? Surface.Renderer!.CellTemplate(node, grid) : Surface.Renderer!.Template(node);
        if (owner.UseCellLayout || owner.UseObjectLayout)
        {
            // Give the preview a fixed cell-sized frame so changing a sibling does not resize it.
            var framed = new DrawingGroup();
            using (var dc = framed.Open())
            {
                var rect = SceneDocument.CellRect(grid, new(0, 0), false);
                dc.DrawRectangle(Brushes.White, new Pen(Brushes.LightGray, 0.5), rect);
                dc.PushClip(new RectangleGeometry(rect)); dc.DrawDrawing(drawing); dc.Pop();
            }
            drawing = framed;
        }
        if (!drawing.Bounds.IsEmpty)
            Properties.Children.Add(new Image { Source = new DrawingImage(drawing), Height = 90, Stretch = Stretch.Uniform, Margin = new Thickness(8) });
        Button(SceneObject.KindName(owner.Kind) + "の割り当てを開く", () => { selectedId = owner.Id; RefreshAll(); });
    }
}
