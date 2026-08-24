using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PhotoGallery.App.Shell;

/// <summary>What a dialog is telling you, which decides its glyph and colour.</summary>
public enum DialogTone
{
    /// <summary>Something happened that is worth knowing and nothing went wrong.</summary>
    Information,

    /// <summary>A decision, with nothing destroyed either way.</summary>
    Question,

    /// <summary>Something is about to be undone, and can be undone again.</summary>
    Caution,

    /// <summary>Something is about to go for good, or already has.</summary>
    Danger,
}

/// <summary>
/// The app's own message box.
/// </summary>
/// <remarks>
/// <c>MessageBox.Show</c> is drawn by Windows, which means it follows the
/// operating system's theme rather than this app's - and after a re-theme it is
/// the one surface that cannot be made to match. This is a plain modal window,
/// so it is painted from the same brushes as everything else.
///
/// <para>Two things beyond appearance. Its buttons are named after what they do:
/// "Delete" and "Keep" say more at the moment of deciding than "OK" and
/// "Cancel", and the wrong one costs a photograph. And a Windows message box is
/// invisible to UI Automation - it is not among the desktop's children, which is
/// why driving this app from a script needed EnumWindows filtered to class
/// #32770 - whereas this is an ordinary element in the tree.</para>
///
/// <para>Blocking, like the thing it replaces. <c>ShowDialog</c> returns only
/// when the dialog closes, so no caller had to become asynchronous to use
/// it.</para>
/// </remarks>
public partial class AppDialog : Window
{
    private bool _confirmed;

    private AppDialog() => InitializeComponent();

    /// <summary>
    /// States something and waits for it to be read.
    /// </summary>
    public static void Tell(
        Window? owner, string title, string message, DialogTone tone = DialogTone.Information)
    {
        AppDialog dialog = Build(owner, title, message, tone);
        dialog.AddButton("Close", isConfirm: false, isDefault: true);
        dialog.ShowDialog();
    }

    /// <summary>
    /// What every dialog's declining button says.
    /// </summary>
    /// <remarks>
    /// One word, not a parameter, and that is the point. Left to each caller it
    /// became "Keep", "Keep it" and "Stay here" within a single afternoon - four
    /// dialogs, four ways of saying no, and a user who has to read the button
    /// rather than recognise it. Cancel is what Windows says and what everyone
    /// already knows; the interesting half of the decision belongs on the other
    /// button, which is named after the act it performs.
    /// </remarks>
    private const string Decline = "Cancel";

    /// <summary>
    /// Asks something and returns whether it was agreed to.
    /// </summary>
    /// <param name="confirm">
    /// What the agreeing button says. Named after the act rather than "OK",
    /// because at the moment of deciding the label is the last thing read.
    /// </param>
    /// <remarks>
    /// Cancel is the default button. Every question this app asks guards
    /// something that cannot be undone cheaply, and a dialog that acts on a
    /// stray Return is a dialog that eventually deletes a photograph nobody
    /// meant to lose. That mirrors what the message boxes it replaces did.
    /// </remarks>
    public static bool Confirm(
        Window? owner,
        string title,
        string message,
        string confirm,
        DialogTone tone = DialogTone.Question)
    {
        AppDialog dialog = Build(owner, title, message, tone);
        dialog.AddButton(confirm, isConfirm: true, isDefault: false);
        dialog.AddButton(Decline, isConfirm: false, isDefault: true);

        dialog.ShowDialog();
        return dialog._confirmed;
    }

    private static AppDialog Build(Window? owner, string title, string message, DialogTone tone)
    {
        var dialog = new AppDialog
        {
            TitleText = { Text = title },
            MessageText = { Text = message },
            Title = title,
        };

        // Centred on the window it interrupts, or on the screen when there is
        // none - which is start-up, before there is a window to interrupt.
        if (owner is not null && owner.IsLoaded)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        (string glyph, string brush) = tone switch
        {
            DialogTone.Question => ("", "Status.Info"),
            DialogTone.Caution => ("", "Status.Caution"),
            DialogTone.Danger => ("", "Status.Danger"),
            _ => ("", "Status.Info"),
        };

        dialog.Glyph.Text = glyph;
        dialog.Glyph.SetResourceReference(ForegroundProperty, brush);

        return dialog;
    }

    private void AddButton(string text, bool isConfirm, bool isDefault)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 96,
            Margin = new Thickness(Buttons.Children.Count == 0 ? 0 : 10, 0, 0, 0),
            IsDefault = isDefault,
        };

        button.SetResourceReference(
            StyleProperty, isConfirm ? "PrimaryButton" : "SecondaryButton");

        button.Click += (_, _) =>
        {
            _confirmed = isConfirm;
            Close();
        };

        Buttons.Children.Add(button);
    }

    /// <summary>
    /// Escape closes without agreeing, wherever the focus is.
    /// </summary>
    /// <remarks>
    /// Handled here rather than by an IsCancel button because the cancelling
    /// button is also the default one, and a single button cannot be both
    /// without Return and Escape becoming the same key.
    /// </remarks>
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _confirmed = false;
            Close();
        }
    }

    /// <summary>
    /// Lets the dialog be moved, since it has no title bar to move it by.
    /// </summary>
    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
