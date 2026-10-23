// Bounded exact DotNet path witnesses; no native calls, writes, retries or File.Exists inference.
using System;
using System.IO;

namespace KingdomEnhancedMod;

internal enum PalaceDestinationFileKind { Missing, Present }

internal sealed class PalaceDestinationFileWitness
{
    internal PalaceDestinationFileKind Kind { get; }
    internal string Folder { get; }
    internal string Filename { get; }
    internal string FullPath { get; }
    internal PalaceDestinationFileWitness(PalaceDestinationFileKind kind,string folder,string filename,string path)
    { Kind=kind; Folder=folder; Filename=filename; FullPath=path; }
}

internal static class PalaceDestinationWitness
{
    internal static bool TryObserveFile(string folder,string filename,
        out PalaceDestinationFileWitness witness,out string reason)
    {
        witness=null; reason="";
        if(!TryPath(folder,filename,out string path,out reason))return false;
        try
        {
            var file=new FileInfo(path);
            if(file.LinkTarget!=null || (file.Exists && (file.Attributes&FileAttributes.ReparsePoint)!=0))
            { reason="file-link"; return false; }
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            { if(!stream.CanRead) { reason="file-not-readable"; return false; } }
            if(!TryPath(folder,filename,out string again,out reason) || again!=path || new FileInfo(path).LinkTarget!=null)
            { reason="file-path-drift"; return false; }
            witness=new PalaceDestinationFileWitness(PalaceDestinationFileKind.Present,folder,filename,path);
            return true;
        }
        catch(FileNotFoundException)
        {
            // Missing is only accepted after re-proving the existing, unlinked parent.
            if(!TryPath(folder,filename,out _,out reason) || new FileInfo(path).LinkTarget!=null)return false;
            witness=new PalaceDestinationFileWitness(PalaceDestinationFileKind.Missing,folder,filename,path);
            return true;
        }
        catch(Exception e) when(e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
        { reason="file-unknown:"+e.GetType().Name; return false; }
    }

    internal static bool TryReadFile(string folder,string filename,int maximumBytes,out byte[] bytes,out string reason)
    {
        bytes=null; reason="";
        if(maximumBytes<=0 || !TryPath(folder,filename,out string path,out reason))return false;
        try
        {
            if(new FileInfo(path).LinkTarget!=null) { reason="file-link"; return false; }
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(stream.Length<1 || stream.Length>maximumBytes) { reason="file-size"; return false; }
            var result=new byte[checked((int)stream.Length)];int offset=0;
            while(offset<result.Length){int read=stream.Read(result,offset,result.Length-offset);if(read<=0){reason="file-truncated";return false;}offset+=read;}
            if(stream.ReadByte()!=-1 || !TryPath(folder,filename,out string again,out reason) || again!=path || new FileInfo(path).LinkTarget!=null)
            { reason="file-read-drift"; return false; }
            bytes=result; return true;
        }
        catch(Exception e) when(e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
        { reason="file-read-unknown:"+e.GetType().Name; return false; }
    }

    private static bool TryPath(string folder,string filename,out string path,out string reason)
    {
        path=""; reason="";
        try
        {
            if(string.IsNullOrEmpty(folder) || !Path.IsPathFullyQualified(folder)
                || Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder))!=folder
                || string.IsNullOrEmpty(filename) || filename=="." || filename==".."
                || filename.IndexOfAny(new[]{'/','\\',':','\0'})>=0 || Path.GetFileName(filename)!=filename)
            { reason="path-or-leaf"; return false; }
            for(DirectoryInfo parent=new DirectoryInfo(folder);parent!=null;parent=parent.Parent)
                if(!parent.Exists || parent.LinkTarget!=null || (parent.Attributes&FileAttributes.ReparsePoint)!=0)
                { reason="parent-missing-or-link"; return false; }
            path=Path.Combine(folder,filename);
            if(Path.GetDirectoryName(path)!=folder || Path.GetFullPath(path)!=path) { reason="path-escape"; return false; }
            return true;
        }
        catch(Exception e) when(e is IOException || e is ArgumentException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
        { reason="path-unknown:"+e.GetType().Name; return false; }
    }
}
