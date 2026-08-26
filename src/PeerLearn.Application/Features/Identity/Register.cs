using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Options;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Features.Identity;

public sealed record RegisterCommand(string Email, string Password, string DisplayName)
    : IRequest<RegisterResult>;

/// <param name="VerificationToken">
/// Yalnızca Jwt:ExposeVerificationTokenInResponse=true iken dolu (geliştirme kolaylığı);
/// aksi halde boş string — token yalnızca e-postayla gider (e-posta sahipliği kanıtı).
/// </param>
public sealed record RegisterResult(Guid UserId, string VerificationToken);

public sealed class RegisterHandler : IRequestHandler<RegisterCommand, RegisterResult>
{
    public const string EmailVerifyPurpose = "email-verify";

    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly IEmailSender _email;
    private readonly JwtOptions _jwtOptions;
    private readonly EmailOptions _emailOptions;

    public RegisterHandler(IAppDbContext db, IPasswordHasher hasher, ITokenService tokens,
        IEmailSender email, IOptions<JwtOptions> jwtOptions, IOptions<EmailOptions> emailOptions)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _email = email;
        _jwtOptions = jwtOptions.Value;
        _emailOptions = emailOptions.Value;
    }

    public async Task<RegisterResult> Handle(RegisterCommand request, CancellationToken ct)
    {
        var email = request.Email.Trim();
        if (email.Length is < 5 or > 320 || !email.Contains('@'))
        {
            throw new AppException(ErrorCodes.InvalidCredentials, "Geçerli bir e-posta girin.");
        }

        if (request.Password.Length < 8)
        {
            throw new AppException(ErrorCodes.InvalidCredentials, "Şifre en az 8 karakter olmalı.");
        }

        // citext kolonu sayesinde karşılaştırma büyük/küçük harf duyarsızdır.
        var exists = await _db.Users.AnyAsync(u => u.Email == email, ct);
        if (exists)
        {
            throw new AppException(ErrorCodes.EmailTaken, "Bu e-posta zaten kayıtlı.", statusCode: 409);
        }

        var user = new User
        {
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = _hasher.Hash(request.Password)
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct); // Unique index eşzamanlı kayıtta son savunmadır.

        var token = _tokens.CreatePurposeToken(user.Id, EmailVerifyPurpose,
            TimeSpan.FromHours(_jwtOptions.EmailVerifyTokenHours));

        await _email.SendAsync(email, DogrulamaEpostasi.Konu(yenidenGonderim: false),
            DogrulamaEpostasi.Govde(token, _emailOptions.PublicWebUrl), ct);

        return new RegisterResult(
            user.Id,
            _jwtOptions.ExposeVerificationTokenInResponse ? token : string.Empty);
    }
}
