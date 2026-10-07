#if !ANDROID
using System.Runtime.ExceptionServices;
namespace Haven.Desktop.Accounts;
internal static class NativeAccountUiCauses
{
 internal static void Add(List<Exception> errors,Exception error){if(!errors.Any(old=>ReferenceEquals(old,error)))errors.Add(error);}
 internal static async Task JoinOneAsync(Task actual,List<Exception> errors)
 {
  try{await actual.ConfigureAwait(true);}
  catch(Exception error)
  {if(actual.IsFaulted&&actual.Exception is {} compound){foreach(var original in compound.InnerExceptions)Add(errors,original);}else Add(errors,error);}
 }
 internal static void Throw(List<Exception> errors)
 {if(errors.Count==1)ExceptionDispatchInfo.Capture(errors[0]).Throw();if(errors.Count>1)throw new AggregateException("Native account owner and original cleanup failed",errors);}
}

#endif
