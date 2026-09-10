using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ClipVault.Services;

namespace ClipVault.Views;

/// <summary>
/// Read-only text viewer that turns URLs, e-mail addresses and existing file/folder paths into clickable links.
/// Bind <see cref="Text"/>; everything else behaves like a read-only RichTextBox (selection and Ctrl+C work).
/// </summary>
public sealed partial class LinkTextBox : RichTextBox
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(LinkTextBox), new PropertyMetadata(null, (d, _) => ((LinkTextBox)d).Rebuild()));

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Very long clips are shown plain; scanning them for links is not worth the delay.</summary>
    private const int MaxLinkifyLength = 200_000;

    [GeneratedRegex(@"(?<url>\b(?:https?://|www\.)[^\s<>""']+)|(?<mail>\b[\w.+-]+@[\w-]+(?:\.[\w-]+)+\b)|(?<path>\b[A-Za-z]:\\[^\s""<>|*?]+|\\\\[^\s""<>|*?\\]+\\[^\s""<>|*?]+)")]
    private static partial Regex LinkPattern();

    public LinkTextBox()
    {
        IsReadOnly = true;
        IsDocumentEnabled = true; // makes hyperlinks respond to a plain click in a read-only box
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
    }

    private void Rebuild()
    {
        var text = (Text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        var paragraph = new Paragraph { Margin = new Thickness(0) };

        int last = 0;
        if (text.Length <= MaxLinkifyLength)
        {
            foreach (Match m in LinkPattern().Matches(text))
            {
                var (start, length, target) = Classify(m);
                if (target is null) continue;
                AppendText(paragraph, text.Substring(last, start - last));
                var link = new Hyperlink(new Run(text.Substring(start, length))) { Tag = target, ToolTip = target };
                link.Click += (_, _) => LinkOpener.Open(target);
                paragraph.Inlines.Add(link);
                last = start + length;
            }
        }
        AppendText(paragraph, text.Substring(last));

        var doc = new FlowDocument(paragraph) { PagePadding = new Thickness(0), TextAlignment = TextAlignment.Left };
        Document = doc;
    }

    /// <summary>Trims trailing punctuation off a match and decides what clicking it should open.</summary>
    private static (int Start, int Length, string? Target) Classify(Match m)
    {
        var value = m.Value.TrimEnd('.', ',', ';', ':', '!', '?', '\'', '"', ')', ']', '>');
        if (value.Length == 0) return (m.Index, 0, null);

        if (m.Groups["url"].Success)
            return (m.Index, value.Length, value.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + value : value);

        if (m.Groups["mail"].Success)
            return (m.Index, value.Length, "mailto:" + value);

        // Paths are only linked when they exist, so ordinary text with a backslash is left alone.
        try
        {
            if (File.Exists(value) || Directory.Exists(value)) return (m.Index, value.Length, value);
        }
        catch { }
        return (m.Index, 0, null);
    }

    private static void AppendText(Paragraph paragraph, string chunk)
    {
        if (chunk.Length == 0) return;
        var lines = chunk.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) paragraph.Inlines.Add(new LineBreak());
            if (lines[i].Length > 0) paragraph.Inlines.Add(new Run(lines[i]));
        }
    }
}
