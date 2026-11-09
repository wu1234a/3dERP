using ThreeDErp.Application.Customers;

namespace ThreeDErp.UnitTests.Customers;

public sealed class CustomerRequestValidatorTests
{
    private readonly CustomerRequestValidator _validator = new();

    private static CreateCustomerRequest CreateRequest(
        string? code = "CUST-001",
        string? name = "示例客户",
        string? contactName = null,
        string? phone = null,
        string? email = null,
        string? address = null,
        string? taxNo = null) =>
        new(code!, name!, contactName, phone, email, address, taxNo);

    [Fact]
    public void Validate_ValidRequest_ReturnsNoErrors()
    {
        var errors = _validator.Validate(CreateRequest(
            contactName: "张三",
            phone: "13800138000",
            email: "contact@example.com",
            address: "上海市杨浦区",
            taxNo: "91310000MA1K00000X"));

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
    [InlineData("CUST_01")]
    [InlineData("CUST 01")]
    [InlineData("THIS-CODE-IS-DEFINITELY-TOO-LONG-FOR-THE-LIMIT")]
    public void Validate_InvalidCode_ReturnsCodeError(string? code)
    {
        var errors = _validator.Validate(CreateRequest(code: code));

        Assert.True(errors.ContainsKey("code"));
    }

    [Fact]
    public void Validate_ValidCodes_AcceptUppercaseDigitsAndHyphen()
    {
        var codes = new[] { "AB", "A1", "CUST-001", "X-9-9", new string('A', 32) };

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
        var errors = _validator.Validate(CreateRequest(name: new string('名', CustomerRequestValidator.NameMaxLength + 1)));

        Assert.True(errors.ContainsKey("name"));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("a b@example.com")]
    [InlineData("@example.com")]
    public void Validate_InvalidEmail_ReturnsEmailError(string email)
    {
        var errors = _validator.Validate(CreateRequest(email: email));

        Assert.True(errors.ContainsKey("email"));
    }

    [Fact]
    public void Validate_ValidEmail_Passes()
    {
        Assert.Empty(_validator.Validate(CreateRequest(email: "user.name+tag@example.co")));
    }

    [Fact]
    public void Validate_PhoneLongerThanLimit_ReturnsPhoneError()
    {
        var errors = _validator.Validate(CreateRequest(phone: new string('1', CustomerRequestValidator.PhoneMaxLength + 1)));

        Assert.True(errors.ContainsKey("phone"));
    }

    [Fact]
    public void Validate_CollectsErrorsForMultipleFields()
    {
        var errors = _validator.Validate(CreateRequest(code: "abc", name: "", email: "bad"));

        Assert.Equal("code,email,name", string.Join(",", errors.Keys.OrderBy(key => key, StringComparer.Ordinal)));
    }

    [Fact]
    public void Validate_UpdateRequest_UsesSameRules()
    {
        var request = new UpdateCustomerRequest("abc", "", null, null, "bad", null, null);

        var errors = _validator.Validate(request);

        Assert.Equal("code,email,name", string.Join(",", errors.Keys.OrderBy(key => key, StringComparer.Ordinal)));
    }
}
