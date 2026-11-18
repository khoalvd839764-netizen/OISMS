using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.Services;

namespace OISM.Application.Features.Auth.PasswordReset;

public interface IPasswordResetService
{
    /// <summary>Tự đặt lại mật khẩu qua email có bật được không (đủ SMTP, khóa ký và URL frontend).</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Nếu tìm thấy tài khoản đang hoạt động có email thì xếp mail chứa link đặt lại vào hàng đợi.
    /// Luôn hoàn tất như nhau để không lộ tài khoản có tồn tại hay không.
    /// </summary>
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);

    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
}

/// <summary>
/// Quên mật khẩu qua link email bằng token tự ký (Upgrade 10, giai đoạn 2b). Không thêm bảng:
/// token gắn dấu vân tay password_hash nên đặt lại xong thì mọi link cũ tự hết hiệu lực.
/// </summary>
public class PasswordResetService : IPasswordResetService
{
    private const string DisabledMessage = "Tính năng tự đặt lại mật khẩu chưa được bật. Vui lòng liên hệ chủ cửa hàng để được đặt lại mật khẩu.";
    private const string InvalidTokenMessage = "Link đặt lại mật khẩu không hợp lệ, đã hết hạn hoặc đã được sử dụng.";

    private readonly IAppDbContext _context;
    private readonly IPasswordResetTokenService _tokenService;
    private readonly IEmailSender _emailSender;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISqlLockService _lockService;
    private readonly TimeProvider _timeProvider;
    private readonly PasswordResetOptions _options;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(
        IAppDbContext context,
        IPasswordResetTokenService tokenService,
        IEmailSender emailSender,
        IPasswordHasher passwordHasher,
        ISqlLockService lockService,
        TimeProvider timeProvider,
        IOptions<PasswordResetOptions> options,
        ILogger<PasswordResetService> logger)
    {
        _context = context;
        _tokenService = tokenService;
        _emailSender = emailSender;
        _passwordHasher = passwordHasher;
        _lockService = lockService;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled =>
        _tokenService.IsConfigured &&
        _emailSender.IsConfigured &&
        IsAbsoluteHttpUrl(_options.AdminBaseUrl);

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default)
    {
        EnsureEnabled();

        var identifier = LoginIdentifier.Resolve(request.Identifier);
        if (!identifier.IsValid)
        {
            return;
        }

        using var privilegedScope = _context.OpenPrivilegedScope();
        var user = await AuthService.FindUserByIdentifierAsync(_context, identifier, ct);
        if (user == null || !user.IsActive || user.Tenant == null || !user.Tenant.IsActive || string.IsNullOrEmpty(user.Email))
        {
            return;
        }

        var expiresAt = _timeProvider.GetUtcNow().Add(_tokenService.Lifetime);
        var token = _tokenService.CreateToken(user.Id, user.PasswordHash, expiresAt);
        // Token nằm sau dấu # nên trình duyệt không gửi lên server: không vào log reverse proxy, không lọt qua Referer.
        var link = $"{ResolveBaseUrl(request.App)}/reset-password#token={token}";

        var email = PasswordResetEmail.Build(user.Email, user.DisplayName, link, (int)_tokenService.Lifetime.TotalMinutes);
        if (!_emailSender.TryQueue(email))
        {
            _logger.LogWarning("Không xếp được mail đặt lại mật khẩu vào hàng đợi cho người dùng {UserId}.", user.Id);
        }
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        EnsureEnabled();

        var payload = _tokenService.ReadToken(request.Token);
        if (payload == null || payload.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            throw InvalidToken();
        }

        // Băm trước khi mở transaction để không giữ khóa dòng users trong lúc chạy BCrypt.
        var newPasswordHash = _passwordHasher.HashPassword(request.NewPassword);

        await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            using var privilegedScope = _context.OpenPrivilegedScope();
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);
            try
            {
                await _lockService.LockUserAsync(payload.UserId, ct);

                var user = await _context.Users
                    .IgnoreQueryFilters()
                    .Include(u => u.Tenant)
                    .FirstOrDefaultAsync(u => u.Id == payload.UserId, ct);

                // Đọc lại sau khi khóa: request đồng thời đã đổi mật khẩu thì dấu vân tay không còn khớp.
                if (user == null || !_tokenService.MatchesPasswordHash(payload, user.PasswordHash))
                {
                    throw InvalidToken();
                }

                if (!user.IsActive)
                {
                    throw new OismException("USER_INACTIVE", "Tài khoản đã bị khóa, không thể đặt lại mật khẩu.", 403);
                }

                if (user.Tenant == null || !user.Tenant.IsActive)
                {
                    throw new OismException("TENANT_INACTIVE", "Cửa hàng đang bị tạm ngưng, không thể đặt lại mật khẩu.", 403);
                }

                var now = _timeProvider.GetUtcNow();
                user.PasswordHash = newPasswordHash;
                user.UpdatedAt = now;
                await RefreshTokenRevocation.MarkAllRevokedAsync(_context, user.Id, now, ct);

                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        });

        _logger.LogInformation("Người dùng {UserId} đã đặt lại mật khẩu qua link email.", payload.UserId);
    }

    private void EnsureEnabled()
    {
        if (!IsEnabled)
        {
            throw new OismException("PASSWORD_RESET_DISABLED", DisabledMessage, 503);
        }
    }

    private static OismException InvalidToken() => new("RESET_TOKEN_INVALID", InvalidTokenMessage, 400);

    private string ResolveBaseUrl(string? app)
    {
        var baseUrl = app == PasswordResetApps.Pos && IsAbsoluteHttpUrl(_options.PosBaseUrl)
            ? _options.PosBaseUrl!
            : _options.AdminBaseUrl!;
        return baseUrl.TrimEnd('/');
    }

    private static bool IsAbsoluteHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
