using OISM.Application.Features.PaymentAccounts.Dtos;

namespace OISM.Application.Features.PaymentAccounts.Services;

public interface IPaymentAccountService
{
    Task<List<PaymentAccountDto>> GetAccountsAsync(CancellationToken ct = default);
    Task<List<ActivePaymentAccountDto>> GetActiveAccountsAsync(CancellationToken ct = default);
    Task<PaymentAccountDto> CreateAccountAsync(CreatePaymentAccountRequest request, Guid userId, CancellationToken ct = default);
    Task<PaymentAccountDto> UpdateAccountAsync(Guid id, UpdatePaymentAccountRequest request, Guid userId, CancellationToken ct = default);
    Task<PaymentAccountDto> SetDefaultAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<PaymentAccountDto> DeactivateAsync(Guid id, Guid userId, CancellationToken ct = default);
}
