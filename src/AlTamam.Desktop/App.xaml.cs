using System.Security.Principal;
using System.Windows;

namespace AlTamam.Desktop;

public partial class App : System.Windows.Application
{
    private Mutex? _instance;
    private DesktopRuntime? _runtime;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance=new Mutex(true,"Local\\AlTamamAlYawmi-"+WindowsIdentity.GetCurrent().User!.Value,out var created);
        if(!created){MessageBox.Show("البرنامج يعمل بالفعل.","التمام اليومي");Shutdown();return;}
        try
        {
            _runtime=await DesktopRuntime.CreateAsync();
            MainWindow=new MainWindow(_runtime);MainWindow.Show();
        }
        catch(Exception)
        {
            MessageBox.Show("تعذرت تهيئة البرنامج بأمان. تحقق من صلاحية مجلد البيانات ووقت الجهاز ومفتاح التشفير؛ لا تحذف ملفات البيانات الموجودة.","تعذر التشغيل",MessageBoxButton.OK,MessageBoxImage.Error);
            Shutdown(2);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _runtime?.Dispose();_instance?.Dispose();base.OnExit(e);
    }
}
