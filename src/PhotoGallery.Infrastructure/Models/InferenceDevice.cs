using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;

namespace PhotoGallery.Infrastructure.Models;

/// <summary>
/// What the model graphs run on, decided once for the life of the process.
/// </summary>
/// <remarks>
/// Both model phases used to run on the processor because nothing ever asked for
/// anything else, and on this library that is around three quarters of an hour
/// of a scan: CLIP ViT-L/14 is a 1.16 GB graph and every new photograph goes
/// through it once, with the face detector and the recogniser behind it. A
/// machine with a graphics card was doing all of it on eleven cores while the
/// card sat idle.
///
/// <para><b>Which adapter.</b> DirectML takes a device index, and index zero is
/// whatever Direct3D enumerates first - on a laptop with switchable graphics
/// that is usually the integrated one, which can be slower than the eleven cores
/// it replaced. Choosing it by luck would make this change a regression on
/// exactly the machines it is meant to help, so the adapters are enumerated and
/// the one with the most dedicated video memory wins. That is DXGI, reached by
/// P/Invoke rather than by taking a graphics package for two calls.</para>
///
/// <para><b>How many at once.</b> The work lists run eleven pictures in parallel,
/// which is right for eleven single-threaded graphs on a processor and wrong for
/// one card: every concurrent run allocates its own activations, and eleven of
/// them through a graph this size will exhaust the memory on a laptop card. The
/// limit belongs here rather than in the handlers - the card is the scarce
/// thing, and the list of work has no business knowing about it.</para>
///
/// <para><b>Falling back.</b> Every failure here ends at the processor, because a
/// scan that is slower than it could be is an inconvenience and a scan that
/// throws is a broken application. A machine with no Direct3D 12 adapter, a
/// driver that refuses, or an adapter too small for the graph all take the same
/// path as before this existed.</para>
/// </remarks>
public static class InferenceDevice
{
    /// <summary>Set this to <c>cpu</c> to keep the graphs off the card.</summary>
    /// <remarks>
    /// Here for two reasons: a driver that turns out to be wrong needs a way back
    /// that is not a rebuild, and measuring one against the other needs a way to
    /// ask for each in turn.
    /// </remarks>
    public const string Variable = "PHOTOGALLERY_INFERENCE";

    /// <summary>
    /// Below this there is no point: the visual graph alone is 1.16 GB, and an
    /// adapter that cannot hold it will either refuse or thrash.
    /// </summary>
    private const long LeastUsefulMemory = 2L * 1024 * 1024 * 1024;

    private static readonly Lock s_gate = new();
    private static Choice? s_choice;
    private static SemaphoreSlim? s_queue;

    /// <summary>How many graphs may run at once, given what they run on.</summary>
    /// <remarks>
    /// ONE on a card, and this is a correctness limit rather than a tuning
    /// choice. A DirectML session records its work through per-session state, and
    /// calling Run on it from two threads at once corrupts that state: with two
    /// it threw <c>DmlCommandRecorder.cpp(342) 80004005</c> about fifty pictures
    /// into a real scan, and with four the process died outright with an access
    /// violation. An access violation cannot be caught, so no amount of handling
    /// further up would have saved the pass - the only safe number is one.
    ///
    /// <para>It costs less than it sounds. One picture at a time through the card
    /// is still around thirty a second against the five a second eleven cores
    /// managed, because the card is that much faster per picture.</para>
    ///
    /// <para>On the processor this is deliberately large: it does not limit
    /// anything, because the work lists already cap themselves at half the cores
    /// and each session there is single-threaded and safe to share.</para>
    /// </remarks>
    public static int Concurrency => Decide().OnGpu ? 1 : int.MaxValue;

    /// <summary>What was chosen, for the log line that says so.</summary>
    public static string Description => Decide().Description;

    /// <summary>
    /// Holds one of the card's slots for the length of one graph run, and does
    /// nothing at all on the processor.
    /// </summary>
    /// <remarks>
    /// The work lists run eleven pictures at once and must not be changed to know
    /// about hardware, so the queue is here: on a card the eleventh caller waits,
    /// on a processor every caller walks straight through. A struct so the
    /// processor path allocates nothing on a per-picture call.
    /// </remarks>
    public static Slot Enter(CancellationToken cancellationToken)
    {
        SemaphoreSlim? gate = Queue;
        if (gate is null)
        {
            return default;
        }

        gate.Wait(cancellationToken);
        return new Slot(gate);
    }

    private static SemaphoreSlim? Queue
    {
        get
        {
            if (!Decide().OnGpu)
            {
                return null;
            }

            lock (s_gate)
            {
                return s_queue ??= new SemaphoreSlim(Concurrency, Concurrency);
            }
        }
    }

    /// <summary>One slot on whatever the graphs are running on.</summary>
    public readonly struct Slot(SemaphoreSlim? held) : IDisposable
    {
        public void Dispose() => held?.Release();
    }

    /// <summary>Options for one graph, on whatever was chosen.</summary>
    public static SessionOptions ForGraph()
    {
        Choice choice = Decide();
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };

        if (choice.OnGpu)
        {
            try
            {
                // DirectML does its own memory management and does not support
                // the memory pattern planner; ORT throws at Run if it is left on.
                options.EnableMemoryPattern = false;
                options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                options.AppendExecutionProvider_DML(choice.DeviceId);
                return options;
            }
            catch (Exception ex) when (ex is OnnxRuntimeException or DllNotFoundException
                                           or EntryPointNotFoundException)
            {
                // The adapter was found and the provider still would not attach.
                // Remember that, so the next graph does not try again and the
                // description stops claiming a card.
                Remember(Choice.Processor($"the processor - DirectML refused ({ex.GetType().Name})"));
                options.Dispose();
                return ForGraph();
            }
        }

        // One thread per graph: the pass already runs several pictures at once,
        // and letting each session spread over every core as well would leave the
        // machine competing with itself. Parallelism belongs to whoever is
        // holding the work list.
        options.IntraOpNumThreads = 1;
        options.InterOpNumThreads = 1;
        return options;
    }

    private static Choice Decide()
    {
        lock (s_gate)
        {
            return s_choice ??= Choose();
        }
    }

    private static void Remember(Choice choice)
    {
        lock (s_gate)
        {
            s_choice = choice;
        }
    }

    private static Choice Choose()
    {
        string? asked = Environment.GetEnvironmentVariable(Variable);
        if (string.Equals(asked, "cpu", StringComparison.OrdinalIgnoreCase))
        {
            return Choice.Processor($"the processor, because {Variable} asks for it");
        }

        try
        {
            (int index, string name, long memory)? best = BestAdapter();

            return best is null
                ? Choice.Processor("the processor - no adapter with enough memory")
                : new Choice(
                    true,
                    best.Value.index,
                    $"{best.Value.name} ({best.Value.memory / (1024 * 1024 * 1024d):N1} GB)");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException
                                       or COMException)
        {
            return Choice.Processor($"the processor - no Direct3D ({ex.GetType().Name})");
        }
    }

    /// <summary>
    /// The Direct3D adapter with the most memory of its own, and the index
    /// DirectML knows it by.
    /// </summary>
    /// <remarks>
    /// Dedicated memory rather than shared: shared memory is system memory an
    /// integrated adapter borrows, so counting it would rank the integrated
    /// adapter above the discrete one on every laptop - which is the exact
    /// mistake this method exists to avoid.
    /// </remarks>
    private static (int Index, string Name, long Memory)? BestAdapter()
    {
        int hr = CreateDXGIFactory1(typeof(IDXGIFactory1).GUID, out object factoryObject);
        if (hr != 0)
        {
            return null;
        }

        var factory = (IDXGIFactory1)factoryObject;
        (int Index, string Name, long Memory)? best = null;

        try
        {
            for (uint index = 0; factory.EnumAdapters1(index, out IDXGIAdapter1 adapter) == 0; index++)
            {
                try
                {
                    adapter.GetDesc1(out AdapterDescription description);

                    // Skip the software renderer: DXGI_ADAPTER_FLAG_SOFTWARE. It
                    // enumerates like a real adapter and would be slower than the
                    // processor it is pretending to be.
                    bool software = (description.Flags & 2) != 0;
                    long memory = (long)description.DedicatedVideoMemory;

                    if (!software && memory >= LeastUsefulMemory
                        && (best is null || memory > best.Value.Memory))
                    {
                        best = ((int)index, description.Description.Trim(), memory);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(adapter);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }

        return best;
    }

    private sealed record Choice(bool OnGpu, int DeviceId, string Description)
    {
        public static Choice Processor(string why) => new(false, 0, why);
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(
        in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    [ComImport]
    [Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        // IDXGIObject, IDXGIFactory and IDXGIFactory1 all contribute slots before
        // the one method used here, and a COM interface is its vtable order: the
        // placeholders are not padding, they ARE the layout. Miscount them and
        // the call lands on a different function with a different signature,
        // which does not throw - it returns nonsense. One too many put this on
        // IsCurrent, which reports a boolean as though it were an HRESULT, so
        // enumeration stopped before the first adapter and every machine looked
        // as though it had no graphics card at all.
        //
        // Nine of them: SetPrivateData, SetPrivateDataInterface, GetPrivateData
        // and GetParent from IDXGIObject, then EnumAdapters, MakeWindowAssociation,
        // GetWindowAssociation, CreateSwapChain and CreateSoftwareAdapter from
        // IDXGIFactory. EnumAdapters1 is the first of IDXGIFactory1's own.
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        void EnumAdapters();
        void MakeWindowAssociation();
        void GetWindowAssociation();
        void CreateSwapChain();
        void CreateSoftwareAdapter();
        [PreserveSig] int EnumAdapters1(uint index, out IDXGIAdapter1 adapter);
    }

    [ComImport]
    [Guid("29038f61-3839-4626-91fd-086879011a05")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        // Seven before it, on the same rule as the factory above: four from
        // IDXGIObject, then EnumOutputs, GetDesc and CheckInterfaceSupport from
        // IDXGIAdapter. GetDesc1 is IDXGIAdapter1's own.
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        void EnumOutputs();
        void GetDesc();
        void CheckInterfaceSupport();
        void GetDesc1(out AdapterDescription description);
    }
}
