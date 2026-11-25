using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;

namespace Pulse {
internal sealed class MessageHistory {
    readonly string path;
    internal DateTimeOffset ClearedThrough{get;private set;}
    readonly DataContractSerializer format=new DataContractSerializer(typeof(List<IslandMessage>));
    internal MessageHistory(string location){
        path=location??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ChengJing","Pulse","notifications.dat");
    }
    internal List<IslandMessage> Load(){
        try{
            if(File.Exists(path)){
                var bytes=ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser);
                using(var stream=new MemoryStream(bytes)){
                    // Original records contain only the serialized XML list.
                    if(bytes.Length>=9 && bytes[0]==1){
                        stream.Position=1;
                        using(var reader=new BinaryReader(stream,System.Text.Encoding.UTF8,true))
                            ClearedThrough=new DateTimeOffset(reader.ReadInt64(),TimeSpan.Zero);
                    }
                    return (List<IslandMessage>)format.ReadObject(stream);
                }
            }
        }catch(IOException){}catch(UnauthorizedAccessException){}catch(CryptographicException){}catch(SerializationException){}
        return new List<IslandMessage>();
    }
    internal bool Clear(){
        var cutoff=DateTimeOffset.UtcNow;
        if(!Write(new List<IslandMessage>(),cutoff))return false;
        ClearedThrough=cutoff;return true;
    }
    internal void Save(List<IslandMessage> messages){Write(messages,ClearedThrough);}
    bool Write(List<IslandMessage> messages,DateTimeOffset cutoff){
        try{
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using(var stream=new MemoryStream()){
                stream.WriteByte(1);
                using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))writer.Write(cutoff.UtcTicks);
                format.WriteObject(stream,messages);
                File.WriteAllBytes(path+".tmp",ProtectedData.Protect(stream.ToArray(),null,DataProtectionScope.CurrentUser));
            }
            if(File.Exists(path))File.Replace(path+".tmp",path,null);else File.Move(path+".tmp",path);
            return true;
        }catch(IOException){}catch(UnauthorizedAccessException){}catch(CryptographicException){}
        return false;
    }
}
}
