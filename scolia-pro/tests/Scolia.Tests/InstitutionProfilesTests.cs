using Scolia.Application.Dtos;
using Scolia.Domain.School;

namespace Scolia.Tests;

public sealed class InstitutionProfilesTests
{
    [Fact]
    public void Legacy_school_without_profile_is_still_itm()
    {
        var school = new SchoolProfile();
        Assert.Equal(InstitutionProfiles.ItmIm, school.InstitutionType);
        Assert.True(InstitutionProfiles.IsEnabled(school.InstitutionType));
    }

    [Fact]
    public void Older_installation_request_remains_itm_without_new_field()
    {
        var request = new SetupRequest(
            "Institut existant", "ITM", null, null, null, "CDF", "FC",
            "2026-2027", 9, 10, "admin", "Administration", "Scolia2026!", false);
        Assert.Equal(InstitutionProfiles.ItmIm, request.InstitutionType);
    }

    [Theory]
    [InlineData("ItmIm", true)]
    [InlineData("Maternelle", true)]
    [InlineData("Primaire", true)]
    [InlineData("Secondaire", true)]
    [InlineData("ComplexeScolaire", true)]
    [InlineData("SuperieurLmd", false)]
    public void Private_school_cycles_are_enabled_while_lmd_remains_closed(string profile, bool enabled)
    {
        Assert.True(InstitutionProfiles.IsKnown(profile));
        Assert.Equal(enabled, InstitutionProfiles.IsEnabled(profile));
    }

    [Fact]
    public void Itm_Iem_alias_is_accepted_but_saved_under_historical_ITM_code()
    {
        Assert.True(InstitutionProfiles.IsKnown("ItmIem"));
        Assert.True(InstitutionProfiles.IsEnabled("ItmIem"));
        Assert.Equal("ItmIm", InstitutionProfiles.Canonicalize("ItmIem"));
        Assert.Equal("ItmIm", InstitutionProfiles.Canonicalize(null));
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_profiles_are_not_known(string? profile)
    {
        Assert.False(InstitutionProfiles.IsKnown(profile));
    }
}
