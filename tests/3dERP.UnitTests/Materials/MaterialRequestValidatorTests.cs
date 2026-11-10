using ThreeDErp.Application.Materials;

namespace ThreeDErp.UnitTests.Materials;

public sealed class MaterialRequestValidatorTests
{
    private readonly MaterialRequestValidator _validator = new();

    private static CreateMaterialRequest CreateRequest(
        string? code = "MAT-RESIN-001",
        string? name = "光敏树脂",
        string? specification = "SLA 通用树脂 1kg 装",
        string? unit = "kg",
        string? materialType = "树脂",
        decimal unitPrice = 350.0000m,
        decimal safetyStock = 10.0000m) =>
        new(code!, name!, specification, unit!, materialType!, unitPrice, safetyStock);

    [Fact]
    public void Validate_ValidRequest_ReturnsNoErrors()
    {
        var errors = _validator.Validate(CreateRequest());

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_OptionalSpecificationCanBeEmpty()
    {
        var errors = _validator.Validate(CreateRequest(specification: null));

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc-01")]
    [InlineData("MAT_01")]
    [InlineData("MAT 01")]
    public void Validate_InvalidCode_ReturnsCodeError(string? code)
    {
        var errors = _validator.Validate(CreateRequest(code: code));

        Assert.True(errors.ContainsKey("code"));
    }

    [Fact]
    public void Validate_CodeLongerThanLimit_ReturnsCodeError()
    {
        var errors = _validator.Validate(CreateRequest(code: new string('M', MaterialRequestValidator.CodeMaxLength + 1)));

        Assert.True(errors.ContainsKey("code"));
    }

    [Fact]
    public void Validate_ValidCodes_AcceptUppercaseDigitsAndHyphen()
    {
        var codes = new[] { "M", "MAT-RESIN-001", "X-9-9", new string('A', MaterialRequestValidator.CodeMaxLength) };

        foreach (var code in codes)
        {
            Assert.Empty(_validator.Validate(CreateRequest(code: code)));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingName_ReturnsNameError(string? name)
    {
        var errors = _validator.Validate(CreateRequest(name: name));

        Assert.True(errors.ContainsKey("name"));
    }

    [Fact]
    public void Validate_NameLongerThanLimit_ReturnsNameError()
    {
        var errors = _validator.Validate(CreateRequest(name: new string('树', MaterialRequestValidator.NameMaxLength + 1)));

        Assert.True(errors.ContainsKey("name"));
    }

    [Fact]
    public void Validate_SpecificationLongerThanLimit_ReturnsSpecificationError()
    {
        var errors = _validator.Validate(CreateRequest(specification: new string('规', MaterialRequestValidator.SpecificationMaxLength + 1)));

        Assert.True(errors.ContainsKey("specification"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingUnit_ReturnsUnitError(string? unit)
    {
        var errors = _validator.Validate(CreateRequest(unit: unit));

        Assert.True(errors.ContainsKey("unit"));
    }

    [Fact]
    public void Validate_UnitLongerThanLimit_ReturnsUnitError()
    {
        var errors = _validator.Validate(CreateRequest(unit: new string('千', MaterialRequestValidator.UnitMaxLength + 1)));

        Assert.True(errors.ContainsKey("unit"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingMaterialType_ReturnsMaterialTypeError(string? materialType)
    {
        var errors = _validator.Validate(CreateRequest(materialType: materialType));

        Assert.True(errors.ContainsKey("materialType"));
    }

    [Fact]
    public void Validate_MaterialTypeLongerThanLimit_ReturnsMaterialTypeError()
    {
        var errors = _validator.Validate(CreateRequest(materialType: new string('金', MaterialRequestValidator.MaterialTypeMaxLength + 1)));

        Assert.True(errors.ContainsKey("materialType"));
    }

    [Theory]
    [InlineData(-0.0001)]
    [InlineData(-1)]
    public void Validate_NegativeAmounts_ReturnAmountErrors(decimal value)
    {
        var unitPriceErrors = _validator.Validate(CreateRequest(unitPrice: value));
        Assert.True(unitPriceErrors.ContainsKey("unitPrice"));

        var safetyStockErrors = _validator.Validate(CreateRequest(safetyStock: value));
        Assert.True(safetyStockErrors.ContainsKey("safetyStock"));
    }

    [Fact]
    public void Validate_AmountAboveLimit_ReturnsAmountError()
    {
        var errors = _validator.Validate(CreateRequest(unitPrice: MaterialRequestValidator.MaxAmount + 0.0001m));

        Assert.True(errors.ContainsKey("unitPrice"));
    }

    [Fact]
    public void Validate_AmountWithTooManyDecimals_ReturnsAmountError()
    {
        var errors = _validator.Validate(CreateRequest(unitPrice: 12.34567m));

        Assert.True(errors.ContainsKey("unitPrice"));
    }

    [Fact]
    public void Validate_AmountsAtLimitsPass()
    {
        Assert.Empty(_validator.Validate(CreateRequest(
            unitPrice: MaterialRequestValidator.MaxAmount,
            safetyStock: 0)));
    }

    [Fact]
    public void Validate_CollectsErrorsForMultipleFields()
    {
        var errors = _validator.Validate(CreateRequest(code: "abc", name: "", unit: null, materialType: ""));

        Assert.Equal("code,materialType,name,unit", string.Join(",", errors.Keys.OrderBy(key => key, StringComparer.Ordinal)));
    }

    [Fact]
    public void Validate_UpdateRequest_UsesSameRules()
    {
        var request = new UpdateMaterialRequest("abc", "", null, "", "", -1m, 0m);

        var errors = _validator.Validate(request);

        Assert.Equal("code,materialType,name,unit,unitPrice", string.Join(",", errors.Keys.OrderBy(key => key, StringComparer.Ordinal)));
    }
}
