using System.Windows;
using System.Windows.Controls;
using ClipVault.Models;

namespace ClipVault.Views;

/// <summary>Picks a list-row or preview template based on the item's content kind.</summary>
public sealed class ClipTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Text { get; set; }
    /// <summary>Optional template for text that <see cref="ClipItem.IsCode"/> flags as code; falls back to <see cref="Text"/>.</summary>
    public DataTemplate? Code { get; set; }
    public DataTemplate? Image { get; set; }
    public DataTemplate? Files { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is ClipItem c ? c.Kind switch
        {
            ClipKind.Text => c.IsCode && Code is not null ? Code : Text,
            ClipKind.Image => Image,
            ClipKind.Files => Files,
            _ => null,
        } : null;
}
