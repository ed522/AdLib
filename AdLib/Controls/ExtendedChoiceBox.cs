using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace AdLib.Controls;

public class ExtendedChoiceBox : SelectingItemsControl
{
    public static readonly StyledProperty<string> PlaceholderProperty =
        AvaloniaProperty.Register<ModalFrame, string>(nameof(Placeholder));

    public static readonly StyledProperty<ICommand> OnSelectionChangedProperty =
        AvaloniaProperty.Register<ModalFrame, ICommand>(nameof(OnSelectionChanged));

    public static readonly StyledProperty<Control> TopItemProperty =
        AvaloniaProperty.Register<ModalFrame, Control>(nameof(TopItem));

    public static readonly StyledProperty<Control> BottomItemProperty =
        AvaloniaProperty.Register<ModalFrame, Control>(nameof(BottomItem));

    public string Placeholder
    {
        get => this.GetValue(PlaceholderProperty);
        set => this.SetValue(PlaceholderProperty, value);
    }

    public ICommand OnSelectionChanged
    {
        get => this.GetValue(OnSelectionChangedProperty);
        set => this.SetValue(OnSelectionChangedProperty, value);
    }

    public Control TopItem
    {
        get => this.GetValue(TopItemProperty);
        set => this.SetValue(TopItemProperty, value);
    }

    public Control BottomItem
    {
        get => this.GetValue(BottomItemProperty);
        set => this.SetValue(BottomItemProperty, value);
    }
}
