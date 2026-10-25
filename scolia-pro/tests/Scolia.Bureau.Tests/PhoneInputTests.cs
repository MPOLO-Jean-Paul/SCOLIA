using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Tests;

public sealed class PhoneInputTests
{
    [Theory]
    [InlineData("0812345678", "0812345678")]
    [InlineData("081 234-56.78", "0812345678")]
    [InlineData("08123456789999", "0812345678")]
    [InlineData("abc08x12", "0812")]
    [InlineData("+243 812 345 678", "0812345678")]
    [InlineData("", "")]
    public void Only_ten_digits_are_kept(string input, string expected) => Assert.Equal(expected, PhoneInput.Clean(input));

    [Fact]
    public void A_phone_field_refuses_letters_and_checks_the_length()
    {
        var form = new FormViewModel("Test");
        var field = form.Add("tel", "Téléphone", FieldKind.Phone, required: true);
        field.Text = "08a1b2";
        Assert.Equal("0812", field.Text);
        Assert.False(form.Validate());
        Assert.Contains("10 chiffres", field.Error);
        field.Text = "0812345678";
        Assert.True(form.Validate());
    }
}
