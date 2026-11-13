using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using GridSketch;

internal static class PartReferenceChecks
{
    public static void Run(string directory)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var doc = new SceneDocument();
        var root = new SceneObject { Kind = ObjectKind.Group, HasLayoutFrame = true, Children = [new() { Kind = ObjectKind.Text, Text = "既定", Name = "キャプション" }] };
        var definition = LibraryPart.FromGroup(root, "絵と説明"); doc.Parts.Add(definition);
        var first = doc.InsertPart(definition, doc.Canvas); var second = doc.InsertPart(definition, doc.Canvas);
        SceneObject Text(SceneObject p) => p.Children.Single().Children.Single();
        string path = Text(first).PartSourcePath!;
        definition.Published.Add(new() { Path = path, Property = "Text", Label = "キャプション" });
        doc.SetPartOverride(first, path, "Text", JsonSerializer.SerializeToElement("個別"));
        definition.Template.Children[0].Text = "更新"; doc.ResolveParts();
        Check(Text(first).Text == "個別" && Text(second).Text == "更新", "Unchanged properties follow definitions; overrides remain independent");
        definition.Template.Children[0].Text = "個別"; doc.ResolveParts();
        Check(first.Part!.Overrides[path].ContainsKey("Text"), "Equal-to-default explicit override remains explicit");
        definition.Template.Children[0].Text = "最新"; doc.ResetPartOverride(first, path, "Text");
        Check(Text(first).Text == "最新" && first.Part.Overrides.Count == 0, "Reset follows the current definition");
        var before = SceneDocument.Restore(doc.Snapshot());
        var stableText = Text(first); stableText.FontSize = 43; stableText.Style.OutlineEnabled = true;
        doc.CapturePartOverrides(before); doc.ResolveParts();
        Check(ReferenceEquals(stableText, Text(first)) && Text(first).FontSize == 43 && Text(second).FontSize != 43
            && first.Part.Overrides[path].ContainsKey("Style.OutlineEnabled"), "Child edits record property-level overrides and preserve editor bindings");
        var dragBefore = SceneDocument.Restore(doc.Snapshot());
        Text(first).X = 25; doc.CapturePartOverrides(dragBefore, force: true); doc.ResolveParts();
        Text(first).X = 0; doc.CapturePartOverrides(dragBefore, force: true); doc.ResolveParts();
        Check(Text(first).X == 0 && !first.Part.Overrides[path].ContainsKey("X"), "Dragging back to the start restores original inheritance");
        var moved = definition.Template.Children.Single(); definition.Template.Children.Clear();
        definition.Template.Children.Add(new() { Kind = ObjectKind.Group, OrganizeOnly = true, Children = [moved] });
        doc.ResolveParts();
        Check(doc.Find(stableText.Id)!.FontSize == 43 && doc.Find(stableText.Id)!.PartSourcePath == path, "Definition reparenting preserves child identity and overrides");
        definition.Template.Children = [moved]; doc.ResolveParts();
        doc.ApplyVisibility(new() { Name = "問題", HiddenIds = [stableText.Id] });
        _ = new SceneRenderer(doc).Export();
        Check(!Text(first).Visible && Text(second).Visible, "Preset visibility survives part resolution during export");
        doc.ApplyVisibility(null);
        var raw = JsonNode.Parse(doc.Snapshot())!;
        Check(raw["Canvas"]!["Children"]![0]!["Children"]!.AsArray().Count == 0, "Instances persist references without cached children");
        string file = Path.Combine(directory, "part-references.gridsketch"); doc.Save(file);
        var loaded = SceneDocument.Load(file);
        Check(loaded.Snapshot() == doc.Snapshot() && Text(loaded.Find(first.Id)!).FontSize == 43, "References, published fields and overrides round-trip");
        var dest = new SceneDocument(); var copied = dest.DuplicateInto([first], dest.Canvas, doc).Single();
        Check(dest.Parts.Count == 1 && Text(copied).FontSize == 43 && dest.Parts[0].Published.Single().Path == path, "Cross-file copy imports definition, published settings and overrides");
        dest.DuplicateInto([second], dest.Canvas, doc);
        Check(dest.Parts.Count == 1, "Matching definition IDs and contents are reused");
        definition.Template.Children[0].Color = "#FF0000"; doc.ResolveParts();
        Check(Text(copied).Color != "#FF0000" && dest.PartConflicts([definition.Id], doc.Parts).Count == 1, "Files are independent and detect definition conflicts");
        var separate = dest.DuplicateInto([first], dest.Canvas, doc).Single();
        Check(dest.Parts.Count == 2 && separate.Part!.DefinitionId != definition.Id && Text(separate).Color == "#FF0000", "Separate policy preserves both definitions");
        var useExisting = dest.DuplicateInto([first], dest.Canvas, doc, PartConflictPolicy.UseDestination).Single();
        Check(dest.Parts.Count == 2 && Text(useExisting).Color == Text(copied).Color && Text(useExisting).FontSize == 43, "Destination policy keeps overrides and destination defaults");
        dest.DuplicateInto([second], dest.Canvas, doc, PartConflictPolicy.UpdateDestination);
        Check(Text(copied).Color == "#FF0000" && Text(copied).FontSize == 43, "Update policy propagates defaults while retaining destination overrides");
        var otherId = LibraryPart.FromGroup(root, definition.Name);
        dest.ImportPartDefinitions([otherId.Id], [otherId]);
        Check(dest.Parts.Count == 3, "Same-name definitions with different IDs stay separate");

        // Nested references and outer-instance overrides must survive transfer, reload and detachment.
        var outerGroup = new SceneObject { Kind = ObjectKind.Group, HasLayoutFrame = true };
        doc.Canvas.Children.Add(outerGroup); var inner = doc.InsertPart(definition, outerGroup);
        var outerDefinition = LibraryPart.FromGroup(outerGroup, "入れ子", doc.Parts); doc.Parts.Add(outerDefinition);
        var outer = doc.InsertPart(outerDefinition, doc.Canvas);
        var nestedText = doc.All().Single(n => n.Kind == ObjectKind.Text && n.PartOwnerId == outer.Id);
        doc.SetPartOverride(outer, nestedText.PartSourcePath!, "Text", JsonSerializer.SerializeToElement("外側の変更"));
        doc.Validate();
        var nestedDestination = new SceneDocument();
        var nestedCopy = nestedDestination.DuplicateInto([outer], nestedDestination.Canvas, doc).Single();
        Check(nestedDestination.Parts.Count == 2 && nestedDestination.All().Single(n => n.Kind == ObjectKind.Text).Text == "外側の変更", "Nested transfer includes dependencies and outer overrides");
        var bundle = Path.Combine(directory, "nested.gspart"); PartFile.Export(outerDefinition, bundle, doc.Parts);
        var imported = PartFile.Import(bundle);
        Check(imported.Dependencies.Count == 1, "Library export embeds nested definitions");
        var stateScene = new SceneDocument(); var stateRoot = new SceneObject { Kind = ObjectKind.Group, HasLayoutFrame = true };
        var stateGrid = SceneDocument.CreateGrid("空のグリッド", 2, 2, 48); stateRoot.Children.Add(stateGrid);
        var selector = new SceneObject { Kind = ObjectKind.CellSelection, Children = [new() { Kind = ObjectKind.Shape }] }; stateGrid.Children.Add(selector);
        SceneDocument.AssignNewState(selector, selector.Children[0]);
        var stateDefinition = LibraryPart.FromGroup(stateRoot, "切り替え"); stateScene.Parts.Add(stateDefinition);
        var stateInstance = stateScene.InsertPart(stateDefinition, stateScene.Canvas);
        var effectiveSelector = stateScene.All().Single(n => n.IsStateSelector);
        var stateBefore = SceneDocument.Restore(stateScene.Snapshot()); effectiveSelector.States[0].Name = "変更";
        stateScene.CapturePartOverrides(stateBefore); stateScene.ResolveParts();
        var stateDestination = new SceneDocument(); stateDestination.DuplicateInto([stateInstance], stateDestination.Canvas, stateScene);
        var copiedSelector = stateDestination.All().Single(n => n.IsStateSelector);
        Check(copiedSelector.States[0].ObjectId == copiedSelector.Children[0].Id && copiedSelector.States[0].Name == "変更", "Overridden selector references remap to copied instance children");
        nestedDestination.DetachPart(nestedCopy);
        Check(nestedDestination.All().All(n => !n.IsPart && n.PartOwnerId is null)
            && nestedDestination.All().Single(n => n.Kind == ObjectKind.Text).Text == "外側の変更", "Detach retains effective nested content as independent objects");
        doc.DetachPart(first);
        Check(!first.IsPart && Text(first).FontSize == 43, "Detach retains child overrides");
        var invalid = SceneDocument.Restore(doc.Snapshot()); var instance = invalid.Find(second.Id)!;
        var baseline = SceneDocument.Restore(invalid.Snapshot()); instance.Children[0].Children.Clear();
        try { invalid.CapturePartOverrides(baseline); throw new Exception("Instance structural edit accepted"); } catch (InvalidOperationException) { }
        var cycle = LibraryPart.FromGroup(new() { Kind = ObjectKind.Group }, "循環");
        cycle.Template.Children.Add(new() { Kind = ObjectKind.Group, Part = new() { DefinitionId = cycle.Id } });
        try { SceneDocument.PartDependencies([cycle.Id], [cycle]); throw new Exception("Dependency cycle accepted"); } catch (InvalidDataException) { }

        // Exercise real property editors, undo and definition editing tabs.
        var window = new MainWindow(); window.Show(); window.UpdateLayout();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        Call("AddProjectTab", doc, null); var ownerTab = Field("activeTab")!;
        Set("selectedId", second.Id); Call("RefreshAll"); window.UpdateLayout();
        var textEditor = PropertyChecks.Find<TextBox>(window, "キャプション");
        textEditor.Text = "画面から変更"; Call("CommitPropertyEdit");
        Check(Text(second).Text == "画面から変更" && second.Part!.Overrides[path].ContainsKey("Text"), "Published caption editor creates an override");
        window.UpdateLayout();
        var preview = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        preview.Render(window);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(preview));
        using (var output = File.Create(Path.Combine(directory, "part-reference-ui.png"))) png.Save(output);
        Call("Undo"); var current = (SceneDocument)Field("document")!;
        Check(Text(current.Find(second.Id)!).Text == "最新", "Published edit is undoable");
        Set("selectedId", current.Find(second.Id)!.Children.Single().Children.Single().Id); Call("RefreshAll"); window.UpdateLayout();
        PropertyChecks.Find<TextBox>(window, "文章").Text = "子から変更"; Call("CommitPropertyEdit");
        Check(Text(current.Find(second.Id)!).Text == "子から変更", "Ordinary child editor creates a lasting override");
        current.ResetPartOverride(current.Find(second.Id)!);
        Set("savedState", current.Snapshot()); Call("EditPartTab", current.Parts.Single(p => p.Id == definition.Id));
        var editDoc = (SceneDocument)Field("document")!;
        editDoc.Canvas.Children[0].Children[0].Text = "定義編集";
        Check((bool)Call("ApplyPartEdit")!, "Edited definition applies");
        Set("savedState", editDoc.Snapshot()); Call("SwitchTab", ownerTab);
        current = (SceneDocument)Field("document")!;
        Check(Text(current.Find(second.Id)!).Text == "定義編集", "Applying definition updates existing references");
        Set("savedState", current.Snapshot()); window.Close();
        Console.WriteLine("PASS: Part inheritance, explicit overrides, reset, persistence, cross-file conflict policies, dependencies, detach, published UI, child editing, undo and definition tabs");
    }
}
