using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using ShopApp.Data;

namespace ShopApp.Services;

/// <summary>
/// The passcode that keeps the app shut when he steps away from the counter.
///
/// Same derivation as the backup key - PBKDF2, SHA-256, a per-secret salt -
/// because there is no reason to invent a second scheme, and one that has been
/// reviewed once is better than two that have not.
///
/// What this is not: encryption. The database file is plain SQLite and anyone
/// with the machine can open it in a viewer without ever launching this. It
/// stops a customer leaning over the counter, not somebody who takes the
/// laptop. Windows accounts and disk encryption are the answer to that, and he
/// should be told so rather than left assuming otherwise.
/// </summary>
public class PasscodeService
{
    private readonly AppDbContext _db;

    private const int Iterations = 200_000;
    private const int HashSize = 32;
    private const int SaltSize = 16;

    public PasscodeService(AppDbContext db) => _db = db;

    public bool IsConfigured()
    {
        var s = _db.Settings.AsNoTracking().Single(x => x.Id == 1);
        return s.PasscodeHash is { Length: > 0 } && s.PasscodeSalt is { Length: > 0 };
    }

    public string? RecoveryQuestion()
    {
        var s = _db.Settings.AsNoTracking().Single(x => x.Id == 1);
        return string.IsNullOrWhiteSpace(s.RecoveryQuestion) ? null : s.RecoveryQuestion;
    }

    public int AutoLockMinutes()
    {
        var s = _db.Settings.AsNoTracking().Single(x => x.Id == 1);
        return Math.Clamp(s.AutoLockMinutes, 0, 240);
    }

    /// <summary>
    /// Sets the passcode and the recovery question together. They are one
    /// operation on purpose: a passcode with no way back is how a forgotten
    /// PIN turns into a lost set of books.
    /// </summary>
    public void Set(string passcode, string question, string answer)
    {
        var s = _db.Settings.Single(x => x.Id == 1);

        s.PasscodeSalt = RandomNumberGenerator.GetBytes(SaltSize);
        s.PasscodeHash = Hash(passcode, s.PasscodeSalt);

        s.RecoveryQuestion = question.Trim();
        s.RecoveryAnswerSalt = RandomNumberGenerator.GetBytes(SaltSize);
        s.RecoveryAnswerHash = Hash(Normalise(answer), s.RecoveryAnswerSalt);

        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        AppLog.Info("Passcode set");
    }

    public void SetAutoLockMinutes(int minutes)
    {
        var s = _db.Settings.Single(x => x.Id == 1);
        s.AutoLockMinutes = Math.Clamp(minutes, 0, 240);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    /// <summary>Removes the passcode entirely. Requires the current one.</summary>
    public bool Remove(string currentPasscode)
    {
        if (!Verify(currentPasscode)) return false;

        var s = _db.Settings.Single(x => x.Id == 1);
        s.PasscodeHash = null;
        s.PasscodeSalt = null;
        s.RecoveryQuestion = null;
        s.RecoveryAnswerHash = null;
        s.RecoveryAnswerSalt = null;
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        AppLog.Warn("Passcode removed");
        return true;
    }

    public bool Verify(string passcode)
    {
        var s = _db.Settings.AsNoTracking().Single(x => x.Id == 1);
        if (s.PasscodeHash is null || s.PasscodeSalt is null) return true;   // none set

        return Same(Hash(passcode, s.PasscodeSalt), s.PasscodeHash);
    }

    /// <summary>
    /// Checks the recovery answer. Case and surrounding spaces are ignored -
    /// he will type it months later and "Lahore" should not fail against
    /// "lahore ".
    /// </summary>
    public bool VerifyRecoveryAnswer(string answer)
    {
        var s = _db.Settings.AsNoTracking().Single(x => x.Id == 1);
        if (s.RecoveryAnswerHash is null || s.RecoveryAnswerSalt is null) return false;

        return Same(Hash(Normalise(answer), s.RecoveryAnswerSalt), s.RecoveryAnswerHash);
    }

    private static string Normalise(string answer) =>
        answer.Trim().ToLowerInvariant();

    private static byte[] Hash(string secret, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(secret), salt, Iterations,
            HashAlgorithmName.SHA256, HashSize);

    /// <summary>
    /// Constant-time comparison. Comparing hashes with SequenceEqual leaks how
    /// much of a guess was right through how long the check took.
    /// </summary>
    private static bool Same(byte[] a, byte[] b) =>
        CryptographicOperations.FixedTimeEquals(a, b);
}
