using Scolia.Application.Dtos;
using Scolia.Domain.School;

namespace Scolia.Tests;

public sealed class InstitutionProfilesTests
{
    [Fact]
    public void Legacy_school_without_profile_is_still_itm()
    {
        var school = new SchoolProfile();
        Assert.Equal(InstitutionProfiles.ItmIem, school.InstitutionType);
        Assert.True(InstitutionProfiles.IsEnabled(school.InstitutionType));
    }

    [Fact]
    public void Older_installation_request_remains_itm_without_new_field()
    {
        var request = new SetupRequest(
            "Institut existant", "ITM", null, null, null, "CDF", "FC",
            "2026-2027", 9, 10, "admin", "Administration", "Scolia2026!", false);
        Assert.Equal(InstitutionProfiles.ItmIem, request.InstitutionType);
    }

    [Theory]
    [InlineData("ItmIem", true)]
    [InlineData("ItmIm", true)]
    [InlineData("Primaire", false)]
    [InlineData("Secondaire", false)]
    [InlineData("SuperieurLmd", false)]
    public void Profiles_are_known_but_only_itm_is_enabled_in_phase_one(string profile, bool enabled)
    {
        Assert.True(InstitutionProfiles.IsKnown(profile));
        Assert.Equal(enabled, InstitutionProfiles.IsEnabled(profile));
    }

    [Fact]
    public void Legacy_internal_identifier_is_presented_as_itm_iem() =>
        Assert.Equal(InstitutionProfiles.ItmIem, InstitutionProfiles.Canonicalize(InstitutionProfiles.LegacyItmIm));

    [Theory]
    [InlineData("Unknown")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_profiles_are_not_known(string? profile)
    {
        Assert.False(InstitutionProfiles.IsKnown(profile));
    }
}
