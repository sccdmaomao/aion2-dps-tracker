using System.Windows;
namespace LocalDps;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if(e.Args.Length==5&&e.Args[0]=="--check-recording")
        {
            try{var window=new MainWindow(persistKnownNames:false);var result=window.CheckRecording(e.Args[1],int.Parse(e.Args[3]),int.Parse(e.Args[4]));File.WriteAllText(e.Args[2],System.Text.Json.JsonSerializer.Serialize(result));window.Close();Shutdown(0);}
            catch(Exception ex){File.WriteAllText(e.Args[2],ex.ToString());Shutdown(1);}
            return;
        }
        if(e.Args.Contains("--self-test"))
        {
            try { SelfTests.Run(); Shutdown(0); }
            catch(Exception ex) { System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"self-test-failure.txt"),ex.ToString()); Shutdown(1); }
            return;
        }
        new MainWindow().Show();
    }
}
