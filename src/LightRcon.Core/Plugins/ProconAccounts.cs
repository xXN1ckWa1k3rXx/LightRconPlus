namespace LightRcon.Core.Plugins;

internal sealed record ProconAccount(string Name, uint Privileges);

internal static class ProconAccounts
{
    // PRoCon.Core.Privileges bits. No Layer password or password hash crosses the bridge.
    public static uint Map(LayerPermission permissions)
    {
        if (!permissions.HasFlag(LayerPermission.View)) return 0;
        uint flags = 0x01 | 0x20000; // Login and registered plugin commands.
        if (permissions.HasFlag(LayerPermission.Settings)) flags |= 0x02;
        if (permissions.HasFlag(LayerPermission.Maps)) flags |= 0x04 | 0x400;
        if (permissions.HasFlag(LayerPermission.Players)) flags |= 0x10 | 0x10000 | 0x40000 | 0x1000;
        if (permissions.HasFlag(LayerPermission.Bans)) flags |= 0x20 | 0x40 | 0x800;
        if ((permissions & (LayerPermission.Players | LayerPermission.Bans)) != 0) flags |= 0x100;
        if (permissions.HasFlag(LayerPermission.LayerAdmin)) flags |= 0x8000 | 0x80000 | 0x100000;
        if (permissions.HasFlag(LayerPermission.Console)) flags |= 0x4000;
        return flags;
    }

    public static ProconAccount[] Snapshot(ServerSession session)
    {
        lock (session.Profile.Sync)
            return session.Profile.Layer.Accounts.Where(account => account.Enabled)
                .Select(account => new ProconAccount(account.Username, Map(account.Permissions ?? session.Profile.Layer.GlobalPermissions)))
                .Where(account => account.Privileges != 0).ToArray();
    }
}
