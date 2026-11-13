using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace GridSketch;

public sealed class GridTextExchangeWindow : Window
{
    private readonly Func<(SceneDocument Document, SceneObject Node)> target;
    private readonly Action<string, GridTextFormat> apply;
    private readonly TextBlock heading = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock hint = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBox editor = new() { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new FontFamily("Consolas"), FontSize = 16, MinHeight = 180 };
    private readonly ComboBox formats = new() { ItemsSource = new[] { "TSV（タブ区切り）", "CSV（カンマ区切り）", "文字詰め（複数文字は [ ]）" }, MinWidth = 260, SelectedIndex = 0 };
    private readonly Button reflect;
    private GridTextFormat format = GridTextFormat.Tsv;
    private string generated = "";
    private bool changing;
    private readonly bool cellStates;
    private string[] stateIds = [];
    private readonly TextBlock stateNames = new() { TextWrapping = TextWrapping.Wrap };

    public GridTextExchangeWindow(string title, Func<(SceneDocument Document, SceneObject Node)> target, Action<string, GridTextFormat> apply, bool cellStates = false)
    {
        this.target = target; this.apply = apply; this.cellStates = cellStates;
        Title = (cellStates ? "マス切り替え" : "グリッド文字") + "の入出力 — " + title; Width = 760; Height = cellStates ? 680 : 580; MinWidth = 560; MinHeight = cellStates ? 560 : 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(16) }; Content = panel;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(heading); top.Children.Add(formats); top.Children.Add(hint);
        if (cellStates) top.Children.Add(new Expander { Header = "状態番号の対応（読み込み時の一覧順）", IsExpanded = true,
            Content = new ScrollViewer { MaxHeight = 90, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stateNames }, Margin = new Thickness(0, 0, 0, 8) });
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom);
        bottom.Children.Add(status);
        var buttons = new WrapPanel(); bottom.Children.Add(buttons);
        Button Add(string label, Action action)
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 5, 10, 5) };
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) => Run(action); buttons.Children.Add(button); return button;
        }
        Add("盤面から読み直す", ReadBoard);
        Add("すべてコピー", () => { Clipboard.SetText(editor.Text); Message("テキスト全体をコピーしました。"); });
        Add("貼り付け", () => { editor.Text = Clipboard.GetText(); editor.Focus(); editor.CaretIndex = 0; });
        reflect = Add("盤面に反映", () =>
        {
            if (cellStates && !target().Node.States.Select(s => s.Id).SequenceEqual(stateIds))
                throw new InvalidOperationException("状態の追加・削除・並べ替えが行われました。「盤面から読み直す」で状態番号の対応を更新してから入力してください。必要なテキストは先にコピーしてください。");
            apply(editor.Text, format);
            generated = editor.Text;
            Message("対象レイヤーの" + (cellStates ? "マスの割り当て" : "文字") + "を置き換えました。本体のCtrl+Zで元に戻せます。");
            RefreshTarget();
        });
        Add("閉じる", Close);
        panel.Children.Add(editor);
        AutomationProperties.SetName(formats, "入出力形式"); AutomationProperties.SetName(editor, "盤面テキスト");
        AutomationProperties.SetName(status, "入出力の結果");
        editor.TextChanged += (_, _) => { if (!changing) Message("テキストを編集しました。「盤面に反映」で対象レイヤー全体を置き換えます。"); };
        formats.SelectionChanged += (_, _) =>
        {
            if (changing || formats.SelectedIndex < 0) return;
            var previous = format; format = (GridTextFormat)formats.SelectedIndex;
            try
            {
                if (editor.Text == generated) ReadBoard();
                else Message("入力中のテキストは保持しています。選択した形式として読み取ります。");
            }
            catch (Exception ex)
            {
                format = previous; changing = true; formats.SelectedIndex = (int)previous; changing = false;
                Message(ex.Message, error: true);
            }
            UpdateHint();
        };
        Activated += (_, _) => RefreshTarget();
        UpdateHint(); Run(ReadBoard);
    }

    private void UpdateHint() => hint.Text = cellStates
        ? "なしは0、状態は一覧順に1からの番号です。" + (format == GridTextFormat.Compact ? "文字詰めは0～9をそのまま、2桁以上を[10]のように囲みます。" : "状態番号を" + (format == GridTextFormat.Csv ? "カンマ" : "タブ") + "で区切ります。")
            + "\n反映はマスの割り当て全体の置き換えです。行数・列数を盤面に合わせてください。状態の名前・順序・表示オブジェクトは変更しません。"
        : (format == GridTextFormat.Compact
        ? "1文字はそのまま、複数文字は [12]、空マスは []。半角スペースも1マスの文字です。[・] を含む盤面はCSV・TSVを使用してください。"
        : "1行が盤面の1行です。空マスは空の項目で表します。区切り文字・引用符を含む項目は引用符で囲み、文字中の引用符は二重にします。")
        + "\n反映は対象レイヤー全体の置き換えです。行数・列数を盤面に合わせてください。文字の色・配置・マスごとの書式は保持します。";

    private void ReadBoard()
    {
        var (scene, node) = target(); var grid = scene.GridFor(node) ?? throw new InvalidOperationException("対象のグリッドがありません。");
        string text = cellStates ? CellStateTextTransfer.Export(node, grid, format) : GridTextTransfer.Export(node, grid, format);
        if (cellStates)
        {
            stateIds = node.States.Select(s => s.Id).ToArray();
            stateNames.Text = "0：なし\n" + string.Join("\n", node.States.Select((s, i) =>
                $"{i + 1}：{node.Children.FirstOrDefault(c => c.Id == s.ObjectId)?.Name ?? s.Name + "（表示オブジェクト未割り当て）"}"));
        }
        changing = true; editor.Text = generated = text; changing = false;
        RefreshTarget(); Message("盤面の" + (cellStates ? "状態番号" : "文字") + "を読み込みました。編集して反映するか、コピーして使用できます。");
    }
    private void RefreshTarget()
    {
        try
        {
            var (scene, node) = target(); var grid = scene.GridFor(node) ?? throw new InvalidOperationException("対象のグリッドがありません。");
            bool editable = scene.Editable(node) && scene.Shown(node);
            heading.Text = $"対象：{node.Name} ／ {grid.Columns}列 × {grid.Rows}行" + (editable ? "" : "（非表示・ロック中：読み出しのみ）");
            reflect.IsEnabled = editable;
        }
        catch (Exception ex) { reflect.IsEnabled = false; heading.Text = ex.Message; }
    }
    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { Message(ex.Message, error: true); RefreshTarget(); }
    }
    private void Message(string text, bool error = false)
    { status.Text = text; status.Foreground = error ? Brushes.Firebrick : Brushes.DimGray; }
}
