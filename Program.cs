namespace VirtualMonitorWindow;

static class Program
{
    [STAThread]
    static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--self-test")
            return await VirtualMonitorService.RunSelfTestAsync();

        if (args.Length > 0 && args[0] == "--elevated-install")
            return await VirtualMonitorService.RunElevatedInstallAsync();

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
