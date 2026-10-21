using System.IO;
using AlTamam.Application.Authentication;
using AlTamam.Application.Attendance;
using AlTamam.Application.Identity;
using AlTamam.Application.Registry;
using AlTamam.Domain.Abstractions;
using AlTamam.Infrastructure.Persistence;
using AlTamam.Infrastructure.Security;

namespace AlTamam.Desktop;

public sealed class DesktopRuntime : IDisposable
{
    public string Root {get;}
    public AlTamamDbContext Database {get;}
    public ISystemClock Clock {get;}
    public AuthenticationService Authentication {get;}
    public PersonRosterService Roster {get;}
    public ConfigurationService Configuration {get;}
    public PersonIdentityService Identity {get;}
    public AttendanceService Attendance {get;}
    internal string DatabaseKey {get;}
    private DesktopRuntime(string root,AlTamamDbContext database,string key,ISystemClock clock)
    {
        Root=root;Database=database;DatabaseKey=key;Clock=clock;
        Authentication=new AuthenticationService(new SqliteAuthenticationStore(database,clock));
        Attendance=new AttendanceService(new SqliteAttendanceStore(database,clock));
        var registry=new SqliteRegistryStore(database,clock);
        Roster=new PersonRosterService(registry);Configuration=new ConfigurationService(registry);
        Identity=new PersonIdentityService(new SqlitePersonIdentityStore(database,clock),clock);
    }
    public static async Task<DesktopRuntime> CreateAsync(string? root=null,CancellationToken cancellationToken=default)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows is required.");
        root=Path.GetFullPath(root??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AlTamamAlYawmi"));
        var marker=Path.Combine(root,"AlTamam.application");
        if(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any()&&!File.Exists(marker))
            throw new InvalidOperationException("The selected directory does not belong to this application.");
        Directory.CreateDirectory(root);
        if(!File.Exists(marker))await File.WriteAllTextAsync(marker,"AlTamamAlYawmi",cancellationToken);
        var path=Path.Combine(root,"Data","altamam.db");
        var key=new WindowsDatabaseKeyStore(Path.Combine(root,"Security","database.key")).LoadOrCreate(path);
        var database=new AlTamamDbContext(path,key);
        try {await database.InitializeAsync(cancellationToken);return new DesktopRuntime(root,database,key,new SystemClock());}
        catch {database.Dispose();throw;}
    }
    public void Dispose()=>Database.Dispose();
}
