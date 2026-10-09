using System;using System.IO;using System.Reflection;using System.Net;
class ResumeCheck{
[STAThread]static int Main(string[] a){try{
ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
var t=Assembly.LoadFrom(a[0]).GetType("Setup");var ctor=t.GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(string),typeof(string)},null);
var instance=ctor.Invoke(new object[]{a[2],null});t.GetField("report",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(instance,new Action<string>(Console.WriteLine));
Directory.CreateDirectory(a[2]);var target=Path.Combine(a[2],"downloaded-config");var bytes=File.ReadAllBytes(a[1]);using(var f=File.Create(target+".download"))f.Write(bytes,0,128);
var method=t.GetMethod("Download",BindingFlags.Instance|BindingFlags.NonPublic);const string hash="377ac4d7aeefd5b870c9fccff9a6d4df36901d99fe3277c2f755bc401601ba1c";
method.Invoke(instance,new object[]{"https://registry.ollama.ai/v2/library/qwen2.5/blobs/sha256:"+hash,target,hash});
if(!File.Exists(target)||new FileInfo(target).Length!=487)throw new Exception("wrong download");Console.WriteLine("PASS actual model registry resumed download and SHA256");
File.Copy(target,target+".download",true);method.Invoke(instance,new object[]{"https://registry.ollama.ai/v2/library/qwen2.5/blobs/sha256:"+hash,target,hash});Console.WriteLine("PASS completed partial download recovered without HTTP 416");return 0;
}catch(Exception e){Console.WriteLine(e);return 1;}}
}