using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using DanBackup.Core.Engine;
using DanBackup.Core.Model;
using DanBackup.Core.Settings;
using Microsoft.Win32;

namespace DanBackup.Core.Modules;

/// <summary>Fontes instaladas pelo usuário (as que não vieram com o Windows).</summary>
public sealed class FontsModule : BackupModuleBase
{
    private static readonly HashSet<string> FontExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ttf", ".otf", ".ttc", ".fon" };

    public override string Id => "fonts";
    public override string Name => "Fontes";
    public override string Description => "Fontes instaladas por você (não as que vêm com o Windows).";

    public override string? Notes =>
        "Fontes que vieram com o Windows são ignoradas. Na restauração, as que ainda não existem são instaladas só para o seu usuário.";

    private static string UserFontsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Fonts");

    public override Task<IReadOnlyList<BackupItem>> DiscoverAsync(AppSettings settings, CancellationToken ct)
    {
        var items = new List<BackupItem>();
        var user = UserFonts().ToList();
        if (user.Count > 0)
            items.Add(new BackupItem { Id = "user-fonts", DisplayName = $"Fontes do usuário ({user.Count})" });

        var system = AddedSystemFonts().ToList();
        if (system.Count > 0)
        {
            items.Add(new BackupItem
            {
                Id = "system-fonts",
                DisplayName = $"Fontes adicionadas ao sistema ({system.Count}) — por você ou por programas como o Office",
                Description = string.Join(", ", system.Take(8).Select(Path.GetFileNameWithoutExtension)) + (system.Count > 8 ? "..." : ""),
            });
        }
        return Task.FromResult<IReadOnlyList<BackupItem>>(items);
    }

    public override Task BackupItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var files = item.Id == "user-fonts" ? UserFonts() : AddedSystemFonts();
        var dir = ctx.GetItemDir(item);
        int count = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if (IO.FileCopier.CopyFile(file, Path.Combine(dir, Path.GetFileName(file)), ctx)) count++;
        }
        ctx.Info($"{count} fonte(s) salvas.");
        return Task.CompletedTask;
    }

    public override Task RestoreItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        Directory.CreateDirectory(UserFontsDir);
        using var reg = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Fonts");
        int installed = 0;
        foreach (var file in Directory.EnumerateFiles(ctx.GetItemDir(item)))
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.Combine(UserFontsDir, Path.GetFileName(file));
            if (File.Exists(target)) continue;
            if (!IO.FileCopier.CopyFile(file, target, ctx)) continue;

            var kind = Path.GetExtension(file).Equals(".otf", StringComparison.OrdinalIgnoreCase) ? "OpenType" : "TrueType";
            reg.SetValue($"{Path.GetFileNameWithoutExtension(file)} ({kind})", target);
            AddFontResource(target);
            installed++;
        }
        PostMessage(new IntPtr(0xFFFF), 0x001D /* WM_FONTCHANGE */, IntPtr.Zero, IntPtr.Zero);
        ctx.Info($"{installed} fonte(s) instalada(s).");
        return Task.CompletedTask;
    }

    private static IEnumerable<string> UserFonts() =>
        Directory.Exists(UserFontsDir)
            ? Directory.EnumerateFiles(UserFontsDir).Where(f => FontExtensions.Contains(Path.GetExtension(f)))
            : [];

    /// <summary>
    /// Fontes em C:\Windows\Fonts que não pertencem ao TrustedInstaller — as do Windows sempre pertencem;
    /// as instaladas por programas ou pelo usuário pertencem a Administradores/SYSTEM.
    /// </summary>
    private static IEnumerable<string> AddedSystemFonts()
    {
        var fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        return Directory.EnumerateFiles(fontsDir)
            .Where(f => FontExtensions.Contains(Path.GetExtension(f)))
            .Where(f => !IsOwnedByTrustedInstaller(f))
            .ToList();
    }

    private static readonly SecurityIdentifier TrustedInstaller =
        new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");

    private static bool IsOwnedByTrustedInstaller(string file)
    {
        try
        {
            return new FileInfo(file).GetAccessControl(AccessControlSections.Owner)
                .GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner && owner == TrustedInstaller;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            return true; // na dúvida, trata como fonte do Windows
        }
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int AddFontResource(string lpFileName);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
