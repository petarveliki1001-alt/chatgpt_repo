namespace PersonalMemoirsReader;
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool smoke=args.Length==4 && args[0]=="--smoke";
        if(smoke)
        {
            Environment.SetEnvironmentVariable("MEMOIRS_MODEL_ROOT",Path.GetFullPath(args[1]));
            Environment.SetEnvironmentVariable("MEMOIRS_DATA_ROOT",Path.Combine(Path.GetFullPath(args[3]),"data"));
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        }
        ApplicationConfiguration.Initialize();
        using var form=new MainForm();
        int exitCode=0;
        if(smoke) form.Shown+=async (_,_)=>
        {
            try { await form.RunDesktopSmokeAsync(Path.GetFullPath(args[2]),Path.GetFullPath(args[3])); }
            catch(Exception ex)
            {
                exitCode=1; Directory.CreateDirectory(args[3]);
                File.WriteAllText(Path.Combine(args[3],"failure.txt"),ex.ToString());
            }
            finally { form.Close(); }
        };
        Application.Run(form);
        return exitCode;
    }
}
