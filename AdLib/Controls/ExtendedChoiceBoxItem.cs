using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace AdLib.Controls;

// Extending Button means Avalonia automatically sets Content + ContentTemplate
// from the parent's ItemsSource and ItemTemplate during container prep.
public class ExtendedChoiceBoxItem : Button
{
    public ExtendedChoiceBoxItem() => this.Click += this.OnClicked;

    private void OnClicked(object? sender, RoutedEventArgs e)
    {
        if (this.FindLogicalAncestorOfType<ExtendedChoiceBox>() is { } parent)
        {
            parent.SelectItem(parent.ItemFromContainer(this));
        }
    }
}
