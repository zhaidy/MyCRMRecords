namespace MyCRMRecords.Tests;

/// <summary>MyCRM's timer UpdateCustomerNo as a scheduled task: Advance's customer number of each company's BRN.</summary>
public sealed class CustomerNumbersTests
{
    private static readonly IReadOnlyDictionary<string, List<string>> Advance = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
    {
        ["200012345K"] = ["C0002"], ["199912345K"] = ["C0001", "C0003"], ["ABC"] = ["C0005"],
    };

    [Theory]
    [InlineData("200012345K", true, "C0002")]
    [InlineData(" 200012345k ", true, "C0002")]
    [InlineData("199912345K", true, "C0001 #Multiple")]
    [InlineData("ABC", true, null)]
    [InlineData("NOTINADVANCE", true, null)]
    [InlineData("200012345K", false, null)]
    public void A_company_gets_the_customer_number_of_its_BRN(string brn, bool active, string? number) =>
        Assert.Equal(number, CustomerNumbers.NumberFor(new Company { Brn = brn, IsActive = active }, Advance));

    [Fact]
    public void The_task_is_off_and_runs_daily_at_02_45_once_turned_on()
    {
        var schedule = new CrmSettings().Jobs.For(Jobs.CustomerNumbers);
        Assert.False(schedule.Enabled);
        Assert.Equal("02:45", schedule.DailyAt);
        Assert.Equal("Customer numbers from Advance", Jobs.Title(Jobs.CustomerNumbers));
        Assert.Contains(Jobs.CustomerNumbers, Jobs.All);
    }
}
