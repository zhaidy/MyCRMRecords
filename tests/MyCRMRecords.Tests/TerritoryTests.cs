namespace MyCRMRecords.Tests;

/// <summary>
/// Named territories, kept by Sales Admins and MD: a name, a number, a salesperson and companies; one rule moves a
/// company; every change of salesperson is a territory movement; nothing is deleted.
/// </summary>
public sealed class TerritoryTests
{
    private static Territories.TerritoryInput Input(string name, long number, ApiUser? owner = null) => new() { Name = name, Number = number, Owner = owner?.Login };

    [Fact]
    public void A_territory_needs_a_name_and_a_number_no_other_active_territory_has()
    {
        using var world = new TestWorld();
        var sara = world.Actor(TestWorld.SalesAdmin);
        var north = world.D.Territories.Save(sara, 0, Input("North", 1, TestWorld.Sam));
        Assert.True(north.Ok, north.Message);
        var t = world.D.Territories.Get(north.Id!.Value)!;
        Assert.Equal(TestWorld.Sam.Login, t.Owner);
        Assert.Equal(TestWorld.SalesAdmin.Login, t.CreatedBy);
        Assert.True(t.IsOwner);

        var bad = world.D.Territories.Save(sara, 0, Input(" north ", 1));
        Assert.Contains(bad.Errors, e => e.Field == "name");
        Assert.Contains(bad.Errors, e => e.Field == "number" && e.Message.Contains("North"));
        Assert.Contains(world.D.Territories.Save(sara, 0, Input("", 0)).Errors, e => e.Field == "number");
        // The salesperson must have the CRM role Sales.
        Assert.Contains(world.D.Territories.Save(sara, 0, Input("East", 2, TestWorld.Mia)).Errors, e => e.Field == "owner");
        // Vacant: no salesperson.
        Assert.True(world.D.Territories.Save(sara, 0, Input("East", 2)).Ok);
        Assert.Equal("Territory 7", Territories.Title(new Territory { Number = 7 }));
    }

    [Fact]
    public void A_territory_MyCRM_made_by_hand_keeps_its_number_0_until_changed()
    {
        using var world = new TestWorld();
        var sara = world.Actor(TestWorld.SalesAdmin);
        var legacy = world.Store.Insert(new Territory { Number = 0, Owner = TestWorld.Sam.Login, IsActive = true });
        world.Store.Insert(new Territory { Number = 0, Owner = TestWorld.Ken.Login, IsActive = true });
        Assert.Equal("Territory #" + legacy.Id, Territories.Title(legacy));
        // Named, number left at 0 (shared with another): saved.
        Assert.True(world.D.Territories.Save(sara, legacy.Id, Input("Sam's accounts", 0, TestWorld.Sam)).Ok);
        Assert.Equal("Sam's accounts", world.D.Territories.Get(legacy.Id)!.Name);
        // A number changed must be a new one.
        Assert.Contains(world.D.Territories.Save(sara, legacy.Id, Input("Sam's accounts", -3, TestWorld.Sam)).Errors, e => e.Field == "number");
    }

    [Fact]
    public void Putting_a_company_in_a_territory_takes_it_out_of_every_other_and_records_the_move()
    {
        using var world = new TestWorld();
        var sara = world.Actor(TestWorld.SalesAdmin);
        var tan = world.Company("Tan", "T1000");
        var lim = world.Company("Lim", "L1000");
        var north = world.Territory("North", 1, TestWorld.Sam, tan.Id, lim.Id);
        var south = world.Territory("South", 2, TestWorld.Ken);
        // Tan was also in a third territory (MyCRM let a company have several salespeople).
        var third = world.Store.Insert(new Territory { Name = "Old", Number = 3, Owner = TestWorld.Mia.Login, IsActive = true });
        world.Store.Insert(new CompanyTerritory { TerritoryId = third.Id, CompanyId = tan.Id, IsActive = true });

        var moved = world.D.Territories.AddCompanies(sara, south.Id, [tan.Id, tan.Id]);
        Assert.True(moved.Ok, moved.Message);
        Assert.Equal("1 company put in South.", moved.Message);
        var links = world.Store.Find<CompanyTerritory>(Query.Where("CompanyId", tan.Id));
        Assert.Equal(3, links.Count);
        Assert.Equal(south.Id, links.Single(l => l.IsActive).TerritoryId);
        Assert.All(links.Where(l => !l.IsActive), l => Assert.Equal("Sara Admin", l.RemovedBy));
        Assert.Equal(TestWorld.Ken.Login, world.D.Territories.OwnerOf(tan.Id));
        var move = world.D.Territories.Movements(tan.Id).First();
        Assert.Equal(TestWorld.Sam.Login, move.FromUser);
        Assert.Equal(TestWorld.Ken.Login, move.ToUser);
        Assert.Equal(TestWorld.SalesAdmin.Login, move.UpdatedBy);
        Assert.Equal(world.Now, move.UpdatedAt);

        // Already there: said so; put back in North later: a new link (the old one stays inactive).
        Assert.False(world.D.Territories.AddCompanies(sara, south.Id, [tan.Id]).Ok);
        Assert.True(world.D.Territories.AddCompanies(sara, north.Id, [tan.Id]).Ok);
        Assert.Equal(4, world.Store.Count<CompanyTerritory>(Query.Where("CompanyId", tan.Id)));
        Assert.Equal(2, world.D.Territories.Companies(north.Id, null, 0).Total);
        Assert.Single(world.D.Territories.Companies(north.Id, "L1000", 0).Rows);
    }

    [Fact]
    public void Companies_taken_out_have_no_salesperson_and_the_move_is_recorded()
    {
        using var world = new TestWorld();
        var sara = world.Actor(TestWorld.SalesAdmin);
        var tan = world.Company("Tan", "T1000");
        var north = world.Territory("North", 1, TestWorld.Sam, tan.Id);
        Assert.False(world.D.Territories.RemoveCompanies(sara, north.Id, [999]).Ok);
        Assert.True(world.D.Territories.RemoveCompanies(sara, north.Id, [tan.Id]).Ok);
        Assert.Equal("", world.D.Territories.OwnerOf(tan.Id));
        var move = world.D.Territories.Movements(tan.Id).First();
        Assert.Equal(TestWorld.Sam.Login, move.FromUser);
        Assert.Equal("", move.ToUser);
        Assert.False(world.Store.Find<CompanyTerritory>().Single().IsActive);
        Assert.True(world.D.Territories.Search("", unassignedOnly: true, 0).Rows.Any(r => r.Id == tan.Id));
    }

    [Fact]
    public void A_new_salesperson_takes_all_the_territorys_companies()
    {
        using var world = new TestWorld();
        var sara = world.Actor(TestWorld.SalesAdmin);
        var tan = world.Company("Tan", "T1000");
        var lim = world.Company("Lim", "L1000");
        var north = world.Territory("North", 1, TestWorld.Sam, tan.Id, lim.Id);
        var result = world.D.Territories.Save(sara, north.Id, Input("North", 1, TestWorld.Ken));
        Assert.True(result.Ok, result.Message);
        Assert.Contains("2 companies are now Ken Kwan's", result.Message);
        Assert.Equal(TestWorld.Ken.Login, world.D.Territories.OwnerOf(lim.Id));
        Assert.All(new[] { tan.Id, lim.Id }, c =>
        {
            var move = world.D.Territories.Movements(c).First();
            Assert.Equal(TestWorld.Sam.Login, move.FromUser);
            Assert.Equal(TestWorld.Ken.Login, move.ToUser);
        });
        // Renamed only: no movement.
        var before = world.Store.Count<TerritoryMovement>();
        Assert.True(world.D.Territories.Save(sara, north.Id, Input("North Coast", 1, TestWorld.Ken)).Ok);
        Assert.Equal(before, world.Store.Count<TerritoryMovement>());
    }

    [Fact]
    public void A_territory_taken_away_is_inactive_with_its_companies_links()
    {
        using var world = new TestWorld();
        var sara = world.Actor(TestWorld.SalesAdmin);
        var tan = world.Company("Tan", "T1000");
        var north = world.Territory("North", 1, TestWorld.Sam, tan.Id);
        var result = world.D.Territories.Remove(sara, north.Id);
        Assert.True(result.Ok, result.Message);
        var t = world.D.Territories.Get(north.Id)!;
        Assert.False(t.IsActive);
        Assert.Equal("Sara Admin", t.RemovedBy);
        Assert.False(world.Store.Find<CompanyTerritory>().Single().IsActive);
        Assert.Equal("", world.D.Territories.Movements(tan.Id).First().ToUser);
        Assert.DoesNotContain(world.D.Territories.All(), x => x.Id == north.Id);
        Assert.Contains(world.D.Territories.All(includeInactive: true), x => x.Id == north.Id);
        Assert.Equal(ResultCodes.NotFound, world.D.Territories.Remove(sara, north.Id).Code);
        // Its number is free again.
        Assert.True(world.D.Territories.Save(sara, 0, Input("North", 1)).Ok);
    }

    [Fact]
    public void A_pasted_list_finds_companies_by_name_or_BRN()
    {
        using var world = new TestWorld();
        var tan = world.Company("Tan & Sons", "T1000");
        var lim = world.Company("Lim Pte Ltd", "L1000");
        // A company without a BRN must not match every line with an empty BRN (MyCRM's bulk import did).
        world.Company("Home User");
        var north = world.Territory("North", 1, TestWorld.Sam, tan.Id);
        var matches = world.D.Territories.MatchList(north.Id, "Lim Pte Ltd\n\nl1000\nNobody\t\nTan & Sons\tT1000\nUnknown name\t");
        Assert.Equal(5, matches.Count);
        Assert.Equal(lim.Id, matches[0].CompanyId);
        Assert.Equal(1, matches[0].Line);
        Assert.Equal(lim.Id, matches[1].CompanyId);
        Assert.Equal(3, matches[1].Line);
        Assert.Null(matches[2].CompanyId);
        Assert.Equal("Company could not be found.", matches[2].Error);
        Assert.Equal(tan.Id, matches[3].CompanyId);
        Assert.Equal("Company is already in this territory.", matches[3].Error);
        Assert.Equal("Sam Sales", matches[3].Salesperson);
        Assert.Null(matches[4].CompanyId);
    }

    [Fact]
    public void Territories_are_kept_by_Sales_Admins_and_MD()
    {
        using var world = new TestWorld();
        Assert.True(world.Actor(TestWorld.Dina).CanManageTerritories);
        Assert.True(world.D.Territories.Save(world.Actor(TestWorld.Dina), 0, Input("Dina's", 9)).Ok);
        foreach (var user in new[] { TestWorld.Sam, TestWorld.Mia, TestWorld.Mark, TestWorld.Olga })
        {
            Assert.Equal(ResultCodes.Forbidden, world.D.Territories.Save(world.Actor(user), 0, Input("X", 10)).Code);
            Assert.Equal(403, world.Call("GET", "territories", user: user).Status);
        }
        Assert.Equal(200, world.Call("GET", "territories", user: TestWorld.SalesAdmin).Status);
        var salespeople = world.D.Territories.Salespeople();
        Assert.Equal(new[] { TestWorld.Ken.Login, TestWorld.Sam.Login }, salespeople.Select(p => p.Login));
    }
}
