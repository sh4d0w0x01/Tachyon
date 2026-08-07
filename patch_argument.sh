sed -i 's/if (!drive.EndsWith(":\\\\")) drive = drive.Substring(0, 1) + ":\\\\";/if (drive.Length > 0 \&\& !drive.EndsWith(":\\\\")) drive = drive.Substring(0, 1) + ":\\\\";/' MftSearch/Program.cs
