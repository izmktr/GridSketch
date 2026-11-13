using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class ContainerChecks
{
    public static void Run(MainWindow window)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        bool Same(Matrix a, Matrix b) => (a.Transform(new Point(0, 0)) - b.Transform(new Point(0, 0))).Length < 1e-6
            && (a.Transform(new Point(10, 20)) - b.Transform(new Point(10, 20))).Length < 1e-6;
        var doc = new SceneDocument();
        var grid = SceneDocument.CreateGrid("空のグリッド", 5, 5, 48); doc.Canvas.Children.Add(grid);
        var a = new SceneObject { Kind = ObjectKind.Shape, Width = 20, Height = 30, Name = "A" };
        var b = new SceneObject { Kind = ObjectKind.Shape, Width = 40, Height = 10, Name = "B", Visible = false };
        SceneDocument.InitializeGridCoordinates(a, 2, 3); SceneDocument.InitializeGridCoordinates(b, 4, 2);
        grid.Children = [a, b];
        var aw = doc.World(a); var bw = doc.World(b);
        var group = doc.WrapContainers([a, b], false);
        Check(group.IsOrganizer && group.Label.Contains("[グループ]") && a.GridColumn == 2 && b.GridRow == 2
            && Same(aw, doc.World(a)) && Same(bw, doc.World(b)), "Grouping preserves cell coordinates, order and invisible contents");
        string id = group.Id; group.Name = "保持する名前";
        doc.VisibilityPresets.Add(new VisibilityPreset { Name = "問題", HiddenIds = [group.Id] });
        var bounds = Rect.Transform(SceneDocument.OwnLayoutRect(a), aw); bounds.Union(Rect.Transform(SceneDocument.OwnLayoutRect(b), bw));
        doc.ConvertContainer(group, false);
        Check(group.IsFrame && group.Id == id && group.Name == "保持する名前" && group.Width == bounds.Width && group.Height == bounds.Height,
            "Conversion retains identity and wraps all content bounds");
        Check(doc.VisibilityPresets[0].HiddenIds.Contains(group.Id), "Conversion keeps visibility preset references");
        Check(a.GridColumn is null && b.GridRow is null && Same(aw, doc.World(a)) && Same(bw, doc.World(b))
            && doc.All().All(n => n.PreservedTransform is null), "Frame children become editable px placements without hidden matrices");
        group.Angle = 27; aw = doc.World(a); bw = doc.World(b);
        doc.ConvertContainer(group, true);
        Check(group.IsOrganizer && group.Angle == 0 && Same(aw, doc.World(a)) && Same(bw, doc.World(b)), "Rotated frame converts without visual change");
        doc.UnwrapContainer(group);
        Check(grid.Children.SequenceEqual(new[] { a, b }) && Same(aw, doc.World(a)), "Organizer unwrap preserves stacking and placement");
        var frame = doc.WrapContainers([a, b], true); aw = doc.World(a); bw = doc.World(b);
        doc.UnwrapContainer(frame);
        Check(Same(aw, doc.World(a)) && Same(bw, doc.World(b)), "Frame wrapping and unwrapping preserve rotated cell placements");

        var border = new SceneObject { Kind = ObjectKind.Border }; grid.Children.Add(border);
        var blocked = new SceneObject { Kind = ObjectKind.Group, HasLayoutFrame = true }; grid.Children.Add(blocked);
        Check(!doc.ReparentTargets(border).Contains(blocked) && !doc.DuplicateTargets([border]).Contains(blocked)
            && blocked.IsFrame, "Invalid targets excluded without automatic conversion");
        var dependent = doc.WrapContainers([border], false);
        string before = doc.Snapshot();
        Check(doc.ConversionError(dependent, false) is not null && doc.FrameWrapError([dependent]) is not null && doc.Snapshot() == before,
            "Grid-dependent conversion disabled without mutation");

        var selector = new SceneObject { Kind = ObjectKind.CellSelection, UseObjectLayout = true }; grid.Children.Add(selector);
        var template = new SceneObject { Kind = ObjectKind.Group, OrganizeOnly = true,
            Children = [new() { Kind = ObjectKind.Shape, Width = 18, Height = 20, UseParentLayout = true }] };
        selector.Children.Add(template); SceneDocument.AssignNewState(selector, template);
        SceneDocument.SetCellState(selector, new(1, 1), selector.States[0].Id);
        var tw = doc.World(template.Children[0]); var stateId = selector.States[0].Id;
        doc.ConvertContainer(template, false);
        Check(selector.States[0].ObjectId == template.Id && selector.StateCells[0].StateId == stateId
            && Same(tw, doc.World(template.Children[0])) && doc.UnwrapError(template) is not null,
            "Template conversion keeps placement and state references; unwrap remains forbidden");
        var part = LibraryPart.FromGroup(template, "フレーム部品");
        Check(part.CreateCopy().IsFrame, "Parts retain frame type");
        doc.ConvertContainer(template, true);
        Check(LibraryPart.FromGroup(template, "グループ部品").CreateCopy().IsOrganizer, "Parts retain organizer type");
        template.Children[0].Locked = true;
        Check(doc.ConversionError(template, false) is not null, "Locked child blocks conversion"); template.Children[0].Locked = false;

        var unsafeFrame = new SceneObject { Kind = ObjectKind.Group, HasLayoutFrame = true, Width = 100, Height = 100,
            UseParentLayout = true, LayoutSizing = "広げる", Children = [new() { Kind = ObjectKind.Text, Text = "文字" }] };
        var host = new SceneObject { Kind = ObjectKind.Group, HasLayoutFrame = true, Width = 200, Height = 100, Children = [unsafeFrame] };
        doc.Canvas.Children.Add(host); before = doc.Snapshot();
        Check(doc.ConversionError(unsafeFrame, true) is not null && doc.UnwrapError(unsafeFrame) is not null && doc.Snapshot() == before,
            "Nonuniform text scaling safely blocks conversion and unwrap");
        try { doc.ConvertContainer(unsafeFrame, true); throw new Exception("Unsafe conversion accepted"); } catch (InvalidOperationException) { }
        Check(doc.Snapshot() == before, "Rejected conversion is atomic");

        var moving = new SceneObject { Kind = ObjectKind.Group, OrganizeOnly = true,
            Children = [new() { Kind = ObjectKind.Shape, Width = 20, Height = 10, X = 4, Y = 8 }] };
        doc.Canvas.Children.Add(moving); var movingChild = moving.Children[0]; var mw = doc.World(movingChild);
        doc.ReparentMany([moving], host);
        Check(moving.IsOrganizer && Same(mw, doc.World(movingChild)) && movingChild.PreservedTransform is null,
            "Reparenting an organizer updates editable child positions without changing its type");
        doc.ReparentMany([moving], grid); double? column = movingChild.GridColumn;
        var secondGrid = SceneDocument.CreateGrid("空のグリッド", 5, 5, 64); doc.Canvas.Children.Add(secondGrid);
        doc.ReparentMany([moving], secondGrid);
        Check(movingChild.GridColumn == column && doc.UsesGridCoordinates(movingChild), "Organizer moving between grids keeps cell coordinate values");

        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        void Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        void Capture(string name)
        {
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(Path.GetTempPath(), name)); png.Save(stream);
        }
        Set("document", doc); Set("selectedId", doc.Canvas.Id); Call("RefreshAll");
        host.LayoutHasInner = true; host.LayoutInsetX = host.LayoutInsetY = 5;
        var inherited = new SceneObject { Kind = ObjectKind.Group, OrganizeOnly = true }; host.Children.Add(inherited);
        var fresh = new SceneObject { Kind = ObjectKind.Shape }; Call("InitializeObjectLayout", fresh, inherited);
        Check(fresh.LayoutRegion == "親の内側" && fresh.Width == 90, "New children inherit frame margins through organizing groups");
        Call("AddClick", new MenuItem { Tag = "Group" }, new RoutedEventArgs());
        var addedGroup = doc.Canvas.Children[^1];
        Check(addedGroup.IsOrganizer && !addedGroup.HasLayoutFrame, "Add group defaults to organizer");
        Capture("container-group.png");
        Set("selectedId", doc.Canvas.Id); Call("RefreshAll");
        Call("AddClick", new MenuItem { Tag = "Frame" }, new RoutedEventArgs());
        var addedFrame = doc.Canvas.Children[^1];
        Check(addedFrame.IsFrame && addedFrame.HasLayoutFrame && addedFrame.Width == 100 && addedFrame.Height == 100
            && addedFrame.Label.Contains("[フレーム]"), "Add frame defaults to 100 by 100 with distinct label");
        Capture("container-frame.png");
        Check(PropertyHelpWindow.TopicName(addedFrame) == "フレーム" && PropertyHelpWindow.TopicName(addedGroup) == "グループ", "Separate help topics");
        var restored = SceneDocument.Restore(doc.Snapshot());
        Check(restored.Find(addedFrame.Id)!.IsFrame && restored.Find(addedGroup.Id)!.IsOrganizer, "Both container kinds survive serialization");
        Set("savedState", doc.Snapshot());
        Console.WriteLine("PASS: Distinct groups/frames, wrapping, conversion, bounds, editable placement, state references, parts, lock restrictions and persistence");
    }
}
