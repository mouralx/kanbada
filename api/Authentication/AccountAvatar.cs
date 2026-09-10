using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Kanbada.Api;

public record AvatarInput(string? Photo, bool UseGravatar = false);
public sealed class AccountAvatar(KanbadaDbContext db, IHttpClientFactory clients)
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public static string Validate(string? photo)
    {
        if (string.IsNullOrEmpty(photo) || photo.Length > MaxBytes * 4 / 3 + 100)
            throw new ApiError(400, "Choose a profile photo smaller than 2 MB.");
        var comma = photo.IndexOf(',');
        if (comma < 0) throw new ApiError(400, "Choose a JPG, PNG, WebP, or GIF image.");
        var mime = photo[..comma] switch
        {
            "data:image/png;base64" => "png", "data:image/jpeg;base64" => "jpeg",
            "data:image/gif;base64" => "gif", "data:image/webp;base64" => "webp", _ => ""
        };
        byte[] bytes;
        try { bytes = Convert.FromBase64String(photo[(comma + 1)..]); }
        catch (FormatException) { throw new ApiError(400, "Choose a JPG, PNG, WebP, or GIF image."); }
        var valid = mime switch
        {
            "png" => bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "jpeg" => bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 }),
            "gif" => bytes.AsSpan().StartsWith("GIF87a"u8) || bytes.AsSpan().StartsWith("GIF89a"u8),
            "webp" => bytes.Length > 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8), _ => false
        };
        if (!valid || bytes.Length < 16 || bytes.Length > MaxBytes)
            throw new ApiError(400, "Choose a JPG, PNG, WebP, or GIF image smaller than 2 MB.");
        return photo;
    }

    public async Task<string?> Gravatar(Guid id, CancellationToken cancellationToken)
    {
        var email = await db.Users.Where(x => x.Id == id).Select(x => x.Email).SingleAsync(cancellationToken);
        var hash = Auth.Hash(email.Trim().ToLowerInvariant()).ToLowerInvariant();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var response = await clients.CreateClient("gravatar").GetAsync(
                $"https://www.gravatar.com/avatar/{hash}?d=404&s=256&r=g", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes)
                throw new ApiError(503, "Gravatar is unavailable. Try again or upload a photo.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
            {
                if (buffer.Length + read > MaxBytes) throw new ApiError(503, "Gravatar is unavailable. Try again or upload a photo.");
                buffer.Write(chunk, 0, read);
            }
            return Validate($"data:{response.Content.Headers.ContentType?.MediaType};base64,{Convert.ToBase64String(buffer.ToArray())}");
        }
        catch (HttpRequestException) { throw new ApiError(503, "Gravatar is unavailable. Try again or upload a photo."); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new ApiError(503, "Gravatar is unavailable. Try again or upload a photo."); }
    }

    public async Task Save(Guid id, AvatarInput input, CancellationToken cancellationToken)
    {
        var photo = input.UseGravatar ? await Gravatar(id, cancellationToken)
            ?? throw new ApiError(400, "No Gravatar photo was found. Upload your own photo.") : Validate(input.Photo);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Users.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Photo, photo), cancellationToken);
        await db.Workspaces.Where(x => db.Members.Any(m => m.WorkspaceId == x.Id && m.UserId == id))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, x => x.Version + 1), cancellationToken);
        await db.Members.Where(x => x.UserId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Photo, photo), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}
