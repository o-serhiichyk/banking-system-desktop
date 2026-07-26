using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("BMS WinForm")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("BMS WinForm")]
[assembly: AssemblyCopyright("Copyright ©  2022")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// The audit-trail types are internal, matching the three existing DL types. This grant
// is what makes them reachable from the test project — the fix FINDINGS.md rank 5
// prescribes for its own finding ("a test project, plus InternalsVisibleTo or a
// visibility change"). Assembly-wide on purpose: CustomerDL.totalMoney, the pure
// function rank 5 singles out, is already reachable by a later test without redoing
// this. The assembly is not signed, so the simple name is sufficient.
[assembly: InternalsVisibleTo("BMS.Tests")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("fde005a2-5ee0-4801-a5b4-403b06a6eda3")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Build and Revision Numbers
// by using the '*' as shown below:
// [assembly: AssemblyVersion("1.0.*")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
