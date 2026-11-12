using ThreeDErp.Application.Inventory;

namespace ThreeDErp.UnitTests.Inventory;

/// <summary>
/// InventoryRequestValidator 单元测试：必填 / 数量规则 / 长度上限 / 空 Guid 拦截。
/// </summary>
public sealed class InventoryRequestValidatorTests
{
    private readonly InventoryRequestValidator _validator = new();

    private static InventoryTransactionRequest CreateValidRequest() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        12.5000m,
        "PO-2026-0001",
        Guid.NewGuid(),
        null,
        "采购收货");

    [Fact]
    public void Validate_ValidRequest_ReturnsNoErrors()
    {
        var errors = _validator.Validate(CreateValidRequest());

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_MissingWarehouseAndMaterial_ReportsBothFields()
    {
        var request = CreateValidRequest() with { WarehouseId = Guid.Empty, MaterialId = Guid.Empty };

        var errors = _validator.Validate(request);

        Assert.Contains("warehouseId", errors.Keys);
        Assert.Contains("materialId", errors.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.0001)]
    public void Validate_NonPositiveQuantity_IsRejected(decimal quantity)
    {
        var request = CreateValidRequest() with { Quantity = quantity };

        var errors = _validator.Validate(request);

        Assert.True(errors.TryGetValue("quantity", out var messages));
        Assert.Contains(messages, message => message.Contains("大于 0"));
    }

    [Fact]
    public void Validate_QuantityAboveDecimal194Limit_IsRejected()
    {
        var request = CreateValidRequest() with { Quantity = InventoryRequestValidator.MaxQuantity + 1 };

        var errors = _validator.Validate(request);

        Assert.True(errors.TryGetValue("quantity", out var messages));
        Assert.Contains(messages, message => message.Contains("不能超过"));
    }

    [Fact]
    public void Validate_QuantityWithMoreThanFourDecimals_IsRejected()
    {
        var request = CreateValidRequest() with { Quantity = 1.00001m };

        var errors = _validator.Validate(request);

        Assert.True(errors.TryGetValue("quantity", out var messages));
        Assert.Contains(messages, message => message.Contains("4 位小数"));
    }

    [Fact]
    public void Validate_MaxStorableQuantity_IsAccepted()
    {
        var request = CreateValidRequest() with { Quantity = 999_999_999_999_999.9999m };

        var errors = _validator.Validate(request);

        Assert.DoesNotContain("quantity", errors.Keys);
    }

    [Fact]
    public void Validate_DocumentNoIsTrimmedAndRequired()
    {
        var blank = _validator.Validate(CreateValidRequest() with { DocumentNo = "   " });
        Assert.Contains("documentNo", blank.Keys);

        var whitespaceOnly = _validator.Validate(CreateValidRequest() with { DocumentNo = "\t" });
        Assert.Contains("documentNo", whitespaceOnly.Keys);
    }

    [Fact]
    public void Validate_DocumentNoOverMaxLength_IsRejected()
    {
        var request = CreateValidRequest() with { DocumentNo = new string('A', InventoryRequestValidator.DocumentNoMaxLength + 1) };

        var errors = _validator.Validate(request);

        Assert.True(errors.TryGetValue("documentNo", out var messages));
        Assert.Contains(messages, message => message.Contains("64"));
    }

    [Fact]
    public void Validate_EmptyOptionalPartyIds_AreRejected()
    {
        var request = CreateValidRequest() with { SupplierId = Guid.Empty, CustomerId = Guid.Empty };

        var errors = _validator.Validate(request);

        Assert.Contains("supplierId", errors.Keys);
        Assert.Contains("customerId", errors.Keys);
    }

    [Fact]
    public void Validate_OmittedOptionalPartyIds_AreAccepted()
    {
        var request = CreateValidRequest() with { SupplierId = null, CustomerId = null };

        var errors = _validator.Validate(request);

        Assert.DoesNotContain("supplierId", errors.Keys);
        Assert.DoesNotContain("customerId", errors.Keys);
    }

    [Fact]
    public void Validate_NoteOverMaxLength_IsRejected()
    {
        var request = CreateValidRequest() with { Note = new string('注', InventoryRequestValidator.NoteMaxLength + 1) };

        var errors = _validator.Validate(request);

        Assert.Contains("note", errors.Keys);
    }

    [Fact]
    public void Validate_MultipleErrors_AreCollectedPerField()
    {
        var request = new InventoryTransactionRequest(
            Guid.Empty,
            Guid.Empty,
            0m,
            "",
            Guid.Empty,
            Guid.Empty,
            new string('a', InventoryRequestValidator.NoteMaxLength + 1));

        var errors = _validator.Validate(request);

        Assert.Equal(7, errors.Count);
        Assert.Contains("warehouseId", errors.Keys);
        Assert.Contains("materialId", errors.Keys);
        Assert.Contains("quantity", errors.Keys);
        Assert.Contains("documentNo", errors.Keys);
        Assert.Contains("supplierId", errors.Keys);
        Assert.Contains("customerId", errors.Keys);
        Assert.Contains("note", errors.Keys);
    }
}
