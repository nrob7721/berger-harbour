using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Tests.Settings;

public class AddonDefinitionTests
{
    [Fact]
    public void Price_on_request_clears_the_prices()
    {
        var addon = AddonDefinition.Create(AddonId.New(), "Hamper", null, true, new AddonPrices(10, 20, 30), false, null, null, 0);
        Assert.Equal(AddonPrices.None, addon.Prices);
    }

    [Fact]
    public void The_capacity_unit_label_is_cleared_without_a_capacity()
    {
        var addon = AddonDefinition.Create(AddonId.New(), "Ice", null, true, AddonPrices.None, true, null, "bags", 0);
        Assert.Null(addon.CapacityUnitLabel);
        addon.Update("Car parking", null, true, AddonPrices.None, true, 11, " car spots ", true);
        Assert.Equal("car spots", addon.CapacityUnitLabel);
    }

    [Fact]
    public void Priced_addons_need_every_online_price()
    {
        var e = Assert.Throws<DomainValidationException>(() =>
            AddonDefinition.Create(AddonId.New(), "Motor", null, false, new AddonPrices(95, null, 130), false, null, null, 0));
        Assert.Equal(["weekendPrice"], e.Errors.ByField.Keys);
    }
}
