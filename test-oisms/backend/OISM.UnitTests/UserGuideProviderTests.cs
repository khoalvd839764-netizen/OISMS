using OISM.Infrastructure.Ai;

namespace OISM.UnitTests;

public sealed class UserGuideProviderTests
{
    [Fact]
    public void GetGuide_LoadsEmbeddedPagesAndFiltersOwnerOnlyContent()
    {
        var provider = new UserGuideProvider();
        var owner = provider.GetGuide("Owner");
        var staff = provider.GetGuide("Staff");

        Assert.Equal(10, owner.Split("## ").Length - 1);
        Assert.Equal(8, staff.Split("## ").Length - 1);
        Assert.Contains("Tài khoản nhận tiền", owner);
        Assert.DoesNotContain("Tài khoản nhận tiền", staff);
        Assert.DoesNotContain("## Báo cáo", staff);
        Assert.DoesNotContain("roles:", owner);
        Assert.DoesNotContain("Quên mật khẩu", owner);
        Assert.Empty(provider.GetGuide("Cashier"));
    }
}
