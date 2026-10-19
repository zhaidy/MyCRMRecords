namespace MyCRMRecords.Tests;

/// <summary>
/// The CRM users and teams (MyCRM's Users screens): a person's MyCRM role and teams, changed in place (MyCRM deleted and
/// made the records again), with MyCRM's rules on how many teams a role takes.
/// </summary>
public sealed class CrmUserTests
{
    private static CrmUsers.ProfileInput Profile(string role, params long[] teams) =>
        new() { Role = role, Designation = "Account Manager", PhoneNumber = "61234567", SalesmanCode = "S01", TerritoryCode = "T01", Teams = teams.ToList() };

    private static List<CrmUser> Records(TestWorld world, ApiUser user) =>
        world.Store.Find<CrmUser>(Query.Where("Login", user.Login)).OrderBy(u => u.Id).ToList();

    [Fact]
    public void A_salesperson_changes_team_in_place_and_keeps_their_record()
    {
        using var world = new TestWorld();
        var sara = world.Actor(TestWorld.SalesAdmin);
        var before = Records(world, TestWorld.Sam).Single();
        before.CId = 77;
        before.ProfileId = 5;
        world.Store.Update(before);
        var result = world.D.Users.Save(sara, TestWorld.Sam.Login, Profile(CrmRoles.Sales, world.KenTeam));
        Assert.True(result.Ok, result.Message);
        Assert.Equal("Sam Sales saved: Sales, Ken Kwan.", result.Message);
        var after = Records(world, TestWorld.Sam).Single();
        // The same record (MyCRM made a new one, losing the old CRM Id, the profile and when it was made).
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(world.KenTeam, after.TeamId);
        Assert.Equal(77, after.CId);
        Assert.Equal(5, after.ProfileId);
        Assert.Equal("Account Manager", after.Designation);
        Assert.Equal(TestWorld.SalesAdmin.Login, after.UpdatedBy);
        Assert.Equal(world.Now, after.UpdatedAt);
    }

    [Fact]
    public void The_teams_a_role_takes_follow_MyCRM()
    {
        using var world = new TestWorld();
        var sara = world.Actor(TestWorld.SalesAdmin);
        Assert.Contains(world.D.Users.Save(sara, TestWorld.Sam.Login, Profile(CrmRoles.Sales)).Errors, e => e.Field == "teams");
        Assert.Contains(world.D.Users.Save(sara, TestWorld.Sam.Login, Profile(CrmRoles.Sales, world.MiaTeam, world.KenTeam)).Errors, e => e.Field == "teams");
        Assert.Contains(world.D.Users.Save(sara, TestWorld.Sam.Login, Profile("NoSuchRole", world.MiaTeam)).Errors, e => e.Field == "role");
        // A manager is put in the team named after them.
        Assert.Contains(world.D.Users.Save(sara, TestWorld.Mia.Login, Profile(CrmRoles.Manager, world.KenTeam)).Errors, e => e.Field == "teams");
        Assert.True(world.D.Users.Save(sara, TestWorld.Mia.Login, Profile(CrmRoles.Manager, world.MiaTeam)).Ok);
        // Every other role: any number of teams, one record each.
        var olga = world.D.Users.Save(sara, TestWorld.Olga.Login, Profile(CrmRoles.Marketing, world.MiaTeam, world.KenTeam));
        Assert.True(olga.Ok, olga.Message);
        Assert.Equal(2, Records(world, TestWorld.Olga).Count(u => u.IsActive));
        Assert.True(world.D.Users.PersonOf(TestWorld.Olga.Login).IsCrmUser);
        Assert.Equal(CrmRoles.Marketing, world.D.People.CrmRoleOf(TestWorld.Olga.Login));
        // Inactive teams cannot be chosen.
        var gone = world.Store.Insert(new CrmTeam { Name = "Old team", IsActive = false });
        Assert.Contains(world.D.Users.Save(sara, TestWorld.Olga.Login, Profile(CrmRoles.Marketing, gone.Id)).Errors, e => e.Field == "teams");
    }

    [Fact]
    public void Teams_taken_away_leave_inactive_records_without_their_team_and_with_the_current_role()
    {
        using var world = new TestWorld();
        var sara = world.Actor(TestWorld.SalesAdmin);
        Assert.True(world.D.Users.Save(sara, TestWorld.Olga.Login, Profile(CrmRoles.Marketing, world.MiaTeam, world.KenTeam)).Ok);
        var first = Records(world, TestWorld.Olga)[0];
        world.Now = world.Now.AddDays(1);
        // Now Head in Ken's team only: the record of Mia's team is inactive, has no team (MyCRM reads every record's team), and
        // carries the new role (MyCRM's Role() reads the first record, active or not).
        Assert.True(world.D.Users.Save(sara, TestWorld.Olga.Login, Profile(CrmRoles.Head, world.KenTeam)).Ok);
        var records = Records(world, TestWorld.Olga);
        Assert.Equal(2, records.Count);
        var off = records.Single(u => !u.IsActive);
        Assert.Null(off.TeamId);
        Assert.Equal(CrmRoles.Head, off.Role);
        Assert.NotNull(off.FormerTeamId);
        Assert.Equal(world.Now, off.RemovedOn);
        Assert.Equal("Sara Admin", off.RemovedBy);
        Assert.Equal(CrmRoles.Head, world.D.People.CrmRoleOf(TestWorld.Olga.Login));
        Assert.Equal(first.Id, records[0].Id);

        // A team added again uses the inactive record, no new one.
        Assert.True(world.D.Users.Save(sara, TestWorld.Olga.Login, Profile(CrmRoles.Head, world.KenTeam, world.MiaTeam)).Ok);
        records = Records(world, TestWorld.Olga);
        Assert.Equal(2, records.Count);
        Assert.All(records, u => Assert.True(u.IsActive));
        Assert.Contains(records, u => u.TeamId == world.MiaTeam && u.RemovedOn is null && u.RemovedBy == "");
    }

    [Fact]
    public void With_no_team_a_person_is_no_longer_a_CRM_user_but_a_CSST_keeps_a_record()
    {
        using var world = new TestWorld();
        var sara = world.Actor(TestWorld.SalesAdmin);
        var result = world.D.Users.Save(sara, TestWorld.Mark.Login, Profile(CrmRoles.Marketing));
        Assert.True(result.Ok);
        Assert.Equal("Mark Marketing has no team, so is no longer a CRM user.", result.Message);
        var mark = Records(world, TestWorld.Mark).Single();
        Assert.False(mark.IsActive);
        Assert.Equal("", mark.Role);
        Assert.False(world.D.Users.PersonOf(TestWorld.Mark.Login).IsCrmUser);
        Assert.Equal("", world.D.People.CrmRoleOf(TestWorld.Mark.Login));

        // A CSST with no team still has one active record, with no team (as MyCRM).
        var csst = world.D.Users.Save(sara, TestWorld.Olga.Login, Profile(CrmRoles.Csst));
        Assert.True(csst.Ok, csst.Message);
        var olga = Records(world, TestWorld.Olga).Single();
        Assert.True(olga.IsActive);
        Assert.Null(olga.TeamId);
        Assert.Equal(CrmRoles.Csst, olga.Role);
        Assert.Contains(world.D.Users.Save(sara, TestWorld.Olga.Login, Profile(CrmRoles.Csst, world.MiaTeam, world.KenTeam)).Errors, e => e.Field == "teams");
    }

    [Fact]
    public void Removing_a_user_and_their_territories_marks_everything_inactive()
    {
        using var world = new TestWorld();
        var sara = world.Actor(TestWorld.SalesAdmin);
        var tan = world.Company("Tan", "T1000");
        var north = world.Territory("North", 1, TestWorld.Sam, tan.Id);
        var result = world.D.Users.RemoveUserAndTerritories(sara, TestWorld.Sam.Login);
        Assert.True(result.Ok, result.Message);
        Assert.Equal("Sam Sales is no longer a CRM user; 1 territory was taken away.", result.Message);
        var sam = Records(world, TestWorld.Sam).Single();
        Assert.False(sam.IsActive);
        Assert.Null(sam.TeamId);
        Assert.Equal(world.MiaTeam, sam.FormerTeamId);
        Assert.False(world.D.Territories.Get(north.Id)!.IsActive);
        Assert.False(world.Store.Find<CompanyTerritory>().Single().IsActive);
        Assert.Equal(ResultCodes.NotFound, world.D.Users.RemoveUserAndTerritories(sara, TestWorld.Sam.Login).Code);
    }

    [Fact]
    public void Teams_are_made_renamed_and_switched_off_never_deleted()
    {
        using var world = new TestWorld();
        var sara = world.Actor(TestWorld.SalesAdmin);
        var created = world.D.Users.SaveTeam(sara, 0, new CrmUsers.TeamInput { Name = "Lee Team", ShortName = "lee" });
        Assert.True(created.Ok, created.Message);
        Assert.Contains(world.D.Users.SaveTeam(sara, 0, new CrmUsers.TeamInput { Name = "lee team" }).Errors, e => e.Field == "name");
        Assert.True(world.D.Users.SaveTeam(sara, created.Id!.Value, new CrmUsers.TeamInput { Name = "Lee Team", IsActive = false }).Ok);
        Assert.False(world.Store.Get<CrmTeam>(created.Id!.Value)!.IsActive);
        Assert.DoesNotContain(world.D.Users.Teams(includeInactive: false), t => t.Id == created.Id);

        // MyCRM's Create Team: the team named after the person, once.
        var ken = world.D.Users.CreateTeamFor(sara, TestWorld.Ken.Login);
        Assert.Equal("Team Ken Kwan already exists.", ken.Message);
        Assert.Equal(world.KenTeam, ken.Id);
        var olga = world.D.Users.CreateTeamFor(sara, TestWorld.Olga.Login);
        Assert.Equal("Olga Other", world.Store.Get<CrmTeam>(olga.Id!.Value)!.Name);

        var members = world.D.Users.Members(world.MiaTeam);
        // In MyCRM's role order (Sales, Manager, ..., SalesAdmin, Telemarketer).
        Assert.Equal(new[] { CrmRoles.Sales, CrmRoles.Manager, CrmRoles.SalesAdmin, CrmRoles.Telemarketer }, members.Select(m => m.Role));
    }

    [Fact]
    public void Only_Sales_Admins_keep_the_CRM_users()
    {
        using var world = new TestWorld();
        foreach (var user in new[] { TestWorld.Sam, TestWorld.Mia, TestWorld.Dina })
        {
            Assert.Equal(ResultCodes.Forbidden, world.D.Users.Save(world.Actor(user), TestWorld.Olga.Login, Profile(CrmRoles.Sales, world.MiaTeam)).Code);
            Assert.Equal(403, world.Call("GET", "crmusers", user: user).Status);
        }
        Assert.Equal(200, world.Call("GET", "crmusers", user: TestWorld.SalesAdmin).Status);
        // MyCRM's IsSalesAdmin reads every record, active or not.
        Assert.True(world.Actor(TestWorld.SalesAdmin).IsSalesAdmin);
        var list = world.D.Users.List(null);
        Assert.Contains(list, p => p.Login == TestWorld.Sam.Login && p.Role == CrmRoles.Sales && p.Teams.Single() == world.MiaTeam);
        Assert.DoesNotContain(list, p => p.Login == TestWorld.Olga.Login);
        Assert.Contains(world.D.Users.List("olga"), p => p.Login == TestWorld.Olga.Login && !p.IsCrmUser);
    }
}
