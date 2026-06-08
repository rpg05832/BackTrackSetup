using System;
using System.Collections.Generic;
using System.IO;

namespace BackTrack
{
    public class FsEntry
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public bool IsDir { get; set; }
    }

    public class FsListing
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Parent { get; set; }
        public bool Ok { get; set; } = true;
        public List<FsEntry> Entries { get; set; } = new List<FsEntry>();
    }

    public static class FileBrowser
    {
        public static FsListing List(string? path)
        {
            var listing = new FsListing();

            // Empty path => "This PC": list the drives.
            if (string.IsNullOrWhiteSpace(path))
            {
                listing.Path = "";
                listing.Name = "המחשב שלי";
                listing.Parent = null;
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        string label = "";
                        try { if (d.IsReady && !string.IsNullOrEmpty(d.VolumeLabel)) label = " (" + d.VolumeLabel + ")"; } catch { }
                        listing.Entries.Add(new FsEntry { Name = d.Name + label, Path = d.RootDirectory.FullName, IsDir = true });
                    }
                    catch { }
                }
                return listing;
            }

            listing.Path = path;
            try
            {
                var di = new DirectoryInfo(path);
                listing.Name = string.IsNullOrEmpty(di.Name) ? path : di.Name;
                listing.Parent = di.Parent?.FullName; // null at a drive root => client goes back to "This PC"

                foreach (var dir in di.GetDirectories())
                {
                    try
                    {
                        if ((dir.Attributes & FileAttributes.Hidden) != 0 || (dir.Attributes & FileAttributes.System) != 0) continue;
                    }
                    catch { }
                    listing.Entries.Add(new FsEntry { Name = dir.Name, Path = dir.FullName, IsDir = true });
                }
                foreach (var f in di.GetFiles())
                {
                    try
                    {
                        if ((f.Attributes & FileAttributes.Hidden) != 0 || (f.Attributes & FileAttributes.System) != 0) continue;
                    }
                    catch { }
                    listing.Entries.Add(new FsEntry { Name = f.Name, Path = f.FullName, IsDir = false });
                }
            }
            catch (Exception)
            {
                listing.Ok = false;
            }
            return listing;
        }

        public static bool Open(string path)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
                return true;
            }
            catch { return false; }
        }
    }
}
