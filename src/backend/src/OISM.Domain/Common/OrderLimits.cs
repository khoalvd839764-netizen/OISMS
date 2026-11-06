namespace OISM.Domain.Common;

public static class OrderLimits
{
    public const decimal MaxMoneyAmount = 999_999_999_999_999_999.99m; // numeric(20,2)
    public const decimal MaxQuantityAmount = 999_999_999_999_999.999m; // numeric(18,3)
}
