using System; using System.IO; using System.Reflection; using System.Windows.Forms;
class UiCheck {
    [STAThread] static int Main(string[] args) {
        try {
            Application.EnableVisualStyles(); var type=Assembly.LoadFrom(args[0]).GetType("Setup");
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var ctor=type.GetConstructor(flags,null,new[]{typeof(string),typeof(string)},null);
            using(var form=(Form)ctor.Invoke(new object[]{null,null})) {
                form.Show(); Application.DoEvents();
                var full=(RadioButton)type.GetField("fullChoice",flags).GetValue(form);
                var offline=(RadioButton)type.GetField("offlineChoice",flags).GetValue(form);
                var button=(Button)type.GetField("install",flags).GetValue(form);
                Check(full.Checked && !offline.Checked,"default complete installation");
                offline.Checked=true; Check(button.Text.Contains("离线"),"offline button caption follows selection");
                Check(!((TextBox)type.GetField("pathBox",flags).GetValue(form)).ReadOnly,"new installation location editable");
                type.GetMethod("DownloadProgress",flags).Invoke(form,new object[]{52428800L,104857600L,2.5}); Application.DoEvents();
                Check(((Label)type.GetField("status",flags).GetValue(form)).Text.Contains("50.0%"),"download percentage");
                Check(((Label)type.GetField("detail",flags).GetValue(form)).Text.Contains("2.5 MB/s"),"download speed");
                form.Close();
            }
            using(var form=(Form)ctor.Invoke(new object[]{args[1],null}))
                Check(((TextBox)type.GetField("pathBox",flags).GetValue(form)).ReadOnly,"completion keeps existing install location");
            var validate=type.GetMethod("ValidateInstallPath",BindingFlags.Static|BindingFlags.NonPublic);
            Check((string)validate.Invoke(null,new object[]{args[1]})==Path.GetFullPath(args[1]),"custom path supports spaces and Chinese");
            foreach(var path in new[]{"relative", "C:\\", "C:MusicTide"}) {
                bool rejected=false; try{validate.Invoke(null,new object[]{path});}catch(TargetInvocationException){rejected=true;}
                Check(rejected,"invalid path rejected: "+path);
            }
            return 0;
        } catch(Exception e){Console.WriteLine(e);return 1;}
    }
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
}
