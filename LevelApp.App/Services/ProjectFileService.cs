using System.Runtime.InteropServices;
using LevelApp.Core.Models;
using LevelApp.Core.Serialization;

namespace LevelApp.App.Services;

/// <summary>
/// Presents native Win32 IFileOpenDialog / IFileSaveDialog pickers (bypassing the
/// WinRT FileOpenPicker / FileSavePicker wrappers) and delegates to
/// <see cref="ProjectSerializer"/> for JSON I/O.
///
/// The WinRT pickers create their underlying COM dialog object lazily inside
/// PickSingleFileAsync / PickSaveFileAsync, so any IFileDialog::SetFolder call made
/// beforehand targets a different COM instance and is ignored.  Driving the COM
/// dialogs directly removes this indirection and gives reliable control over the
/// initial folder.
///
/// Call <see cref="Initialize"/> once from <c>MainWindow</c> after the window handle
/// is available.
/// </summary>
public sealed class ProjectFileService : IProjectFileService
{
    private readonly ISettingsService _settings;
    private nint _hwnd;

    public ProjectFileService(ISettingsService settings) => _settings = settings;

    /// <summary>Stores the owner window handle used to parent file-picker dialogs.</summary>
    public void Initialize(nint hwnd) => _hwnd = hwnd;

    /// <summary>Saves the project to a known path without showing a dialog.</summary>
    public Task SaveToPathAsync(Project project, string path)
    {
        string json = ProjectSerializer.Serialize(project);
        return File.WriteAllTextAsync(path, json);
    }

    /// <summary>
    /// Opens a Save dialog and writes the project as a <c>.levelproj</c> file.
    /// Returns the chosen path, or <c>null</c> if the user cancelled.
    /// </summary>
    public async Task<string?> SaveAsAsync(Project project, string suggestedFileName)
    {
        string? path = await ShowSaveDialogAsync(_hwnd, _settings.DefaultProjectFolder, suggestedFileName);
        if (path is null) return null;
        await File.WriteAllTextAsync(path, ProjectSerializer.Serialize(project));
        return path;
    }

    /// <summary>
    /// Opens an Open dialog and deserialises the selected <c>.levelproj</c> file.
    /// Returns <c>(null, null)</c> if the user cancelled.
    /// </summary>
    public async Task<(Project? project, string? path)> OpenAsync()
    {
        string? path = await ShowOpenDialogAsync(_hwnd, _settings.DefaultProjectFolder);
        if (path is null) return (null, null);
        string json = await File.ReadAllTextAsync(path);
        return (ProjectSerializer.Deserialize(json), path);
    }

    /// <summary>
    /// Auto-generates a suggested filename from the project's object definition.
    /// Format: {GeometryType}_{WidthMm}x{HeightMm}  e.g. <c>SurfacePlate_1200x800</c>
    /// </summary>
    public string SuggestFileName(Project project)
    {
        var def = project.ObjectDefinition;
        string geom = def.GeometryModuleId;
        if (def.Parameters.TryGetValue("widthMm",  out var w) &&
            def.Parameters.TryGetValue("heightMm", out var h))
            return $"{geom}_{Convert.ToInt32(w)}x{Convert.ToInt32(h)}";
        return geom;
    }

    // ── COM dialog helpers ────────────────────────────────────────────────────

    private static Task<string?> ShowOpenDialogAsync(nint hwnd, string initialFolder)
        => RunOnStaThread(() =>
        {
            var clsid = s_clsidFileOpenDialog;
            var iid   = s_iidIFileDialog;
            if (CoCreateInstance(ref clsid, 0, CLSCTX_INPROC_SERVER, ref iid, out nint ptr) != 0)
                return null;

            var dlg = (IFileDialog)Marshal.GetObjectForIUnknown(ptr);
            Marshal.Release(ptr);

            ConfigureFileTypes(dlg);
            SetInitialFolder(dlg, initialFolder);

            if (dlg.Show(hwnd) != 0) return null;   // S_FALSE / cancelled

            dlg.GetResult(out IShellItem item);
            item.GetDisplayName(SIGDN_FILESYSPATH, out string path);
            return path;
        });

    private static Task<string?> ShowSaveDialogAsync(nint hwnd, string initialFolder, string suggestedFileName)
        => RunOnStaThread(() =>
        {
            var clsid = s_clsidFileSaveDialog;
            var iid   = s_iidIFileDialog;
            if (CoCreateInstance(ref clsid, 0, CLSCTX_INPROC_SERVER, ref iid, out nint ptr) != 0)
                return null;

            var dlg = (IFileDialog)Marshal.GetObjectForIUnknown(ptr);
            Marshal.Release(ptr);

            ConfigureFileTypes(dlg);
            dlg.SetDefaultExtension("levelproj");
            dlg.SetFileName(suggestedFileName);
            SetInitialFolder(dlg, initialFolder);

            if (dlg.Show(hwnd) != 0) return null;

            dlg.GetResult(out IShellItem item);
            item.GetDisplayName(SIGDN_FILESYSPATH, out string path);

            // Belt-and-braces: ensure the extension is present.
            if (!path.EndsWith(".levelproj", StringComparison.OrdinalIgnoreCase))
                path += ".levelproj";
            return path;
        });

    private static void ConfigureFileTypes(IFileDialog dlg)
    {
        COMDLG_FILTERSPEC[] filters =
        [
            new() { pszName = "Level Project", pszSpec = "*.levelproj" }
        ];
        dlg.SetFileTypes((uint)filters.Length, filters);
        dlg.SetFileTypeIndex(1);
    }

    private static void SetInitialFolder(IFileDialog dlg, string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        var iid = s_iidIShellItem;
        if (SHCreateItemFromParsingName(folder, 0, ref iid, out nint ptr) != 0) return;
        var item = (IShellItem)Marshal.GetObjectForIUnknown(ptr);
        Marshal.Release(ptr);
        dlg.SetFolder(item);
    }

    /// <summary>
    /// Runs <paramref name="func"/> on a dedicated STA background thread and returns
    /// a Task that completes with its result.  COM file dialogs require an STA thread;
    /// running them on the UI thread would block WinUI 3's dispatcher message loop.
    /// </summary>
    private static Task<string?> RunOnStaThread(Func<string?> func)
    {
        var tcs = new TaskCompletionSource<string?>();
        var thread = new Thread(() =>
        {
            try   { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }

    // ── CLSIDs / IIDs / constants ─────────────────────────────────────────────

    // Both IFileOpenDialog and IFileSaveDialog inherit IFileDialog, so CoCreateInstance
    // with IID_IFileDialog succeeds for either CLSID via implicit QueryInterface.
    private static Guid s_clsidFileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    private static Guid s_clsidFileSaveDialog = new("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B");
    private static Guid s_iidIFileDialog      = new("42F85136-DB7E-439C-85F1-E4075D135FC8");
    private static Guid s_iidIShellItem       = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    private const uint CLSCTX_INPROC_SERVER = 1;
    private const uint SIGDN_FILESYSPATH    = 0x80058000;

    // ── P/Invoke ──────────────────────────────────────────────────────────────

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid rclsid, nint pUnkOuter, uint dwClsContext, ref Guid riid, out nint ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath, nint pbc, ref Guid riid, out nint ppv);

    // ── COM interfaces ────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct COMDLG_FILTERSPEC
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pszName;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszSpec;
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint pbc, ref Guid bhid, ref Guid riid, out nint ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    /// <summary>
    /// Flat COM interface definition for IFileDialog (shobjidl_core.h).
    /// Vtable slots 0–2 are IUnknown (implicit with InterfaceIsIUnknown);
    /// the 24 declared methods below map to vtable slots 3–26.
    /// </summary>
    [ComImport]
    [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig] int Show(nint hwndOwner);                                           // IModalWindow
        void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(nint pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        [PreserveSig] int Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(nint pFilter);
    }
}
