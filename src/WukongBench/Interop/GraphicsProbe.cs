using System.Runtime.InteropServices;
using WukongBench.Core;

namespace WukongBench.Interop;

/// <summary>DXGI 1.1 adapter enumeration and actual D3D12 OPTIONS5 capability query; no GPU-name heuristics.</summary>
internal static class GraphicsProbe
{
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(in Guid iid, out nint factory);
    [DllImport("d3d12.dll")] private static extern int D3D12CreateDevice(nint adapter, int minimumLevel, in Guid iid, out nint device);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters(nint self, uint index, out nint adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDescription(nint self, out AdapterDescription description);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CheckFeature(nint self, int feature, ref Options5 data, uint size);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow; public int LuidHigh; public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Options5
    { public int SrvOnlyTiledResourceTier3, RenderPassesTier, RaytracingTier; }
    private static T Method<T>(nint obj, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size));

    public static IReadOnlyList<GpuInfo> Read()
    {
        var factoryIid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(factoryIid, out var factory));
        var result = new List<GpuInfo>();
        try
        {
            // IDXGIFactory1::EnumAdapters1 follows 12 inherited vtable methods.
            var enumerate = Method<EnumAdapters>(factory, 12);
            for (uint index = 0; ; index++)
            {
                var hr = enumerate(factory, index, out var adapter);
                if (hr == unchecked((int)0x887A0002)) break;
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    Marshal.ThrowExceptionForHR(Method<GetDescription>(adapter, 10)(adapter, out var desc));
                    if ((desc.Flags & 2) != 0) continue; // DXGI_ADAPTER_FLAG_SOFTWARE
                    result.Add(new((int)index, desc.Description.TrimEnd('\0'), (ulong)desc.DedicatedVideoMemory, RayTracing(adapter)));
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }
        return result;
    }

    private static bool? RayTracing(nint adapter)
    {
        var deviceIid = new Guid("189819f1-1db6-4b57-be54-1821339b85f7");
        try
        {
            if (D3D12CreateDevice(adapter, 0xb000, deviceIid, out var device) < 0) return false;
            try
            {
                var options = new Options5();
                // ID3D12Device::CheckFeatureSupport, D3D12_FEATURE_D3D12_OPTIONS5 = 27.
                var hr = Method<CheckFeature>(device, 13)(device, 27, ref options, (uint)Marshal.SizeOf<Options5>());
                return hr >= 0 ? options.RaytracingTier != 0 : null;
            }
            finally { Marshal.Release(device); }
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
    }
}
