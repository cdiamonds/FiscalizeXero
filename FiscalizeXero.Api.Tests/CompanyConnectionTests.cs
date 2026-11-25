using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FiscalizeXero.Api.Tests;

/// <summary>
/// One Xero connection per Fiscalize company: whichever colleague connected it, every member of the company
/// resolves to the same row (and so to the devices and fiscal records stored under it).
/// </summary>
public class CompanyConnectionTests
{
    private const long Company = 4242;

    private static DataContext NewDb() =>
        new(new DbContextOptionsBuilder<DataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static AppUserCompany Member(DataContext db, long userId, long companyId = Company)
    {
        var user = new AppUser { UserId = userId, Email = $"u{userId}@example.test" };
        var auc = new AppUserCompany { AppUser = user, CompanyId = companyId, CompanyName = "Acme" };
        db.AddRange(user, auc);
        db.SaveChanges();
        return auc;
    }

    private static ClientConfig Config(DataContext db, AppUserCompany auc, string? refresh, Guid? tenant)
    {
        var c = new ClientConfig { AppUserCompanyId = auc.Id, State = Guid.NewGuid(), RefreshToken = refresh, TenantId = tenant };
        db.Configs.Add(c);
        db.SaveChanges();
        return c;
    }

    [Fact]
    public async Task Colleague_resolves_to_the_connection_another_member_set_up()
    {
        using var db = NewDb();
        var owner = Member(db, 1);
        Member(db, 2); // signed in later, never connected
        var live = Config(db, owner, "rt", Guid.NewGuid());

        var found = await new ClientConfigRepository(db).GetForCompanyAsync(Company);

        Assert.Equal(live.Id, found?.Id);
        Assert.Equal(owner.Id, found!.AppUserCompanyId);
    }

    [Fact]
    public async Task Live_connection_beats_an_abandoned_row_of_the_signed_in_user()
    {
        using var db = NewDb();
        var colleague = Member(db, 1);
        var me = Member(db, 2);
        Config(db, me, refresh: null, tenant: null);             // e.g. saved settings before anyone connected
        var org = Guid.NewGuid();
        var cleared = Config(db, Member(db, 3), null, org);      // tokens cleared after Xero refused them, organisation still known
        var live = Config(db, colleague, "rt", org);

        var repo = new ClientConfigRepository(db);
        Assert.Equal(live.Id, (await repo.GetForCompanyAsync(Company))?.Id);

        db.Configs.Remove(live);
        db.SaveChanges();
        Assert.Equal(cleared.Id, (await repo.GetForCompanyAsync(Company))?.Id);
    }

    [Fact]
    public async Task Another_fiscalize_company_is_never_returned()
    {
        using var db = NewDb();
        Config(db, Member(db, 1, companyId: 999), "rt", Guid.NewGuid());

        Assert.Null(await new ClientConfigRepository(db).GetForCompanyAsync(Company));
    }

    [Fact]
    public async Task Members_are_counted_per_company()
    {
        using var db = NewDb();
        Member(db, 1);
        Member(db, 2);
        Member(db, 3, companyId: 999);

        Assert.Equal(2, await new AppUserCompanyRepository(db).CountMembersAsync(Company));
    }

    [Fact]
    public async Task Reconnecting_lands_on_the_row_that_already_holds_the_devices()
    {
        using var db = NewDb();
        Member(db, 1);
        var deviceHolder = Member(db, 2);
        Member(db, 3);
        db.DeviceConfigs.Add(new AppUserDeviceConfig { AppUserCompanyId = deviceHolder.Id, DeviceId = 36822, DeviceNumber = 1, IsDefault = true });
        db.SaveChanges();

        var repo = new AppUserCompanyRepository(db);
        Assert.Equal(deviceHolder.Id, await repo.GetDataHolderIdAsync(Company));
        Assert.Null(await repo.GetDataHolderIdAsync(999));
    }
}
