using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Island.Services;

namespace Island;

/// <summary>Formatting commands for the notes editor, including «☐ задача» checklists.</summary>
public partial class IslandWindow
{
    private const string Unchecked = "☐ ";
    private const string Checked = "☑ ";

    private IEnumerable<Paragraph> SelectedParagraphs()
    {
        var start = NoteEditor.Selection.Start.Paragraph;
        var end = NoteEditor.Selection.End.Paragraph;
        Block? current = start;
        while (current is not null)
        {
            if (current is Paragraph p) yield return p;
            if (current == end) yield break;
            current = current.NextBlock;
        }
    }

    private static Run? FirstRun(Paragraph paragraph)
    {
        foreach (var inline in paragraph.Inlines)
        {
            if (FirstRunIn(inline) is { } run) return run;
        }

        return null;
    }

    private static Run? FirstRunIn(Inline inline) => inline switch
    {
        Run r => r,
        Span s => s.Inlines.Select(FirstRunIn).FirstOrDefault(r => r is not null),
        _ => null,
    };

    private static bool IsChecklist(Run? run) =>
        run is not null && (run.Text.StartsWith(Unchecked, StringComparison.Ordinal) || run.Text.StartsWith(Checked, StringComparison.Ordinal));

    private void NoteChecklist_Click(object sender, RoutedEventArgs e)
    {
        if (NoteEditor.Document.Blocks.Count == 0)
        {
            var first = new Paragraph(new Run(Unchecked));
            NoteEditor.Document.Blocks.Add(first);
            NoteEditor.CaretPosition = first.ContentEnd;
            NoteEditor.Focus();
            return;
        }

        foreach (var paragraph in SelectedParagraphs().ToList())
        {
            var run = FirstRun(paragraph);
            if (IsChecklist(run))
            {
                run!.Text = run.Text[2..];
                SetStrike(paragraph, run, false);
            }
            else if (run is not null)
            {
                run.Text = Unchecked + run.Text;
            }
            else
            {
                paragraph.Inlines.Add(new Run(Unchecked));
            }
        }

        NoteEditor.Focus();
    }

    /// <summary>Clicking the box at the start of a «☐ …» line toggles it and strikes the rest of the line.</summary>
    private void NoteEditor_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (!NoteEditor.Selection.IsEmpty) return;
            var position = NoteEditor.GetPositionFromPoint(e.GetPosition(NoteEditor), false);
            if (position?.Paragraph is not { } paragraph) return;

            var run = FirstRun(paragraph);
            if (!IsChecklist(run)) return;
            if (position.CompareTo(run!.ContentStart.GetPositionAtOffset(1)) > 0) return;

            var becomesChecked = run.Text.StartsWith(Unchecked, StringComparison.Ordinal);
            run.Text = (becomesChecked ? Checked : Unchecked) + run.Text[2..];
            SetStrike(paragraph, run, becomesChecked);
            e.Handled = true;
        }
        catch
        {
            // A click that lands between layout updates is not worth a crash.
        }
    }

    private void SetStrike(Paragraph paragraph, Run run, bool on)
    {
        var restStart = run.ContentStart.GetPositionAtOffset(2);
        if (restStart is null) return;

        var rest = new TextRange(restStart, paragraph.ContentEnd);
        rest.ApplyPropertyValue(Inline.TextDecorationsProperty, on ? TextDecorations.Strikethrough : new TextDecorationCollection());
        rest.ApplyPropertyValue(TextElement.ForegroundProperty, on ? (Brush)FindResource("SubBrush") : (Brush)FindResource("TextBrush"));
    }

    /// <summary>Enter on a checklist line starts the next task; Enter on an empty task ends the list.</summary>
    private void NoteEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;

        var paragraph = NoteEditor.CaretPosition.Paragraph;
        if (paragraph is null) return;
        var run = FirstRun(paragraph);
        if (!IsChecklist(run)) return;

        var text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim();
        if (text.Length <= 1)
        {
            run!.Text = run.Text[2..];
            e.Handled = true;
            return;
        }

        // Mid-line Enter keeps the default split; only the end of the line starts a new task.
        var following = NoteEditor.CaretPosition.GetNextInsertionPosition(LogicalDirection.Forward);
        if (following is not null && following.Paragraph == paragraph) return;

        var next = new Paragraph(new Run(Unchecked));
        paragraph.SiblingBlocks.InsertAfter(paragraph, next);
        NoteEditor.CaretPosition = next.ContentEnd;
        e.Handled = true;
    }

    private void NoteStrike_Click(object sender, RoutedEventArgs e)
    {
        var selection = NoteEditor.Selection;
        if (!selection.IsEmpty)
        {
            var current = selection.GetPropertyValue(Inline.TextDecorationsProperty);
            var has = current is TextDecorationCollection c && c.Any(d => d.Location == TextDecorationLocation.Strikethrough);
            selection.ApplyPropertyValue(Inline.TextDecorationsProperty, has ? new TextDecorationCollection() : TextDecorations.Strikethrough);
        }

        NoteEditor.Focus();
    }

    private void NoteClearFormat_Click(object sender, RoutedEventArgs e)
    {
        TextRange range;
        if (!NoteEditor.Selection.IsEmpty) range = NoteEditor.Selection;
        else if (NoteEditor.CaretPosition.Paragraph is { } p) range = new TextRange(p.ContentStart, p.ContentEnd);
        else return;

        range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
        range.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
        range.ApplyPropertyValue(Inline.TextDecorationsProperty, new TextDecorationCollection());
        range.ApplyPropertyValue(TextElement.ForegroundProperty, (Brush)FindResource("TextBrush"));
        NoteEditor.Focus();
    }

    private void NoteRemind_Click(object sender, RoutedEventArgs e)
    {
        var title = _currentNote?.Title ?? "";
        ReminderBox.Text = title.Length == 0 ? "" : title + " ";
        ReminderBox.CaretIndex = ReminderBox.Text.Length;
        FocusLater(ReminderBox);
    }

    private void NoteCopy_Click(object sender, RoutedEventArgs e)
    {
        var text = new TextRange(NoteEditor.Document.ContentStart, NoteEditor.Document.ContentEnd).Text.TrimEnd();
        if (text.Length == 0) return;
        SilentSetClipboard(text);
        if (sender is Button button) FlashDone(button);
    }

    private static void FlashDone(Button button)
    {
        var original = button.Content;
        button.Content = "";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            button.Content = original;
        };
        timer.Start();
    }
}
