using HavenOS.Apps.MiniComputer;

static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
var directory=Path.Combine(Path.GetTempPath(),"9to1-mini-"+Guid.NewGuid());
try
{
 var path=Path.Combine(directory,"catalog.json");var store=new JsonMiniComputerCatalogStore(path);
 var empty=await store.ReadAsync(default);Check(empty.SchemaVersion==1&&empty.VirtualMachines.Length==0,"absent catalog starts empty");
 var provider=ProviderId.New();await store.WriteAsync(empty with {DefaultProviderID=provider},default);
 Check((await new JsonMiniComputerCatalogStore(path).ReadAsync(default)).DefaultProviderID==provider,"provider identity survives reopen");
 var original=await File.ReadAllTextAsync(path);var future=original.Replace("\"schemaVersion\": 1","\"schemaVersion\": 99");
 await File.WriteAllTextAsync(path,future);
 try{await store.ReadAsync(default);throw new Exception("future schema accepted");}catch(InvalidDataException){}
 Check(await File.ReadAllTextAsync(path)==future,"unknown schema preserved byte for byte");
 try{await store.WriteAsync(empty with {SchemaVersion=99},default);throw new Exception("unknown schema written");}catch(InvalidDataException){}
 Check(await File.ReadAllTextAsync(path)==future,"rejected write leaves prior catalog");
 Console.WriteLine("PASS MiniComputer durable provider identity, unknown-schema read/write rejection and preservation");
}
finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
