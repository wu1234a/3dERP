using ThreeDErp.Application.Warehouses;

namespace ThreeDErp.UnitTests.Warehouses;

public sealed class WarehouseRequestValidatorTests
{
    private readonly WarehouseRequestValidator _validator = new();

    private static CreateWarehouseRequest CreateRequest(
        string? code = "WH-001",
        string? name = "示例仓库",
        string? address = null,
        string? contactName = null,
        string? phone = null) =>
        new(code!, name!, address, contactName, phone);

    [Fact]
    public void Validate_ValidRequest_ReturnsNoErrors()
    {
        var errors = _validator.Validate(CreateRequest(
            address: "上海市浦东新区张江路 1 号",
            contactName: "张三",
            phone: "13800138000"));

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_OptionalFieldsCanBeEmpty()
    {
        var errors = _validator.Validate(CreateRequest());

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    [InlineData("abc-01")]
    [InlineData("WH_01")]
    [InlineData("WH 01")]
    [InlineData("THIS-CODE-IS-DEFINITELY-TOO-LONG-FOR-THE-LIMIT")]
    public void Validate_InvalidCode_ReturnsCodeError(string? code)
    {
        var errors = _validator.Validate(CreateRequest(code: code));

        Assert.True(errors.ContainsKey("code"));
    }

    [Fact]
    public void Validate_ValidCodes_AcceptUppercaseDigitsAndHyphen()
    {
        var codes = new[] { "AB", "A1", "WH-001", "X-9-9", new string('A', 32) };

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
        var errors = _validator.Validate(CreateRequest(name: new string('仓', WarehouseRequestValidator.NameMaxLength + 1)));

        Assert.True(errors.ContainsKey("name"));
    }

    [Fact]
    public void Validate_AddressLongerThanLimit_ReturnsAddressError()
    {
        var errors = _validator.Validate(CreateRequest(address: new string('a', WarehouseRequestValidator.AddressMaxLength + 1)));

        Assert.True(errors.ContainsKey("address"));
    }

    [Fact]
    public void Validate_ContactNameLongerThanLimit_ReturnsContactNameError()
    {
        var errors = _validator.Validate(CreateRequest(contactName: new string('张', WarehouseRequestValidator.ContactNameMaxLength + 1)));

        Assert.True(errors.ContainsKey("contactName"));
    }

    [Fact]
    public void Validate_PhoneLongerThanLimit_ReturnsPhoneError()
    {
        var errors = _validator.Validate(CreateRequest(phone: new string('1', WarehouseRequestValidator.PhoneMaxLength + 1)));

        Assert.True(errors.ContainsKey("phone"));
    }

    [Fact]
    public void Validate_CollectsErrorsForMultipleFields()
    {
        var errors = _validator.Validate(CreateRequest(code: "abc", name: "", address: new string('a', 300)));

        Assert.Equal("address,code,name", string.Join(",", errors.Keys.OrderBy(key => key, StringComparer.Ordinal)));
    }

    [Fact]
    public void Validate_UpdateRequest_UsesSameRules()
    {
        var request = new UpdateWarehouseRequest("abc", "", new string('a', 300), null, null);

        var errors = _validator.Validate(request);

        Assert.Equal("address,code,name", string.Join(",", errors.Keys.OrderBy(key => key, StringComparer.Ordinal)));
    }
}
