using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using System.Security.Cryptography;

namespace Kanbada.Api;

public record TwoFactorInput(string Password, string? Code = null);
public record TwoFactorStatus(bool Available, bool Enabled, int RecoveryCodesRemaining, bool Required, bool PasswordRequired);
public record TwoFactorSetup(string Secret, string Uri);
public record RecoveryCodes(string[] Codes);

public sealed class TwoFactor(KanbadaDbContext db, IDataProtectionProvider protection, TimeProvider clock)
{
    private IDataProtector Protector(Guid user) => protection.CreateProtector("Kanbada.TwoFactor.v1", user.ToString());
    private sealed class InvalidFactor : Exception;

    public async Task<TwoFactorStatus> Status(Guid id)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == id);
        return new(true, user.TwoFactorSecret != null,
            await db.RecoveryCodes.CountAsync(x => x.UserId == id), user.TwoFactorRequired, user.PasswordHash != null);
    }

    // The no-op UPDATE acquires a PostgreSQL row lock before reading authentication state.
    // Every mutation and code consumption uses this lock, including across API instances.
    private async Task<T> WithUser<T>(Guid id, Func<UserEntity, Task<T>> action)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        if (await db.Users.Where(x => x.Id == id).ExecuteUpdateAsync(s =>
            s.SetProperty(x => x.TwoFactorFailedAttempts, x => x.TwoFactorFailedAttempts)) == 0)
            throw new ApiError(401, "Please sign in.");
        var user = await db.Users.SingleAsync(x => x.Id == id);
        if (user.TwoFactorLockedUntil > clock.GetUtcNow())
            throw new ApiError(429, "Too many verification attempts. Try again in five minutes.");
        if (user.TwoFactorLockedUntil != null)
        {
            user.TwoFactorLockedUntil = null;
            user.TwoFactorFailedAttempts = 0;
        }
        try
        {
            var result = await action(user);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return result;
        }
        catch (InvalidFactor)
        {
            user.TwoFactorFailedAttempts++;
            if (user.TwoFactorFailedAttempts >= 5)
                user.TwoFactorLockedUntil = clock.GetUtcNow().AddMinutes(5);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            throw new ApiError(400, "Verification failed. Check your password and code, or wait for a new authenticator code.");
        }
    }

    private static void Password(UserEntity user, string? password)
    {
        if (user.PasswordHash == null) return; // The session already authenticated the external identity.
        if (string.IsNullOrEmpty(password) || password.Length > 200 ||
            new PasswordHasher<string>().VerifyHashedPassword(user.Id.ToString(), user.PasswordHash, password) == PasswordVerificationResult.Failed)
            throw new InvalidFactor();
    }

    private long? Match(UserEntity user, string encryptedSecret, string? code)
    {
        if ((code?.Length ?? 0) > 100) return null;
        var normalized = (code ?? "").Replace(" ", "");
        if (normalized.Length != 6 || normalized.Any(c => c < '0' || c > '9')) return null;
        var secret = Protector(user.Id).Unprotect(encryptedSecret);
        var totp = new Totp(Base32Encoding.ToBytes(secret));
        return totp.VerifyTotp(clock.GetUtcNow().UtcDateTime, normalized, out var step,
            new VerificationWindow(previous: 1, future: 1)) && step > (user.TwoFactorLastStep ?? -1) ? step : null;
    }

    private async Task Verify(UserEntity user, string? code)
    {
        if (user.TwoFactorSecret == null) throw new ApiError(400, "Two-factor authentication is not enabled.");
        if (Match(user, user.TwoFactorSecret, code) is { } step)
            user.TwoFactorLastStep = step;
        else
        {
            var normalized = (code ?? "").Replace("-", "").Replace(" ", "").ToUpperInvariant();
            if (normalized.Length != 32 || !normalized.All(Uri.IsHexDigit)) throw new InvalidFactor();
            var hash = Auth.Hash(user.Id + ":" + normalized);
            var recovery = await db.RecoveryCodes.SingleOrDefaultAsync(x => x.UserId == user.Id && x.Hash == hash);
            if (recovery == null) throw new InvalidFactor();
            db.RecoveryCodes.Remove(recovery);
        }
        user.TwoFactorFailedAttempts = 0;
        user.TwoFactorLockedUntil = null;
    }

    public Task<bool> Login(Guid id, string? code, Func<Task> issueSession) => WithUser(id, async user =>
    {
        if (user.TwoFactorSecret != null)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            if (code.Length > 100) throw new InvalidFactor();
            await Verify(user, code);
        }
        // Create the session under the same lock so enrollment cannot race a password-only login.
        await issueSession();
        return true;
    });

    public Task<TwoFactorSetup> Setup(Guid id, string password) => WithUser(id, user =>
    {
        Password(user, password);
        if (user.TwoFactorSecret != null) throw new ApiError(409, "Two-factor authentication is already enabled.");
        var secret = Base32Encoding.ToString(RandomNumberGenerator.GetBytes(20));
        user.TwoFactorPendingSecret = Protector(id).Protect(secret);
        user.TwoFactorPendingExpiresAt = clock.GetUtcNow().AddMinutes(10);
        var uri = $"otpauth://totp/{Uri.EscapeDataString("Kanbada:" + user.Email)}?secret={secret}&issuer=Kanbada&algorithm=SHA1&digits=6&period=30";
        return Task.FromResult(new TwoFactorSetup(secret, uri));
    });

    public Task<RecoveryCodes> Confirm(Guid id, TwoFactorInput input, string? session) => WithUser(id, async user =>
    {
        Password(user, input.Password);
        if (user.TwoFactorSecret != null) throw new ApiError(409, "Two-factor authentication is already enabled.");
        if (user.TwoFactorPendingSecret == null || user.TwoFactorPendingExpiresAt <= clock.GetUtcNow())
            throw new ApiError(400, "Authenticator setup expired. Start setup again.");
        var step = Match(user, user.TwoFactorPendingSecret, input.Code) ?? throw new InvalidFactor();
        user.TwoFactorSecret = user.TwoFactorPendingSecret;
        user.TwoFactorPendingSecret = null;
        user.TwoFactorPendingExpiresAt = null;
        user.TwoFactorLastStep = step;
        user.TwoFactorFailedAttempts = 0;
        await RevokeOtherSessions(id, session);
        await MarkSessionVerified(id, session);
        return await ReplaceRecoveryCodes(id);
    });

    public Task<bool> Disable(Guid id, TwoFactorInput input, string? session) => WithUser(id, async user =>
    {
        if (user.TwoFactorRequired) throw new ApiError(403, "Two-factor authentication is required for this account and cannot be disabled.");
        Password(user, input.Password);
        await Verify(user, input.Code);
        user.TwoFactorSecret = null;
        user.TwoFactorPendingSecret = null;
        user.TwoFactorPendingExpiresAt = null;
        user.TwoFactorLastStep = null;
        // Persist a consumed recovery code before deleting the remaining set.
        await db.SaveChangesAsync();
        await db.RecoveryCodes.Where(x => x.UserId == id).ExecuteDeleteAsync();
        await RevokeOtherSessions(id, session);
        return true;
    });

    public Task<RecoveryCodes> Regenerate(Guid id, TwoFactorInput input, string? session) => WithUser(id, async user =>
    {
        Password(user, input.Password);
        await Verify(user, input.Code);
        await db.SaveChangesAsync();
        await RevokeOtherSessions(id, session);
        return await ReplaceRecoveryCodes(id);
    });

    public Task<bool> VerifySession(Guid id, string? code, string? session) => WithUser(id, async user =>
    {
        await Verify(user, code);
        await MarkSessionVerified(id, session);
        return true;
    });

    private async Task MarkSessionVerified(Guid id, string? session)
    {
        if (await db.Sessions.Where(x => x.UserId == id && x.Id == session && x.ExpiresAt > DateTimeOffset.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TwoFactorVerified, true)) != 1)
            throw new ApiError(401, "Please sign in.");
    }

    private Task<int> RevokeOtherSessions(Guid id, string? session) =>
        db.Sessions.Where(x => x.UserId == id && x.Id != session).ExecuteDeleteAsync();

    private async Task<RecoveryCodes> ReplaceRecoveryCodes(Guid id)
    {
        await db.RecoveryCodes.Where(x => x.UserId == id).ExecuteDeleteAsync();
        var codes = Enumerable.Range(0, 10).Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(16))).ToArray();
        db.RecoveryCodes.AddRange(codes.Select(code => new RecoveryCodeEntity { UserId = id, Hash = Auth.Hash(id + ":" + code) }));
        return new(codes.Select(code => string.Join("-", Enumerable.Range(0, 4).Select(i => code.Substring(i * 8, 8)))).ToArray());
    }
}
