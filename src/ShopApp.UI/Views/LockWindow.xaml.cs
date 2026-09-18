using System.Windows;
using ShopApp.Services;

namespace ShopApp.UI.Views;

/// <summary>
/// Guards the app. Shown at startup and again whenever it has sat idle.
///
/// Deliberately offers no way past except the passcode or the recovery
/// answer: Close quits rather than continuing, because a lock with a skip
/// button is decoration.
/// </summary>
public partial class LockWindow : Window
{
    private enum Mode { Setup, Unlock, Recovery }

    private readonly PasscodeService _passcodes;
    private Mode _mode;

    /// <summary>True only if he got in.</summary>
    public bool Unlocked { get; private set; }

    /// <summary>
    /// Suggestions, not a fixed list - the box is editable. His own question
    /// is better than anything offered here, and a list of stock questions
    /// invites the answer somebody could look up.
    /// </summary>
    private static readonly string[] Suggestions =
    {
        "What was my first supplier called?",
        "What street was the first shop on?",
        "What is my mother's maiden name?",
        "What was my first vehicle's registration?"
    };

    /// <param name="relock">Shown because the app sat idle, not at startup.</param>
    /// <param name="forceSetup">
    /// Opened from Settings to change an existing passcode. The caller has
    /// already checked the current one, so this goes straight to setup rather
    /// than asking for it twice.
    /// </param>
    public LockWindow(PasscodeService passcodes, bool relock = false, bool forceSetup = false)
    {
        InitializeComponent();
        _passcodes = passcodes;

        QuestionBox.ItemsSource = Suggestions;

        _mode = forceSetup || !passcodes.IsConfigured() ? Mode.Setup : Mode.Unlock;

        // Changing a passcode from Settings is a choice, not a gate, so it can
        // be abandoned without the app closing.
        if (forceSetup) QuitButton.Content = "Cancel";

        Apply(relock);
    }

    private void Apply(bool relock = false)
    {
        UnlockPanel.Visibility = _mode == Mode.Unlock ? Visibility.Visible : Visibility.Collapsed;
        RecoveryPanel.Visibility = _mode == Mode.Recovery ? Visibility.Visible : Visibility.Collapsed;
        SetupPanel.Visibility = _mode is Mode.Setup or Mode.Recovery
            ? Visibility.Visible : Visibility.Collapsed;

        // During recovery he answers the question and picks a new passcode,
        // but the question itself stays as it was.
        QuestionSetupPanel.Visibility = _mode == Mode.Setup
            ? Visibility.Visible : Visibility.Collapsed;

        ForgotButton.Visibility = _mode == Mode.Unlock && _passcodes.RecoveryQuestion() is not null
            ? Visibility.Visible : Visibility.Collapsed;

        ErrorText.Text = "";

        switch (_mode)
        {
            case Mode.Setup:
                HeaderText.Text = "Set a passcode";
                SubText.Text = "Asked when the application opens, and again if it has been left "
                             + "idle. It keeps the books off the screen when you step away from "
                             + "the counter.";
                GoButton.Content = "Set passcode";
                Loaded += (_, _) => NewBox.Focus();
                break;

            case Mode.Unlock:
                HeaderText.Text = relock ? "Locked" : "Welcome back";
                SubText.Text = relock
                    ? "Left idle, so it locked itself. Nothing was lost."
                    : "Enter your passcode to continue.";
                GoButton.Content = "Unlock";
                Loaded += (_, _) => EntryBox.Focus();
                break;

            case Mode.Recovery:
                HeaderText.Text = "Recover access";
                SubText.Text = "Answer your recovery question, then choose a new passcode.";
                QuestionText.Text = _passcodes.RecoveryQuestion() ?? "";
                GoButton.Content = "Set new passcode";
                Loaded += (_, _) => AnswerBox.Focus();
                break;
        }
    }

    private void Forgot_Click(object sender, RoutedEventArgs e)
    {
        _mode = Mode.Recovery;
        Apply();
        AnswerBox.Focus();
    }

    private void Go_Click(object sender, RoutedEventArgs e)
    {
        switch (_mode)
        {
            case Mode.Unlock: TryUnlock(); break;
            case Mode.Setup: TrySetup(); break;
            case Mode.Recovery: TryRecover(); break;
        }
    }

    private void TryUnlock()
    {
        if (!_passcodes.Verify(EntryBox.Password))
        {
            ErrorText.Text = "That passcode is not right.";
            EntryBox.Clear();
            EntryBox.Focus();
            AppLog.Warn("Failed unlock attempt");
            return;
        }

        Unlocked = true;
        DialogResult = true;
    }

    private void TrySetup()
    {
        if (!ValidNewPasscode(out var passcode)) return;

        var question = QuestionBox.Text?.Trim() ?? "";
        var answer = NewAnswerBox.Text?.Trim() ?? "";

        if (question.Length < 5)
        {
            ErrorText.Text = "Choose or type a recovery question.";
            return;
        }

        // A passcode with no way back is how a forgotten PIN becomes a lost
        // set of books, so the answer is required rather than optional.
        if (answer.Length < 2)
        {
            ErrorText.Text = "Answer your recovery question. Without it, a forgotten "
                           + "passcode cannot be undone.";
            return;
        }

        _passcodes.Set(passcode, question, answer);
        Unlocked = true;
        DialogResult = true;
    }

    private void TryRecover()
    {
        if (!_passcodes.VerifyRecoveryAnswer(AnswerBox.Text ?? ""))
        {
            ErrorText.Text = "That answer does not match.";
            AppLog.Warn("Failed recovery attempt");
            return;
        }

        if (!ValidNewPasscode(out var passcode)) return;

        _passcodes.Set(passcode, _passcodes.RecoveryQuestion() ?? "", AnswerBox.Text ?? "");
        AppLog.Warn("Passcode reset through recovery");

        Unlocked = true;
        DialogResult = true;
    }

    private bool ValidNewPasscode(out string passcode)
    {
        passcode = NewBox.Password;

        if (passcode.Length < 4)
        {
            ErrorText.Text = "Use at least four characters. Digits are fine - it is typed "
                           + "several times a day.";
            NewBox.Focus();
            return false;
        }

        if (passcode != ConfirmBox.Password)
        {
            ErrorText.Text = "The two entries do not match.";
            ConfirmBox.Clear();
            ConfirmBox.Focus();
            return false;
        }

        return true;
    }

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        Unlocked = false;
        DialogResult = false;
    }
}
