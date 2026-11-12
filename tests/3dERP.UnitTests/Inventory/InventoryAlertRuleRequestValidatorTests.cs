using ThreeDErp.Application.Inventory;

namespace ThreeDErp.UnitTests.Inventory;

/// <summary>
/// InventoryAlertRuleRequestValidator 单元测试：仓库 / 物料必填（非空 Guid）、
/// MinimumQuantity 非负、上限与 decimal(19,4) 精度，创建 / 更新共用同一套规则。
/// </summary>
public sealed class InventoryAlertRuleRequestValidatorTests
{
    private readonly InventoryAlertRuleRequestValidator _validator = new();

    private static CreateInventoryAlertRuleRequest CreateRequest(
        Guid? warehouseId = null,
        Guid? materialId = null,
        decimal minimumQuantity = 10.0000m) =>
        new(warehouseId ?? Guid.NewGuid(), materialId ?? Guid.NewGuid(), minimumQuantity);

    private static UpdateInventoryAlertRuleRequest UpdateRequest(
        Guid? warehouseId = null,
        Guid? materialId = null,
        decimal minimumQuantity = 10.0000m) =>
        new(warehouseId ?? Guid.NewGuid(), materialId ?? Guid.NewGuid(), minimumQuantity, IsEnabled: true);

    [Fact]
    public void ValidCreateRequest_ReturnsNoErrors()
    {
        var errors = _validator.Validate(CreateRequest(minimumQuantity: 25.5m));

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidUpdateRequest_ReturnsNoErrors()
    {
        var errors = _validator.Validate(UpdateRequest(minimumQuantity: 0m));

        Assert.Empty(errors);
    }

    [Fact]
    public void EmptyGuids_AreReported()
    {
        var errors = _validator.Validate(CreateRequest(Guid.Empty, Guid.Empty));

        Assert.Contains("warehouseId", errors.Keys);
        Assert.Contains("materialId", errors.Keys);
    }

    [Theory]
    [InlineData(-0.0001)]
    [InlineData(-1)]
    public void NegativeMinimumQuantity_IsReported(decimal minimumQuantity)
    {
        var errors = _validator.Validate(CreateRequest(minimumQuantity: minimumQuantity));

        Assert.Contains("minimumQuantity", errors.Keys);
        Assert.Contains("不能为负数", errors["minimumQuantity"][0]);
    }

    [Fact]
    public void MinimumQuantityAboveDecimal194Max_IsReported()
    {
        var errors = _validator.Validate(
            CreateRequest(minimumQuantity: InventoryAlertRuleRequestValidator.MaxQuantity + 0.0001m));

        Assert.Contains("minimumQuantity", errors.Keys);
    }

    [Theory]
    [InlineData(10.00001)]
    [InlineData(0.12345)]
    public void MinimumQuantityWithMoreThanFourDecimals_IsReported(decimal minimumQuantity)
    {
        var errors = _validator.Validate(CreateRequest(minimumQuantity: minimumQuantity));

        Assert.Contains("minimumQuantity", errors.Keys);
        Assert.Contains("位小数", errors["minimumQuantity"][0]);
    }

    [Fact]
    public void UpdateRequestSharesTheSameRules()
    {
        var errors = _validator.Validate(UpdateRequest(Guid.Empty, Guid.Empty, -1m));

        Assert.Contains("warehouseId", errors.Keys);
        Assert.Contains("materialId", errors.Keys);
        Assert.Contains("minimumQuantity", errors.Keys);
    }

    [Fact]
    public void MultipleErrors_AreCollectedPerField()
    {
        var errors = _validator.Validate(CreateRequest(Guid.Empty, Guid.Empty, -5m));

        Assert.Equal(3, errors.Count);
        Assert.Single(errors["warehouseId"]);
        Assert.Single(errors["materialId"]);
        Assert.Single(errors["minimumQuantity"]);
    }
}
