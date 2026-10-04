using OISM.Domain.Common;

namespace OISM.Domain.Entities;

public class Role : BaseEntity
{
    public string Code { get; set; } = string.Empty;        // "Owner", "Staff", "Cashier"
    public string Description { get; set; } = string.Empty;
}