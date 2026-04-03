using MassLens.Core;

namespace MassLens.Tests;

public class MassLensOptionsTests
{
    [Fact]
    public void Default_options_are_enabled()
    {
        var opts = new MassLensOptions();
        Assert.True(opts.Enabled);
    }

    [Fact]
    public void Default_base_path_is_masslens()
    {
        var opts = new MassLensOptions();
        Assert.Equal("/masslens", opts.BasePath);
    }

    [Fact]
    public void Default_readonly_is_false()
    {
        var opts = new MassLensOptions();
        Assert.False(opts.ReadOnly);
    }

    [Fact]
    public void Default_sqlite_is_disabled()
    {
        var opts = new MassLensOptions();
        Assert.False(opts.EnableSqliteAuditLog);
    }

    [Fact]
    public void Default_channel_capacity_is_10000()
    {
        var opts = new MassLensOptions();
        Assert.Equal(10_000, opts.ChannelCapacity);
    }

    [Fact]
    public void Default_recent_entries_capacity_is_500()
    {
        var opts = new MassLensOptions();
        Assert.Equal(500, opts.RecentEntriesCapacity);
    }

    [Fact]
    public void Default_audit_log_capacity_is_200()
    {
        var opts = new MassLensOptions();
        Assert.Equal(200, opts.AuditLogCapacity);
    }

    [Fact]
    public void Default_disable_in_production_is_true()
    {
        var opts = new MassLensOptions();
        Assert.True(opts.DisableInProduction);
    }

    [Fact]
    public void AllowedIPs_defaults_to_empty_array()
    {
        var opts = new MassLensOptions();
        Assert.Empty(opts.AllowedIPs);
    }
}
