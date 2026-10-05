namespace Taskee.Core;

public sealed class SettingsSession
{
    public string FilePath { get; }
    public AppConfig Configuration { get; }
    public bool CanSave { get; private set; }
    public bool IsNew { get; }
    public string Notice { get; private set; } = "";

    internal SettingsSession(string path)
    {
        FilePath=Path.GetFullPath(path);
        bool absent=false;
        try {
            var config=ConfigStore.Read(FilePath);
            if(config.SchemaVersion!=1) {
                Configuration=AppConfig.Default();
                Notice="Your saved profile uses a newer settings version. The original files are protected. Import a compatible profile, or save the current settings as a recovery profile.";
                return;
            }
            ConfigStore.Validate(config);Configuration=config;CanSave=true;return;
        } catch(FileNotFoundException) {absent=true;}
        catch(DirectoryNotFoundException) {absent=true;}
        catch(Exception ex) {Notice="Your saved profile could not be loaded: "+ex.Message;}

        try {
            var backup=ConfigStore.Read(FilePath+".bak");
            ConfigStore.Validate(backup);Configuration=backup;
            Notice="Your profile was recovered from its backup. The original files are protected until you save a recovery profile.";
            return;
        } catch(FileNotFoundException) when(absent) {IsNew=true;CanSave=true;}
        catch(DirectoryNotFoundException) when(absent) {IsNew=true;CanSave=true;}
        catch(Exception ex) {if(Notice.Length==0) Notice="Your saved profile could not be loaded: "+ex.Message;}
        Configuration=AppConfig.Default();
        if(!CanSave) Notice+=" Original files are protected; changes are kept in memory until you save a recovery profile.";
    }

    public void Save(AppConfig config)
    {
        if(!CanSave) throw new InvalidOperationException("The original profile is protected. Save a recovery profile first.");
        ConfigStore.Save(config,FilePath);
    }

    public string Recover(AppConfig config)
    {
        ConfigStore.Validate(config);
        string recovery=Path.Combine(Path.GetDirectoryName(FilePath)!,"recovery",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(recovery);
        // Copy both originals before the atomic save can rotate the backup.
        foreach(string original in new[]{FilePath,FilePath+".bak"}) {
            try {
                using var input=new FileStream(original,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
                using var output=new FileStream(Path.Combine(recovery,Path.GetFileName(original)),FileMode.CreateNew,FileAccess.Write);
                input.CopyTo(output);
            } catch(FileNotFoundException) { }
        }
        ConfigStore.Save(config,FilePath);CanSave=true;Notice="Original profiles preserved in "+recovery;
        return recovery;
    }
}

public sealed class SettingsAutosave(SettingsSession settings)
{
    public bool Pending { get; private set; }
    public long NextAttempt { get; private set; }
    public string Status { get; private set; } = "Changes save automatically";
    public Exception? LastError { get; private set; }
    private int failures;

    public void Changed(long now)
    {
        Pending=true;NextAttempt=now+450;Status=settings.CanSave?"Saving…":"Profile protected · changes kept in memory";
    }
    public bool TrySave(AppConfig config,long now,bool force=false)
    {
        if(!Pending || (!force && now<NextAttempt)) return false;
        if(!settings.CanSave) {Status="Profile protected · changes kept in memory";return false;}
        try {settings.Save(config);Saved();return true;}
        catch(Exception ex) {
            LastError=ex;failures=Math.Min(failures+1,5);
            long delay=Math.Min(10000,1000L<<(failures-1));NextAttempt=now+delay;
            Status=$"Save failed · retrying in {delay/1000}s: {ex.Message}";return false;
        }
    }
    public void Saved() {Pending=false;failures=0;LastError=null;Status="All changes saved";}
}
