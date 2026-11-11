using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.ShopIntegrations;
using OISM.Application.Features.ShopIntegrations.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// Quản lý kết nối gian hàng sàn TMĐT dùng cho webhook (FR-SIM-01). Chỉ Owner được truy cập.
/// Secret webhook chỉ trả về khi tạo hoặc đổi secret.
/// </summary>
[ApiController]
[Route("api/shop-integrations")]
[Authorize(Roles = "Owner")]
public class ShopIntegrationsController : ControllerBase
{
    private readonly IShopIntegrationService _service;

    public ShopIntegrationsController(IShopIntegrationService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetIntegrations(CancellationToken ct)
    {
        return Ok(await _service.GetIntegrationsAsync(ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetIntegrationById([FromRoute] Guid id, CancellationToken ct)
    {
        var integration = await _service.GetIntegrationByIdAsync(id, ct) ?? throw NotFoundError();
        return Ok(integration);
    }

    [HttpPost]
    public async Task<IActionResult> CreateIntegration([FromBody] CreateShopIntegrationRequest request, CancellationToken ct)
    {
        var validation = await new CreateShopIntegrationValidator().ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            throw new OismException("VALIDATION_ERROR", string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)), 400);
        }

        var response = await _service.CreateIntegrationAsync(request, ct);
        return StatusCode(201, response);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateIntegration([FromRoute] Guid id, [FromBody] UpdateShopIntegrationRequest request, CancellationToken ct)
    {
        var validation = await new UpdateShopIntegrationValidator().ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            throw new OismException("VALIDATION_ERROR", string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)), 400);
        }

        var response = await _service.UpdateIntegrationAsync(id, request, ct) ?? throw NotFoundError();
        return Ok(response);
    }

    [HttpPost("{id:guid}/rotate-secret")]
    public async Task<IActionResult> RotateSecret([FromRoute] Guid id, CancellationToken ct)
    {
        var response = await _service.RotateSecretAsync(id, ct) ?? throw NotFoundError();
        return Ok(response);
    }

    private static OismException NotFoundError() =>
        new("SHOP_INTEGRATION_NOT_FOUND", "Không tìm thấy kết nối gian hàng.", 404);
}
