param([Parameter(Mandatory=$true)][string[]]$Paths)
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class MusicTideFileLocks {
 [StructLayout(LayoutKind.Sequential)] public struct Unique { public uint Pid; public System.Runtime.InteropServices.ComTypes.FILETIME Started; }
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] public struct Info {
  public Unique Process;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string App;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)] public string Service;
  public uint Type,Status,Session;
  [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
 }
 [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)] static extern int RmStartSession(out uint session,uint flags,string key);
 [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)] static extern int RmRegisterResources(uint session,uint fileCount,string[] files,uint appCount,IntPtr apps,uint serviceCount,IntPtr services);
 [DllImport("rstrtmgr.dll")] static extern int RmGetList(uint session,out uint needed,ref uint count,[In,Out] Info[] info,ref uint reasons);
 [DllImport("rstrtmgr.dll")] static extern int RmEndSession(uint session);
 public static Info[] Find(string[] paths) {
  uint session;int status=RmStartSession(out session,0,Guid.NewGuid().ToString("N")); if(status!=0)throw new Exception("RmStartSession: "+status);
  try {
   status=RmRegisterResources(session,(uint)paths.Length,paths,0,IntPtr.Zero,0,IntPtr.Zero);if(status!=0)throw new Exception("RmRegisterResources: "+status);
   uint needed,count=0,reasons=0;status=RmGetList(session,out needed,ref count,null,ref reasons);
   if(status==0)return new Info[0];if(status!=234)throw new Exception("RmGetList: "+status);
   count=needed;var result=new Info[count];status=RmGetList(session,out needed,ref count,result,ref reasons);if(status!=0)throw new Exception("RmGetList: "+status);Array.Resize(ref result,(int)count);return result;
  } finally { RmEndSession(session); }
 }
}
'@
[MusicTideFileLocks]::Find($Paths) | ForEach-Object { [pscustomobject]@{Pid=$_.Process.Pid; App=$_.App; Service=$_.Service} }
