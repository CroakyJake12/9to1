using System.Runtime.CompilerServices;

// Narrow composition boundary: the desktop layer (assembly 'Haven') owns the native
// app-content registry and its app bindings, so it is the only party allowed to issue a
// BrowseNativeAdmissionRefusal missing-binding proof. Browse never accepts a self-claimed
// marker from an ordinary app-content factory, including a test or app-supplied factory.
[assembly: InternalsVisibleTo("Haven")]
