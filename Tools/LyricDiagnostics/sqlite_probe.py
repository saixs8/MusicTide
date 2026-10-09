import sqlite3
from pathlib import Path
for n in ['KGMusicV3.db','RecentlyMusicV3.db','playlistV3.db']:
 try:
  c=sqlite3.connect('file:C:/Users/Administrator/AppData/Roaming/KuGou8/'+n+'?mode=ro',uri=True)
  for name,sql in c.execute("select name,sql from sqlite_master where type='table'"):
   if any(s in (sql or '').lower() for s in ['progress','position','playtime','curtime','duration','play_time']): print(n,name,sql[:1600])
 except Exception as e: print(n,str(e))
