using ThreeDErp.Application.Inventory;

namespace ThreeDErp.UnitTests.Inventory;

/// <summary>
/// 预警规则请求校验器测试：标识必填、阈值非负、decimal(19,4) 上下限
/// （最大值与 4 位小数精度）以及创建 / 更新两种请求的等价校验。
/// </summary>
public sealed class InventoryAlertRuleRequestValidatorTests
{
    private readonly InventoryAlertRuleRequestValidator _validator = new();

    private static Guid NonEmptyGuid() => Guid.NewGuid();

    [Fact]
    public void Validate_ValidRequest_ReturnsNoErrors()
    {
        var createErrors = _validator.Validate(
            new CreateInventoryAlertRuleRequest(NonEmptyGuid(), NonEmptyGuid(), 10.0000m));
        Assert.Empty(createErrors);

        var updateErrors = _validator.Validate(
            new UpdateInventoryAlertRuleRequest(NonEmptyGuid(), NonEmptyGuid(), 0m, IsEnabled: false));
        Assert.Empty(updateErrors);
    }

    [Fact]
    public void Validate_ZeroThreshold_IsAllowed()
    {
        var errors = _validator.Validate(
            new CreateInventoryAlertRuleRequest(NonEmptyGuid(), NonEmptyGuid(), 0.0000m));

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_MaxDecimal194Threshold_IsAllowed()
    {
        var errors = _validator.Validate(
            new CreateInventoryAlertRuleRequest(NonEmptyGuid(), NonEmptyGuid(), InventoryAlertRuleRequestValidator.MaxQuantity));

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_NegativeThreshold_IsRejected()
    {
        var errors = _validator.Validate(
            new CreateInventoryAlertRuleRequest(NonEmptyGuid(), NonEmptyGuid(), -0.0001m));

        Assert.Contains("minimumQuantity", errors.Keys);
        Assert.Contains("不能为负数", errors["minimumQuantity"][0]);
    }

    [Fact]
    public void Validate_ThresholdAboveDecimal194UpperBound_IsRejected()
    {
        var errors = _validator.Validate(
            new CreateInventoryAlertRuleRequest(NonEmptyGuid(), NonEmptyGuid(), 1_000_000_000_000_000m));

        Assert.Contains("minimumQuantity", errors.Keys);
    }

    [Theory]
    [InlineData(10.00001)]
    [InlineData(0.12345)]
    public void Validate_ThresholdWithMoreThanFourDecimals_IsRejected(decimal minimumQuantity)
    {
        var errors = _validator.Validate(
            new CreateInventoryAlertRuleRequest(NonEmptyGuid(), NonEmptyGuid(), minimumQuantity));

        Assert.Contains("minimumQuantity", errors.Keys);
        Assert.Contains("位小数", errors["minimumQuantity"][0]);
    }

    [Fact]
    public void Validate_EmptyScopeIdentifiers_AreRejected()
    {
        var errors = _validator.Validate(
            new CreateInventoryAlertRuleRequest(Guid.Empty, Guid.Empty, 10m));

        Assert.Contains("warehouseId", errors.Keys);
        Assert.Contains("materialId", errors.Keys);
    }

    [Fact]
    public void Validate_UpdateRequest_UsesSameRules()
    {
        var errors = _validator.Validate(
            new UpdateInventoryAlertRuleRequest(Guid.Empty, NonEmptyGuid(), -1m, IsEnabled: true));

        Assert.Contains("warehouseId", errors.Keys);
        Assert.Contains("minimumQuantity", errors.Keys);
        Assert.DoesNotContain("materialId", errors.Keys);
    }
}
