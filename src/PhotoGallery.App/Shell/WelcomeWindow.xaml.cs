using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PhotoGallery.Application.Ports;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.App.Shell;

/// <summary>
/// The set-up screen. It runs before anything else exists, because the app
/// cannot build its services until it knows where the database lives.
/// </summary>
/// <remarks>
/// One decision, pre-filled with a sensible answer, and a hint that changes to
/// describe what will actually happen to the folder in front of you - so the
/// outcome is never a surprise after the button is pressed.
/// </remarks>
public partial class WelcomeWindow : Window
{
    private static readonly string[] s_photoPatterns =
        ["*.jpg", "*.jpeg", "*.png", "*.heic", "*.heif", "*.webp"];

    public WelcomeWindow(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        InitializeComponent();

        // This is the first window a new user sees, so a caption in the wrong
        // theme is the first thing they would notice.
        TitleBarPainter.Follow(this);

        // The last folder if there is one, and otherwise nothing. No suggested
        // path: the app has no business guessing where someone's pictures ought
        // to live, and any guess it made would be a location baked into the
        // build rather than a choice. Continue stays disabled until a folder is
        // given.
        FolderBox.Text = config.LastWorkingFolder is { Length: > 0 } last && Directory.Exists(last)
            ? last
            : string.Empty;

        UpdateHint();

        Loaded += (_, _) =>
        {
            FolderBox.Focus();
            FolderBox.CaretIndex = FolderBox.Text.Length;
        };
    }

    /// <summary>The folder chosen, once the window closes successfully.</summary>
    public string? ChosenFolder { get; private set; }

    /// <summary>
    /// True when the chosen folder already holds photos, so the caller can
    /// register it as a photo source as well as the working folder.
    /// </summary>
    public bool FolderHasPhotos { get; private set; }

    private void OnBrowseClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Pick a working folder" };

        string current = FolderBox.Text.Trim();
        if (Directory.Exists(current))
        {
            dialog.InitialDirectory = current;
        }

        if (dialog.ShowDialog(this) == true)
        {
            FolderBox.Text = dialog.FolderName;
        }
    }

    private void OnFolderTextChanged(object sender, TextChangedEventArgs e) => UpdateHint();

    private void OnContinueClicked(object sender, RoutedEventArgs e) => Continue();

    private void OnCancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>
    /// Describes what pressing Continue will do to the folder currently typed,
    /// so the three outcomes - reopen, create, or create-and-index - are visible
    /// before the click rather than after.
    /// </summary>
    private void UpdateHint()
    {
        ErrorText.Text = string.Empty;
        string folder = FolderBox.Text.Trim();

        if (folder.Length == 0)
        {
            HintText.Text = "Choose where Photo Gallery should keep its files.";
            ContinueButton.IsEnabled = false;
            return;
        }

        ContinueButton.IsEnabled = true;

        if (IsExistingLibrary(folder))
        {
            HintText.Text = "This is already a Photo Gallery library - it will be reopened.";
            return;
        }

        if (!Directory.Exists(folder))
        {
            HintText.Text = "This folder will be created.";
            return;
        }

        HintText.Text = ContainsPhotos(folder)
            ? "Photos already in this folder and its subfolders will be added to your library."
            : "Photo Gallery will set itself up here. You can add photo folders next.";
    }

    private void Continue()
    {
        string folder = FolderBox.Text.Trim().TrimEnd('\\', '/');
        if (folder.Length == 0)
        {
            ErrorText.Text = "Enter a folder, or use Browse to pick one.";
            return;
        }

        if (!Path.IsPathFullyQualified(folder))
        {
            ErrorText.Text = "Enter a full path, for example D:\\Pictures\\PhotoGallery.";
            return;
        }

        try
        {
            // Create here, with the error visible, rather than after the window
            // closes and the user has nowhere to read the failure.
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            ErrorText.Text = $"Cannot use that folder: {ex.Message}";
            return;
        }

        FolderHasPhotos = !IsExistingLibrary(folder) && ContainsPhotos(folder);
        ChosenFolder = folder;
        DialogResult = true;
    }

    private static bool IsExistingLibrary(string folder) => WorkingFolder.IsLibrary(folder);

    /// <summary>
    /// A shallow look for image files - two levels is enough to recognise a
    /// pictures folder without walking a whole drive while the user types.
    /// </summary>
    private static bool ContainsPhotos(string folder)
    {
        try
        {
            if (HasPhotosDirectlyIn(folder))
            {
                return true;
            }

            foreach (string child in Directory.EnumerateDirectories(folder))
            {
                if (HasPhotosDirectlyIn(child))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException)
        {
            return false;
        }
    }

    private static bool HasPhotosDirectlyIn(string folder)
    {
        foreach (string pattern in s_photoPatterns)
        {
            using IEnumerator<string> matches = Directory
                .EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly)
                .GetEnumerator();
            if (matches.MoveNext())
            {
                return true;
            }
        }

        return false;
    }
}
