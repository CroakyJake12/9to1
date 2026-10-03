using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
var output=new List<object>();
foreach(var file in args){
 using var pe=new PEReader(File.OpenRead(file));var mr=pe.GetMetadataReader();var pdb=Path.ChangeExtension(file,".pdb");
 using var provider=MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(pdb));var pr=provider.GetMetadataReader();var id=pr.DebugMetadataHeader!.Id.ToArray();
 var entries=pe.ReadDebugDirectory();var entry=entries.Single(x=>x.Type==DebugDirectoryEntryType.CodeView);var cv=pe.ReadCodeViewDebugDirectoryData(entry);
 var docs=pr.Documents.Select(h=>{var d=pr.GetDocument(h);return new {path=pr.GetString(d.Name),algorithm=pr.GetGuid(d.HashAlgorithm),hash=Convert.ToHexString(pr.GetBlobBytes(d.Hash)).ToLowerInvariant()};}).ToArray();
 output.Add(new {file,dllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant(),pdb,pdbSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pdb))).ToLowerInvariant(),mvid=mr.GetGuid(mr.GetModuleDefinition().Mvid),codeViewGuid=cv.Guid,codeViewStamp=entry.Stamp,pdbGuid=new Guid(id.Take(16).ToArray()),pdbStamp=BitConverter.ToUInt32(id,16),pairMatches=cv.Guid==new Guid(id.Take(16).ToArray())&&entry.Stamp==BitConverter.ToUInt32(id,16),documents=docs});
}
Console.WriteLine(JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}));
