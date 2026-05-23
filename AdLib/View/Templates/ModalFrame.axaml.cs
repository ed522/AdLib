using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;

namespace AdLib.View.Templates;

public partial class ModalFrame : ContentControl
{
    public enum ModalButtonType
    {
        None = 0,
        Ok,
        CancelOk,
        Continue,
        CancelContinue,
        YesNo,
        YesNoCancel,
        AbortRetryIgnore,
        CancelTryAgainContinue,
        Close,
        CancelRetry,
    }

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<ModalFrame, string>(nameof(Title));

    public static readonly StyledProperty<bool> CanCloseWithoutActionProperty =
        AvaloniaProperty.Register<ModalFrame, bool>(nameof(CanCloseWithoutAction));

    public static readonly StyledProperty<ICommand> CloseCommandProperty =
        AvaloniaProperty.Register<ModalFrame, ICommand>(nameof(CloseCommand));


    public ModalFrame() { this.InitializeComponent(); }

    public string Title
    {
        get => this.GetValue(TitleProperty);
        set => this.SetValue(TitleProperty, value);
    }

    public bool CanCloseWithoutAction
    {
        get => this.GetValue(CanCloseWithoutActionProperty);
        set => this.SetValue(CanCloseWithoutActionProperty, value);
    }

    public ICommand CloseCommand
    {
        get => this.GetValue(CloseCommandProperty);
        set => this.SetValue(CloseCommandProperty, value);
    }
}
