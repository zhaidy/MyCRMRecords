namespace MyCRMRecords.Tests;

/// <summary>
/// Who may open which company: MyCRM's UserCanAccessCompany with GetSub's list of people, the permanent territories kept
/// here and MyCRM's temporary ones (its Projects and CSST parts) read from MyCRM.
/// </summary>
public sealed class AccessTests
{
    [Fact]
    public void Each_role_lists_the_people_whose_companies_it_opens()
    {
        using var world = new TestWorld();
        var access = world.D.Access;
        // Sales and others like it: themselves only.
        Assert.Equal(new[] { TestWorld.Sam.Login }, access.UserList(TestWorld.Sam.Login, CrmRoles.Sales));
        Assert.Equal(new[] { TestWorld.Olga.Login }, access.UserList(TestWorld.Olga.Login, ""));
        // A manager: the Sales, CLETrainer, Channel Partner, Telemarketer and Contract people of their teams.
        Assert.Equal(new[] { TestWorld.Mia.Login, TestWorld.Sam.Login, TestWorld.Tom.Login }.OrderBy(l => l),
            access.UserList(TestWorld.Mia.Login, CrmRoles.Manager).OrderBy(l => l));
        // Any other role (Marketing here): everyone of their teams with a role.
        Assert.Equal(new[] { TestWorld.Ken.Login, TestWorld.Mark.Login }.OrderBy(l => l), access.UserList(TestWorld.Mark.Login, CrmRoles.Marketing).OrderBy(l => l));
        // Credit, OPO and Stock Allocator: everyone with an active CRM user record.
        Assert.Equal(7, access.UserList(TestWorld.Olga.Login, CrmRoles.Credit).Count - 1);
        // An inactive record's team still counts (MyCRM reads every record's team): MyCRMRecords empties it when it is taken away.
        var mark = world.Store.Find<CrmUser>(Query.Where("Login", TestWorld.Mark.Login)).Single();
        mark.IsActive = false;
        world.Store.Update(mark);
        Assert.Contains(TestWorld.Ken.Login, access.UserList(TestWorld.Mark.Login, CrmRoles.Marketing));
    }

    [Fact]
    public void Roles_that_see_every_company_open_every_one_others_their_teams_territories()
    {
        using var world = new TestWorld();
        var tan = world.Company("Tan", "T1000");
        var lim = world.Company("Lim", "L1000");
        world.Territory("North", 1, TestWorld.Sam, tan.Id);
        world.Territory("South", 2, TestWorld.Ken, lim.Id);
        var access = world.D.Access;
        foreach (var user in new[] { TestWorld.SalesAdmin, TestWorld.Dina, TestWorld.Tom, TestWorld.Admin })
        {
            Assert.True(world.Actor(user).SeesAllCompanies, user.Login);
            Assert.Null(access.CompanyIds(world.Actor(user)));
            Assert.True(access.CanOpen(world.Actor(user), lim));
        }
        Assert.Equal(new[] { tan.Id }, access.CompanyIds(world.Actor(TestWorld.Sam)));
        Assert.True(access.CanOpen(world.Actor(TestWorld.Sam), tan));
        Assert.False(access.CanOpen(world.Actor(TestWorld.Sam), lim));
        Assert.True(access.CanOpen(world.Actor(TestWorld.Mia), tan));
        Assert.False(access.CanOpen(world.Actor(TestWorld.Mia), lim));
        Assert.True(access.CanOpen(world.Actor(TestWorld.Mark), lim));
        Assert.False(access.CanOpen(world.Actor(TestWorld.Olga), tan));
    }

    [Fact]
    public void An_inactive_company_is_opened_only_by_the_roles_that_see_every_company()
    {
        using var world = new TestWorld();
        var tan = world.Company("Tan", "T1000");
        world.Territory("North", 1, TestWorld.Sam, tan.Id);
        tan = world.D.Companies.Get(tan.Id)!;
        tan.IsActive = false;
        world.Store.Update(tan);
        Assert.False(world.D.Access.CanOpen(world.Actor(TestWorld.Sam), tan));
        Assert.Empty(world.D.Access.CompanyIds(world.Actor(TestWorld.Sam))!);
        Assert.True(world.D.Access.CanOpen(world.Actor(TestWorld.SalesAdmin), tan));
    }

    [Fact]
    public void A_company_of_a_temporary_territory_in_MyCRM_is_opened_by_its_holder_and_their_manager()
    {
        using var world = new TestWorld();
        var old = world.OldApp;
        // As imported: MyCRM's company 5001.
        var project = world.Store.Insert(new Company { Name = "Project Co", Brn = "P1000", IsActive = true, OutSystemsId = 5001 });
        var territory = old.Add("Territory", new() { ["TERRITORY"] = 1015001L, ["OWNERID"] = 101L, ["ISACTIVE"] = true, ["ISTEMPORARY"] = true });
        old.Add("CompanyTerritory", new() { ["TERRITORYID"] = territory, ["COMPANYID"] = 5001L, ["ISACTIVE"] = true });
        Assert.True(world.D.Access.CanOpen(world.Actor(TestWorld.Sam), project));
        Assert.True(world.D.Access.CanOpen(world.Actor(TestWorld.Mia), project));
        Assert.Contains(project.Id, world.D.Access.CompanyIds(world.Actor(TestWorld.Sam))!);
        Assert.False(world.D.Access.CanOpen(world.Actor(TestWorld.Ken), project));
        // The project ended (MyCRM's timer turned the territory off): no longer.
        old.Change("Territory", territory, new() { ["ISACTIVE"] = false });
        Assert.False(world.D.Access.CanOpen(world.Actor(TestWorld.Sam), project));
        // Temporary territories are never copied here.
        Assert.Empty(world.Store.Find<Territory>());
    }
}
