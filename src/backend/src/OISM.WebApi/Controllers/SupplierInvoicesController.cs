using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.SupplierInvoices;
using OISM.WebApi.Http;

namespace OISM.WebApi.Controllers;

[ApiController]
[Route("api/supplier-invoices")]
[Authorize(Roles = "Owner,Staff")]
public sealed class SupplierInvoicesController(SupplierInvoiceService service) : ControllerBase
{
    [HttpGet]
    [ApiErrorContract(ApiErrorProfile.SupplierInvoices)]
    public async Task<IActionResult> List([FromQuery] SupplierInvoiceFilterDto filter, CancellationToken ct) =>
        Ok(await service.ListAsync(filter, ct));

    [HttpGet("{id:guid}")]
    [ApiErrorContract(ApiErrorProfile.SupplierInvoices)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Ok(await service.GetAsync(id, ct));

    [HttpPost]
    [ApiErrorContract(ApiErrorProfile.SupplierInvoices)]
    public async Task<IActionResult> Create(
        CreateSupplierInvoiceDto dto,
        [FromHeader(Name = "Idempotency-Key")] string? key,
        CancellationToken ct)
    {
        if (!Guid.TryParse(key, out var requestKey) || requestKey == Guid.Empty)
            throw new OismException("VALIDATION_ERROR", "Bắt buộc cung cấp Idempotency-Key dạng GUID.", 400);

        var invoice = await service.CreateAsync(dto, requestKey, RequireUserId(), ct);
        return invoice.IsReplay ? Ok(invoice) : CreatedAtAction(nameof(Get), new { id = invoice.Id }, invoice);
    }

    [HttpPut("{id:guid}")]
    [ApiErrorContract(ApiErrorProfile.SupplierInvoices)]
    public async Task<IActionResult> Update(Guid id, SupplierInvoiceDraftDto dto, CancellationToken ct) =>
        Ok(await service.UpdateAsync(id, dto, ct));

    [HttpPost("{id:guid}/submit")]
    [ApiErrorContract(ApiErrorProfile.SupplierInvoices)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct) =>
        Ok(await service.SubmitAsync(id, ct));

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Owner")]
    [ApiErrorContract(ApiErrorProfile.SupplierInvoices)]
    public async Task<IActionResult> Approve(
        Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SupplierInvoiceDecisionDto? dto, CancellationToken ct) =>
        Ok(await service.DecideAsync(id, approve: true, dto?.DecisionNote, RequireUserId(), ct));

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "Owner")]
    [ApiErrorContract(ApiErrorProfile.SupplierInvoices)]
    public async Task<IActionResult> Reject(
        Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SupplierInvoiceDecisionDto? dto, CancellationToken ct) =>
        Ok(await service.DecideAsync(id, approve: false, dto?.DecisionNote, RequireUserId(), ct));

    [HttpPost("{id:guid}/cancel")]
    [ApiErrorContract(ApiErrorProfile.SupplierInvoices)]
    public async Task<IActionResult> Cancel(
        Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelSupplierInvoiceDto? dto, CancellationToken ct) =>
        Ok(await service.CancelAsync(id, dto?.Reason, RequireUserId(), ct));

    private Guid RequireUserId() =>
        User.GetUserId() ?? throw new OismException("UNAUTHORIZED", "Không xác định được người dùng từ token.", 401);
}
