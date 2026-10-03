using System.Text.Json;
using Avalonia.Controls;
using Haven.Core.Forms;

namespace HavenOS.Forms;

/// <summary>Native presentation capability only. Forms services retain validation,
/// authentication, publication/response CAS and marking; an input never grants resource access.</summary>
public interface IFormNativeMathematicsProvider
{
    bool Supports(FormField field);
    IFormNativeMathematicsInput Create(FormField field, JsonElement? answer, Action<JsonElement> changed);
}
public interface IFormNativeMathematicsInput : IDisposable
{
    Control Control { get; }
}
