using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace AdLib.Controls;

[TemplatePart(Name = "PART_Button", Type = typeof(Button), IsRequired = true),
 TemplatePart(Name = "PART_Popup", Type = typeof(Popup), IsRequired = true), PseudoClasses(":flyout-open")]
public class ExtendedChoiceBox : SelectingItemsControl
{
    public static readonly StyledProperty<Control?> TopItemProperty =
        AvaloniaProperty.Register<ExtendedChoiceBox, Control?>(nameof(TopItem));

    public static readonly StyledProperty<Control?> BottomItemProperty =
        AvaloniaProperty.Register<ExtendedChoiceBox, Control?>(nameof(BottomItem));

    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<ExtendedChoiceBox, string?>(nameof(PlaceholderText));

    public static readonly DirectProperty<ExtendedChoiceBox, object?> SelectionBoxItemProperty =
        AvaloniaProperty.RegisterDirect<ExtendedChoiceBox, object?>(
            nameof(SelectionBoxItem),
            o => o.SelectionBoxItem
        );

    private object? _selectionBoxItem;

    private Popup? _popup;
    private Button? _button;

    public Control? TopItem
    {
        get => this.GetValue(TopItemProperty);
        set => this.SetValue(TopItemProperty, value);
    }

    public Control? BottomItem
    {
        get => this.GetValue(BottomItemProperty);
        set => this.SetValue(BottomItemProperty, value);
    }

    public string? PlaceholderText
    {
        get => this.GetValue(PlaceholderTextProperty);
        set => this.SetValue(PlaceholderTextProperty, value);
    }

    public object? SelectionBoxItem
    {
        get => this._selectionBoxItem;
        private set => this.SetAndRaise(SelectionBoxItemProperty, ref this._selectionBoxItem, value);
    }

    static ExtendedChoiceBox()
    {
        SelectionModeProperty.OverrideDefaultValue<ExtendedChoiceBox>(SelectionMode.Single);
    }

    public ExtendedChoiceBox() => this.SelectionChanged += this.OnSelectionChanged;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        // unbinds old events then rebinds to the newly-found buttons when a new template is applied
        if (this._button is not null) this._button.Click -= this.OnButtonClick;
        if (this._popup is not null) this._popup.Closed -= this.OnPopupClosed;

        // names are required to exist
        this._button = e.NameScope.Find<Button>("PART_Button");
        this._popup = e.NameScope.Find<Popup>("PART_Popup");

        if (this._button is not null) this._button.Click += this.OnButtonClick;
        if (this._popup is not null) this._popup.Closed += this.OnPopupClosed;
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        // toggles popup, so pseudoclass needs to be reset
        if (this._popup is null) return;
        this._popup.IsOpen = !this._popup.IsOpen;
        this.PseudoClasses.Set(":flyout-open", this._popup.IsOpen);
    }

    // can be closed without clicking the button so reset pseudoclass
    private void OnPopupClosed(object? sender, EventArgs e) => this.PseudoClasses.Set(":flyout-open", false);

    private void OnSelectionChanged(object? _, SelectionChangedEventArgs e)
    {
        this.SelectionBoxItem = this.SelectedItem;
        // auto closes popup
        if (this._popup is null) return;
        this._popup.IsOpen = false;
        this.PseudoClasses.Set(":flyout-open", false);
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) =>
        new ExtendedChoiceBoxItem();

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey) =>
        this.NeedsContainer<ExtendedChoiceBoxItem>(item, out recycleKey);

    // Called by ExtendedChoiceBoxItem on click
    internal void SelectItem(object? item) => this.SelectedItem = item;
}
