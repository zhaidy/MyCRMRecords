using MyCRMRecords.Data;
using Xunit;

namespace MyCRMRecords.Tests;

public sealed class SettingsFileTests
{
    [Fact]
    public void Settings_are_saved_while_another_process_has_the_file_open_for_a_moment()
    {
        var folder = Path.Combine(Path.GetTempPath(), "mcr-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = new SettingsFile<CrmSettings>(Path.Combine(folder, "crm-records-settings.json"), new FakeProtector());
            file.Change(s => s.Jobs.Enabled = false);
            // Another process reads it without letting it be replaced (as the old website does during a deployment):
            // the save waits and succeeds once it lets go.
            var holder = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var release = Task.Run(async () => { await Task.Delay(600); holder.Dispose(); });
            file.Change(s => s.Jobs.Enabled = true);
            release.Wait();
            Assert.True(file.Load().Jobs.Enabled);
            // Reading never stops a save: Load lets the file be replaced while it reads.
            using (new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                file.Change(s => s.Jobs.Enabled = false);
            Assert.False(file.Load().Jobs.Enabled);
            // A program that keeps the file open for good, letting others write but not replace it: the settings are
            // written into the file itself.
            using (new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                file.Change(s => s.Jobs.Enabled = true);
                Assert.True(file.Load().Jobs.Enabled);
            }
            Assert.True(file.Load().Jobs.Enabled);
        }
        finally { Directory.Delete(folder, true); }
    }
}
