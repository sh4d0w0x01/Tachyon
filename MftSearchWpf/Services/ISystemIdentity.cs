using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace MftSearchWpf.Services
{
    public interface ISystemIdentity
    {
        bool IsWindowsOS();
        bool IsAdministratorRole();
    }

    public class SystemIdentity : ISystemIdentity
    {
        public bool IsWindowsOS()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

        public bool IsAdministratorRole()
        {
#pragma warning disable CA1416
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
#pragma warning restore CA1416
        }
    }
}
