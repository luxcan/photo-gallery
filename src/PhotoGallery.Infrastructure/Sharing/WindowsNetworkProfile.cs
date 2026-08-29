using System.Runtime.InteropServices;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Infrastructure.Sharing;

/// <summary>
/// Asks Windows what it thinks of the networks this machine is on.
/// </summary>
/// <remarks>
/// Through the Network List Manager, which is the same thing the "Public
/// network / Private network" switch in Settings writes to. There is no managed
/// API for it, and the alternatives are worse: parsing the output of a
/// PowerShell cmdlet, or reading a registry key whose layout Microsoft has never
/// documented.
///
/// <para>Answers false when it cannot tell. A wrong "your network is Public"
/// sends somebody to change a setting that was not the problem; a wrong nothing
/// leaves them where they already were, with a typed address still offered.
/// </para>
/// </remarks>
public sealed class WindowsNetworkProfile : INetworkProfile
{
    private const int NlmConnectivityIpv4Internet = 0x40;
    private const int NlmConnectivityIpv6Internet = 0x400;

    public bool IsPublic()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        object? manager = null;

        try
        {
            Type? type = Type.GetTypeFromCLSID(
                new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"));

            if (type is null)
            {
                return false;
            }

            manager = Activator.CreateInstance(type);

            if (manager is not INetworkListManager networks)
            {
                return false;
            }

            foreach (INetwork network in Connected(networks))
            {
                // NLM_NETWORK_CATEGORY_PUBLIC is 0. Private is 1 and
                // domain-authenticated is 2, and neither blocks the beacon.
                if (network.GetCategory() == 0)
                {
                    return true;
                }
            }

            return false;
        }
        catch (COMException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        finally
        {
            if (manager is not null && Marshal.IsComObject(manager))
            {
                Marshal.ReleaseComObject(manager);
            }
        }
    }

    private static IEnumerable<INetwork> Connected(INetworkListManager networks)
    {
        // NLM_ENUM_NETWORK_CONNECTED is 1: the ones this machine is actually on,
        // rather than every network it has ever joined. The result is an
        // IEnumVARIANT behind an object, so it is walked rather than iterated.
        if (networks.GetNetworks(1) is not System.Collections.IEnumerable found)
        {
            yield break;
        }

        foreach (object? entry in found)
        {
            if (entry is INetwork network && network.IsConnected)
            {
                yield return network;
            }
        }
    }

    [ComImport]
    [Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface INetworkListManager
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetNetworks(int flags);
    }

    [ComImport]
    [Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface INetwork
    {
        bool IsConnected { get; }

        int GetCategory();
    }
}
