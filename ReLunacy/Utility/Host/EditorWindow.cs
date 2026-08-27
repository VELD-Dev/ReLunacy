using SDL3;
using Veldrith;

namespace ReLunacy.Utility;

/// <summary>The application's OS window, and the graphics device drawing into it.
///
/// A thin wrapper over SDL3: create the window, pump its event queue (handing every event to
/// <see cref="Input"/>), and hand Veldrith the native handles it needs for a swapchain.</summary>
public sealed class EditorWindow : IDisposable
{
    private nint _handle;

    /// <summary>False once the window has been closed, which is what ends the main loop.</summary>
    public bool Exists { get; private set; }

    public event Action? Resized;

    public nint Handle => _handle;

    private EditorWindow(nint handle)
    {
        _handle = handle;
        Exists = true;
    }

    /// <summary>Opens the window and creates a graphics device with a swapchain onto it.</summary>
    /// <exception cref="PlatformNotSupportedException">The requested backend is not available here.</exception>
    public static EditorWindow Create(
        int width, int height, string title, GraphicsDeviceOptions options,
        GraphicsBackend preferredBackend, out GraphicsDevice graphicsDevice)
    {
        if (!GraphicsDevice.IsBackendSupported(preferredBackend))
            throw new PlatformNotSupportedException($"The graphics backend [{preferredBackend}] is not supported on this platform.");

        if (!SDL.Init(SDL.InitFlags.Video | SDL.InitFlags.Events))
            throw new InvalidOperationException($"SDL_Init failed: {SDL.GetError()}");

        // The backend flag has to be on the window at CREATION time: SDL picks the surface type then,
        // and a window made without it cannot be handed to Vulkan afterwards.
        // Deliberately NOT HighPixelDensity. With it, the drawable is larger than the window in desktop
        // coordinates, while SDL keeps reporting the cursor in the smaller one: every framebuffer here
        // is sized in pixels and every hit test compares against ImGui's display size, so the two spaces
        // have to stay the same one. Supporting a scaled display means converting at the input boundary,
        // not just asking for the bigger surface.
        var flags = SDL.WindowFlags.Resizable | preferredBackend switch
        {
            GraphicsBackend.Vulkan => SDL.WindowFlags.Vulkan,
            GraphicsBackend.Metal => SDL.WindowFlags.Metal,
            _ => 0,
        };

        nint handle = SDL.CreateWindow(title, width, height, flags);
        if (handle == nint.Zero)
            throw new InvalidOperationException($"SDL_CreateWindow failed: {SDL.GetError()}");

        var window = new EditorWindow(handle);
        graphicsDevice = window.CreateGraphicsDevice(options, preferredBackend);
        return window;
    }

    private GraphicsDevice CreateGraphicsDevice(GraphicsDeviceOptions options, GraphicsBackend backend)
    {
        var (w, h) = GetSizeInPixels();
        var description = new SwapchainDescription(
            CreateSwapchainSource(), (uint)w, (uint)h,
            options.SwapchainDepthFormat, options.SyncToVerticalBlank, options.SwapchainSrgbFormat);

        return backend switch
        {
            GraphicsBackend.Vulkan => GraphicsDevice.CreateVulkan(options, description),
            GraphicsBackend.Direct3D12 => GraphicsDevice.CreateD3D12(options, description),
            GraphicsBackend.Metal => GraphicsDevice.CreateMetal(options, description),
            _ => throw new VeldridException($"Invalid GraphicsBackend: [{backend}]"),
        };
    }

    /// <summary>The platform-native handles behind this window, in the shape Veldrith wants.
    ///
    /// SDL exposes them as window "properties" rather than as typed accessors, which is why this reads
    /// like a lookup table. Wayland is checked before X11 because a session running XWayland reports
    /// both, and the native one is the right answer.</summary>
    private SwapchainSource CreateSwapchainSource()
    {
        uint props = SDL.GetWindowProperties(_handle);

        if (OperatingSystem.IsWindows())
        {
            nint hwnd = SDL.GetPointerProperty(props, SDL.Props.WindowWin32HWNDPointer, nint.Zero);
            nint hinstance = SDL.GetPointerProperty(props, SDL.Props.WindowWin32InstancePointer, nint.Zero);
            if (hwnd != nint.Zero)
                return SwapchainSource.CreateWin32(hwnd, hinstance);
        }
        else if (OperatingSystem.IsMacOS())
        {
            nint nsWindow = SDL.GetPointerProperty(props, SDL.Props.WindowCocoaWindowPointer, nint.Zero);
            if (nsWindow != nint.Zero)
                return SwapchainSource.CreateNSWindow(nsWindow);
        }
        else
        {
            nint wlDisplay = SDL.GetPointerProperty(props, SDL.Props.WindowWaylandDisplayPointer, nint.Zero);
            nint wlSurface = SDL.GetPointerProperty(props, SDL.Props.WindowWaylandSurfacePointer, nint.Zero);
            if (wlDisplay != nint.Zero && wlSurface != nint.Zero)
                return SwapchainSource.CreateWayland(wlDisplay, wlSurface);

            nint x11Display = SDL.GetPointerProperty(props, SDL.Props.WindowX11DisplayPointer, nint.Zero);
            long x11Window = SDL.GetNumberProperty(props, SDL.Props.WindowX11WindowNumber, 0);
            if (x11Display != nint.Zero && x11Window != 0)
                return SwapchainSource.CreateXlib(x11Display, (nint)x11Window);
        }

        throw new PlatformNotSupportedException("Could not find a native window handle SDL and Veldrith agree on.");
    }

    /// <summary>Size of the drawable surface, NOT of the window in desktop coordinates. The two differ
    /// on a scaled display, and every framebuffer here is sized in real pixels.</summary>
    public (int Width, int Height) GetSizeInPixels()
    {
        SDL.GetWindowSizeInPixels(_handle, out int w, out int h);
        return (Math.Max(1, w), Math.Max(1, h));
    }

    public int GetWidth() => GetSizeInPixels().Width;
    public int GetHeight() => GetSizeInPixels().Height;

    public void SetTitle(string title) => SDL.SetWindowTitle(_handle, title);

    /// <summary>Sets the taskbar/titlebar icon. SDL copies the pixels into its own surface, so the
    /// caller's image can be released straight afterwards.</summary>
    public unsafe void SetIcon(Engine.Rendering.Resources.Image icon)
    {
        fixed (byte* pixels = icon.Data)
        {
            nint surface = SDL.CreateSurfaceFrom(
                icon.Width, icon.Height, SDL.PixelFormat.ABGR8888, (nint)pixels, icon.Width * 4);
            if (surface == nint.Zero) return;
            SDL.SetWindowIcon(_handle, surface);
            SDL.DestroySurface(surface);
        }
    }

    /// <summary>Drains SDL's event queue into <see cref="Input"/> and this window's own state. Call once
    /// per frame, before anything reads input.</summary>
    public void PumpEvents()
    {
        SDL.PumpEvents();
        while (SDL.PollEvent(out var e))
        {
            switch ((SDL.EventType)e.Type)
            {
                case SDL.EventType.Quit:
                case SDL.EventType.WindowCloseRequested:
                    Exists = false;
                    break;

                // Pixel size, not window size: on a scaled display only this one tracks the framebuffer,
                // and a WindowResized alone would leave every target sized for the wrong surface.
                case SDL.EventType.WindowPixelSizeChanged:
                    Resized?.Invoke();
                    break;

                // Focus loss has to clear the key state. The OS stops delivering key-up events to an
                // unfocused window, so a key held while alt-tabbing away would otherwise stay down
                // forever.
                case SDL.EventType.WindowFocusLost:
                    Input.ClearState();
                    break;

                default:
                    Input.ProcessEvent(e);
                    break;
            }
        }
    }

    public void Dispose()
    {
        if (_handle == nint.Zero) return;
        SDL.DestroyWindow(_handle);
        _handle = nint.Zero;
        Exists = false;
        SDL.Quit();
    }
}
