using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.Support.TestDates;

namespace BergerHarbour.Domain.Tests.Shared;

public class BookingReferenceTests
{
    [Fact]
    public void Format_is_last_name_start_date_and_suffix()
    {
        Assert.Equal("SMITH-2701150", BookingReference.Create(BookingReference.PrefixFor("Smith", D("2027-01-15")), 0).Value);
        Assert.Equal("SMITH-2701151", BookingReference.Create(BookingReference.PrefixFor("Smith", D("2027-01-15")), 1).Value);
    }

    [Theory]
    [InlineData("Müller", "MULLER")]
    [InlineData("O'Brien", "OBRIEN")]
    [InlineData("Smith-Jones", "SMITHJONES")]
    [InlineData("van der Berg", "VANDERBERG")]
    [InlineData("Wolfeschlegelsteinhausen", "WOLFESCHLEGELSTEINHA")]
    [InlineData("山田", "GUEST")]
    [InlineData("", "GUEST")]
    public void Name_part_is_cleaned(string lastName, string expected) =>
        Assert.Equal(expected, BookingReference.NamePart(lastName));

    [Fact]
    public void Name_part_is_cut_to_20_characters() => Assert.Equal(20, BookingReference.NamePart("Wolfeschlegelsteinhausenbergerdorff").Length);

    [Fact]
    public void Suffix_continues_past_9() =>
        Assert.Equal(["SMITH-2701159", "SMITH-27011510", "SMITH-27011511"],
            new[] { 9, 10, 11 }.Select(s => BookingReference.Create("SMITH-270115", s).Value));

    [Fact]
    public void Search_matches_case_insensitively() => Assert.True(BookingReference.Parse("SMITH-2701150").Matches(" smith-2701150 "));
}
